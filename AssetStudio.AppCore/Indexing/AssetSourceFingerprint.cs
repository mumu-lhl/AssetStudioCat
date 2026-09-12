using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AssetStudio.AppCore.Indexing;

public sealed record AssetSourceFingerprint(
    string RootPath,
    int FileCount,
    long TotalBytes,
    string Digest,
    string? SourceKey = null)
{
    [JsonIgnore]
    public string IndexKey => SourceKey ?? RootPath;

    public static AssetSourceFingerprint Create(string sourcePath, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(sourcePath);
        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            : new[] { root };

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        long totalBytes = 0;
        Span<byte> numbers = stackalloc byte[16];

        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            var relativePath = Directory.Exists(root) ? Path.GetRelativePath(root, file) : info.Name;
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath.Replace('\\', '/')));
            BinaryPrimitives.WriteInt64LittleEndian(numbers[..8], info.Length);
            BinaryPrimitives.WriteInt64LittleEndian(numbers[8..], info.LastWriteTimeUtc.Ticks);
            hash.AppendData(numbers);
            count++;
            totalBytes += info.Length;
        }

        return new AssetSourceFingerprint(root, count, totalBytes, Convert.ToHexString(hash.GetHashAndReset()));
    }

    public static AssetSourceFingerprint CreateFiles(
        IEnumerable<string> sourceFiles,
        CancellationToken cancellationToken = default)
    {
        var files = sourceFiles
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new ArgumentException("At least one source file is required.", nameof(sourceFiles));
        }
        if (files.Length == 1)
        {
            return Create(files[0], cancellationToken);
        }

        var selectionIdentity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', files.Select(file => file.Replace('\\', '/'))))));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long totalBytes = 0;
        Span<byte> numbers = stackalloc byte[16];
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if (!info.Exists)
            {
                throw new FileNotFoundException("A selected source file no longer exists.", file);
            }
            hash.AppendData(Encoding.UTF8.GetBytes(file.Replace('\\', '/')));
            BinaryPrimitives.WriteInt64LittleEndian(numbers[..8], info.Length);
            BinaryPrimitives.WriteInt64LittleEndian(numbers[8..], info.LastWriteTimeUtc.Ticks);
            hash.AppendData(numbers);
            totalBytes += info.Length;
        }
        var digest = Convert.ToHexString(hash.GetHashAndReset());
        var root = Path.GetDirectoryName(files[0])!;
        var sourceKey = Path.Combine(root, $".assetstudiocat-selection-{selectionIdentity[..24]}");
        return new AssetSourceFingerprint(root, files.Length, totalBytes, digest, sourceKey);
    }
}
