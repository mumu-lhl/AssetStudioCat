using System.Buffers.Binary;
using System.Text;

namespace AssetStudio.AppCore.Indexing;

public static class AssetIndexBinaryStorage
{
    private static readonly byte[] Magic = "ASCB"u8.ToArray();
    private const int CurrentVersion = 1;
    public const int EntryByteSize = 82;

    public static async Task SaveAsync(
        string binaryPath,
        IReadOnlyList<AssetIndexEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var tmpPath = binaryPath + ".tmp";
        await using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
        {
            // 1. Collect and deduplicate strings
            var stringToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var strings = new List<string>();

            int GetStringIdx(string? s)
            {
                if (s is null) return -1;
                if (stringToIndex.TryGetValue(s, out var idx)) return idx;
                idx = strings.Count;
                strings.Add(s);
                stringToIndex[s] = idx;
                return idx;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                GetStringIdx(entry.SourcePath);
                GetStringIdx(entry.ObjectSourcePath);
                GetStringIdx(entry.SerializedFile);
                GetStringIdx(entry.TypeName);
                GetStringIdx(entry.Name);
                GetStringIdx(entry.Container);
                GetStringIdx(entry.GameObjectSerializedFile);
                GetStringIdx(entry.ParentTransformSerializedFile);
            }

            // 2. Write Header
            using var headerWriter = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            headerWriter.Write(Magic);
            headerWriter.Write(CurrentVersion);
            headerWriter.Write((long)entries.Count);
            headerWriter.Write(strings.Count);

            // 3. Write String Table
            foreach (var s in strings)
            {
                var utf8Bytes = Encoding.UTF8.GetBytes(s);
                headerWriter.Write(utf8Bytes.Length);
                headerWriter.Write(utf8Bytes);
            }
            headerWriter.Flush();

            // 4. Write Entries Table in batches
            const int batchSize = 1024;
            var buffer = new byte[batchSize * EntryByteSize];
            var total = entries.Count;
            var entryIndex = 0;

            while (entryIndex < total)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var countInBatch = Math.Min(batchSize, total - entryIndex);
                var span = buffer.AsSpan(0, countInBatch * EntryByteSize);

                for (var b = 0; b < countInBatch; b++)
                {
                    var entry = entries[entryIndex + b];
                    var itemSpan = span.Slice(b * EntryByteSize, EntryByteSize);

                    BinaryPrimitives.WriteInt64LittleEndian(itemSpan[0..8], entry.Id);
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[8..12], GetStringIdx(entry.SourcePath));
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[12..16], GetStringIdx(entry.ObjectSourcePath));
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[16..20], GetStringIdx(entry.SerializedFile));
                    BinaryPrimitives.WriteInt64LittleEndian(itemSpan[20..28], entry.PathId);
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[28..32], entry.ClassId);
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[32..36], GetStringIdx(entry.TypeName));
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[36..40], GetStringIdx(entry.Name));
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[40..44], GetStringIdx(entry.Container));
                    BinaryPrimitives.WriteInt64LittleEndian(itemSpan[44..52], entry.ByteStart);
                    BinaryPrimitives.WriteUInt32LittleEndian(itemSpan[52..56], entry.ByteSize);

                    itemSpan[56] = entry.GameObjectPathId.HasValue ? (byte)1 : (byte)0;
                    BinaryPrimitives.WriteInt64LittleEndian(itemSpan[57..65], entry.GameObjectPathId.GetValueOrDefault());
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[65..69], GetStringIdx(entry.GameObjectSerializedFile));

                    itemSpan[69] = entry.ParentTransformPathId.HasValue ? (byte)1 : (byte)0;
                    BinaryPrimitives.WriteInt64LittleEndian(itemSpan[70..78], entry.ParentTransformPathId.GetValueOrDefault());
                    BinaryPrimitives.WriteInt32LittleEndian(itemSpan[78..82], GetStringIdx(entry.ParentTransformSerializedFile));
                }

                await stream.WriteAsync(buffer.AsMemory(0, countInBatch * EntryByteSize), cancellationToken);
                entryIndex += countInBatch;
            }

            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tmpPath, binaryPath, overwrite: true);
    }

    public static async Task<AssetIndexEntry[]?> TryLoadAsync(
        string binaryPath,
        long expectedEntryCount,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(binaryPath)) return null;

        try
        {
            await using var stream = new FileStream(binaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, useAsync: true);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var magic = reader.ReadBytes(4);
            if (!magic.AsSpan().SequenceEqual(Magic)) return null;

            var version = reader.ReadInt32();
            if (version != CurrentVersion) return null;

            var entryCount = reader.ReadInt64();
            if (entryCount != expectedEntryCount || entryCount > int.MaxValue) return null;

            var stringCount = reader.ReadInt32();
            if (stringCount < 0) return null;

            var stringTable = new string[stringCount];
            for (var i = 0; i < stringCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var len = reader.ReadInt32();
                if (len == 0)
                {
                    stringTable[i] = string.Empty;
                }
                else
                {
                    var bytes = reader.ReadBytes(len);
                    stringTable[i] = Encoding.UTF8.GetString(bytes);
                }
            }

            string? GetStr(int idx) => idx >= 0 && idx < stringTable.Length ? stringTable[idx] : null;
            string GetNonNullStr(int idx) => idx >= 0 && idx < stringTable.Length ? stringTable[idx] : string.Empty;

            var count = (int)entryCount;
            var entries = new AssetIndexEntry[count];

            const int batchSize = 1024;
            var buffer = new byte[batchSize * EntryByteSize];
            var entryIndex = 0;

            while (entryIndex < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var countInBatch = Math.Min(batchSize, count - entryIndex);
                var bytesToRead = countInBatch * EntryByteSize;
                await stream.ReadExactlyAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

                var span = buffer.AsSpan(0, bytesToRead);
                for (var b = 0; b < countInBatch; b++)
                {
                    var itemSpan = span.Slice(b * EntryByteSize, EntryByteSize);

                    var id = BinaryPrimitives.ReadInt64LittleEndian(itemSpan[0..8]);
                    var sourcePath = GetNonNullStr(BinaryPrimitives.ReadInt32LittleEndian(itemSpan[8..12]));
                    var objectSourcePath = GetNonNullStr(BinaryPrimitives.ReadInt32LittleEndian(itemSpan[12..16]));
                    var serializedFile = GetNonNullStr(BinaryPrimitives.ReadInt32LittleEndian(itemSpan[16..20]));
                    var pathId = BinaryPrimitives.ReadInt64LittleEndian(itemSpan[20..28]);
                    var classId = BinaryPrimitives.ReadInt32LittleEndian(itemSpan[28..32]);
                    var typeName = GetNonNullStr(BinaryPrimitives.ReadInt32LittleEndian(itemSpan[32..36]));
                    var name = GetNonNullStr(BinaryPrimitives.ReadInt32LittleEndian(itemSpan[36..40]));
                    var container = GetStr(BinaryPrimitives.ReadInt32LittleEndian(itemSpan[40..44]));
                    var byteStart = BinaryPrimitives.ReadInt64LittleEndian(itemSpan[44..52]);
                    var byteSize = BinaryPrimitives.ReadUInt32LittleEndian(itemSpan[52..56]);

                    var hasGoPath = itemSpan[56] != 0;
                    var goPathId = BinaryPrimitives.ReadInt64LittleEndian(itemSpan[57..65]);
                    var goFile = GetStr(BinaryPrimitives.ReadInt32LittleEndian(itemSpan[65..69]));

                    var hasParentPath = itemSpan[69] != 0;
                    var parentPathId = BinaryPrimitives.ReadInt64LittleEndian(itemSpan[70..78]);
                    var parentFile = GetStr(BinaryPrimitives.ReadInt32LittleEndian(itemSpan[78..82]));

                    entries[entryIndex + b] = new AssetIndexEntry(
                        id,
                        sourcePath,
                        objectSourcePath,
                        serializedFile,
                        pathId,
                        classId,
                        typeName,
                        name,
                        container,
                        byteStart,
                        byteSize,
                        hasGoPath ? goPathId : null,
                        goFile,
                        hasParentPath ? parentPathId : null,
                        parentFile);
                }

                entryIndex += countInBatch;
            }

            return entries;
        }
        catch
        {
            return null;
        }
    }
}
