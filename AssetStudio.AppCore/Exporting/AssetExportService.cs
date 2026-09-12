using System.Buffers;
using System.Text;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using global::AssetStudio;

namespace AssetStudio.AppCore.Exporting;

public sealed class AssetExportService
{
    private const int CopyBufferSize = 128 * 1024;
    private readonly AssetObjectLoader _objectLoader;

    public AssetExportService(AssetObjectLoader objectLoader)
    {
        _objectLoader = objectLoader;
    }

    public Task<AssetExportResult> ExportRawAsync(
        AssetIndexEntry entry,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExportRaw(entry, outputPath, cancellationToken), cancellationToken);

    public Task<AssetExportResult> ExportDumpAsync(
        AssetIndexEntry entry,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExportDump(entry, outputPath, cancellationToken), cancellationToken);

    private AssetExportResult ExportRaw(
        AssetIndexEntry entry,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var session = _objectLoader.OpenAsync(entry, cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        var asset = session.Asset;
        var files = new List<string>();

        WriteAtomically(outputPath, temporaryPath =>
        {
            asset.reader.Reset();
            using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize);
            CopyExactly(asset.reader.BaseStream, output, asset.byteSize, cancellationToken);
        });
        files.Add(Path.GetFullPath(outputPath));

        ResourceReader? externalData = asset switch
        {
            Texture2D texture when !string.IsNullOrEmpty(texture.m_StreamData?.path) => texture.image_data,
            AudioClip audio when !string.IsNullOrEmpty(audio.m_Source) => audio.m_AudioData,
            VideoClip video when !string.IsNullOrEmpty(video.m_ExternalResources?.m_Source) => video.m_VideoData,
            _ => null,
        };
        if (externalData is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sidecarPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(outputPath))!,
                Path.GetFileNameWithoutExtension(outputPath) + "_data.dat");
            WriteAtomically(sidecarPath, externalData.WriteData);
            files.Add(sidecarPath);
        }

        return new AssetExportResult(files);
    }

    private AssetExportResult ExportDump(
        AssetIndexEntry entry,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var session = _objectLoader.OpenAsync(entry, cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        var text = session.Asset.Dump();
        if (string.IsNullOrEmpty(text))
        {
            text = session.Asset.DumpObject();
        }
        if (string.IsNullOrEmpty(text))
        {
            throw new NotSupportedException($"A dump could not be produced for {entry.TypeName}.");
        }

        WriteAtomically(outputPath, temporaryPath =>
        {
            using var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(false), CopyBufferSize);
            writer.Write(text);
        });
        return new AssetExportResult([Path.GetFullPath(outputPath)]);
    }

    private static void CopyExactly(Stream input, Stream output, long length, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            var remaining = length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    throw new EndOfStreamException($"Expected {remaining:N0} more bytes while exporting the asset.");
                }
                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void WriteAtomically(string outputPath, Action<string> write)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            write(temporaryPath);
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
