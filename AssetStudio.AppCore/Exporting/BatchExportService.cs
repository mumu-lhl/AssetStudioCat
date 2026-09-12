using System.Runtime.CompilerServices;
using System.Text;
using AssetStudio.AppCore.Indexing;

namespace AssetStudio.AppCore.Exporting;

public sealed class BatchExportService
{
    private readonly AssetExportService _assetExporter;
    private readonly ConvertedAssetExportService _convertedExporter;
    private readonly AnimatorExportService _animatorExporter;

    public BatchExportService(
        AssetExportService assetExporter,
        ConvertedAssetExportService convertedExporter,
        AnimatorExportService animatorExporter)
    {
        _assetExporter = assetExporter;
        _convertedExporter = convertedExporter;
        _animatorExporter = animatorExporter;
    }

    public async Task<BatchExportSummary> ExportAsync(
        IAsyncEnumerable<AssetIndexEntry> entries,
        string outputDirectory,
        BatchExportMode mode,
        IProgress<BatchExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var completed = 0;
        var succeeded = 0;
        var failed = 0;
        string? errorLogPath = null;
        StreamWriter? errorWriter = null;
        try
        {
            await foreach (var entry in entries.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (mode == BatchExportMode.Converted && entry.TypeName == "Animator")
                    {
                        await _animatorExporter.ExportAsync(entry, root, cancellationToken);
                    }
                    else if (mode == BatchExportMode.Converted)
                    {
                        await _convertedExporter.ExportAsync(entry, root, cancellationToken);
                    }
                    else
                    {
                        var extension = mode == BatchExportMode.Raw ? ".dat" : ".txt";
                        var path = GetAvailablePath(root, entry, extension);
                        if (mode == BatchExportMode.Raw)
                        {
                            await _assetExporter.ExportRawAsync(entry, path, cancellationToken);
                        }
                        else
                        {
                            await _assetExporter.ExportDumpAsync(entry, path, cancellationToken);
                        }
                    }
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failed++;
                    if (errorWriter is null)
                    {
                        errorLogPath = Path.Combine(root, $"AssetStudioCat-export-errors-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
                        errorWriter = new StreamWriter(errorLogPath, false, new UTF8Encoding(false));
                    }
                    await errorWriter.WriteLineAsync(
                        $"{entry.TypeName}\t{entry.Name}\tPathID {entry.PathId}\t{exception.Message}".AsMemory(),
                        cancellationToken);
                }
                completed++;
                progress?.Report(new BatchExportProgress(completed, succeeded, failed, entry.Name));
            }
        }
        finally
        {
            if (errorWriter is not null)
            {
                await errorWriter.DisposeAsync();
            }
        }
        return new BatchExportSummary(completed, succeeded, failed, errorLogPath);
    }

    public static async IAsyncEnumerable<AssetIndexEntry> FromEntries(
        IEnumerable<AssetIndexEntry> entries,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }

    private static string GetAvailablePath(string directory, AssetIndexEntry entry, string extension)
    {
        const string invalid = "<>:\"/\\|?*";
        var name = new string(entry.Name
            .Select(character => char.IsControl(character) || invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "asset";
        }
        var candidate = Path.Combine(directory, name + extension);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }
        candidate = Path.Combine(directory, $"{name}_{entry.PathId}{extension}");
        return File.Exists(candidate) || Directory.Exists(candidate)
            ? Path.Combine(directory, $"{name}_{entry.PathId}_{Guid.NewGuid():N}{extension}")
            : candidate;
    }
}
