using System.Text;
using System.Text.Json;
using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Exporting;

public sealed class AssetListExportService
{
    public async Task<long> ExportAsync(
        DiskAssetIndex index,
        string outputPath,
        AssetListExportFormat format,
        string? searchText = null,
        string? typeName = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var count = format == AssetListExportFormat.Csv
                ? await WriteCsvAsync(index, temporaryPath, searchText, typeName, cancellationToken)
                : await WriteJsonAsync(index, temporaryPath, searchText, typeName, cancellationToken);
            File.Move(temporaryPath, fullPath, true);
            return count;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task<long> WriteCsvAsync(
        DiskAssetIndex index,
        string path,
        string? searchText,
        string? typeName,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("Name,Container,Type,PathID,StoredSize,ObjectSource,SerializedFile");
        long count = 0;
        await foreach (var entry in index.EnumerateAsync(searchText, typeName, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', [
                Escape(entry.Name), Escape(entry.Container), Escape(entry.TypeName), entry.PathId.ToString(),
                entry.ByteSize.ToString(), Escape(entry.ObjectSourcePath), Escape(entry.SerializedFile)]));
            count++;
        }
        return count;
    }

    private static async Task<long> WriteJsonAsync(
        DiskAssetIndex index,
        string path,
        string? searchText,
        string? typeName,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        long count = 0;
        await foreach (var entry in index.EnumerateAsync(searchText, typeName, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(writer, entry);
            count++;
        }
        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
        return count;
    }

    private static string Escape(string? value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
