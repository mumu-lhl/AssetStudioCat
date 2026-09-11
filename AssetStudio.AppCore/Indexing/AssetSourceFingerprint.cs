using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AssetStudio.AppCore.Indexing;

public sealed record AssetSourceFingerprint(string RootPath, int FileCount, long TotalBytes, string Digest)
{
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
}
