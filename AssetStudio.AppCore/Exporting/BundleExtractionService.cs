using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Loading;
using AssetStudio.CustomOptions;
using global::AssetStudio;

namespace AssetStudio.AppCore.Exporting;

public sealed class BundleExtractionService(AppSettings settings, CacheLayout layout)
{
    public Task<int> ExtractAsync(
        IReadOnlyList<string> sourcePaths,
        string outputDirectory,
        IProgress<BundleExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Extract(sourcePaths, outputDirectory, progress, cancellationToken), cancellationToken);

    private int Extract(
        IReadOnlyList<string> sourcePaths,
        string outputDirectory,
        IProgress<BundleExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sources = ExpandSources(sourcePaths).ToArray();
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var extracted = 0;
        for (var index = 0; index < sources.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            extracted += ExtractSource(sources[index], root, cancellationToken);
            progress?.Report(new BundleExtractionProgress(index + 1, sources.Length, extracted, sources[index]));
        }
        return extracted;
    }

    private int ExtractSource(string sourcePath, string outputRoot, CancellationToken cancellationToken)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists) return 0;
        using var session = DecompressionSession.Create(settings, layout,
            sourceInfo.Length <= long.MaxValue / 3 ? sourceInfo.Length * 3 : long.MaxValue);
        using var reader = new FileReader(sourcePath);
        return reader.FileType switch
        {
            FileType.BundleFile => ExtractBundle(reader, outputRoot, session, cancellationToken),
            FileType.WebFile => ExtractWeb(reader, outputRoot, cancellationToken),
            _ => 0,
        };
    }

    private static int ExtractBundle(
        FileReader reader,
        string outputRoot,
        DecompressionSession session,
        CancellationToken cancellationToken)
    {
        var options = new CustomBundleOptions();
        session.ApplyTo(options);
        using var bundleStream = new OffsetStream(reader);
        var bundleReader = new FileReader(reader.FullPath, bundleStream);
        var bundle = new BundleFile(bundleReader, options);
        var extractRoot = Path.Combine(outputRoot, reader.FileName + "_unpacked");
        var count = ExtractStreams(extractRoot, bundle.fileList, true, cancellationToken);
        while (bundle.IsDataAfterBundle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bundleStream.Offset = reader.Position;
            var nextReader = new FileReader($"{reader.FullPath}_0x{bundleStream.Offset:X}", bundleStream);
            if (nextReader.FileType != FileType.BundleFile) break;
            if (nextReader.Position > 0)
            {
                bundleStream.Offset += nextReader.Position;
                nextReader.FullPath = $"{reader.FullPath}_0x{bundleStream.Offset:X}";
                nextReader.FileName = $"{reader.FileName}_0x{bundleStream.Offset:X}";
            }
            bundle = new BundleFile(nextReader, options, isMultiBundle: true);
            count += ExtractStreams(extractRoot, bundle.fileList, true, cancellationToken);
        }
        return count;
    }

    private static int ExtractWeb(FileReader reader, string outputRoot, CancellationToken cancellationToken)
    {
        var web = new WebFile(reader);
        var extractRoot = Path.Combine(outputRoot, reader.FileName + "_unpacked");
        return ExtractStreams(extractRoot, web.fileList, false, cancellationToken);
    }

    private static int ExtractStreams(
        string extractRoot,
        IEnumerable<StreamFile> files,
        bool disposeFirstStream,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(extractRoot);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Stream? first = null;
        var count = 0;
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.stream is null) continue;
                first ??= file.stream;
                var target = Path.GetFullPath(Path.Combine(root, file.path));
                if (!target.StartsWith(prefix, StringComparison.Ordinal) && !string.Equals(target, root, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A bundle entry attempted to write outside the selected extraction directory.");
                }
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                file.stream.Position = 0;
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024);
                file.stream.CopyTo(output, 128 * 1024);
                count++;
                if (!disposeFirstStream) file.stream.Dispose();
            }
        }
        finally
        {
            if (disposeFirstStream) first?.Dispose();
        }
        return count;
    }

    private static IEnumerable<string> ExpandSources(IEnumerable<string> paths)
    {
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) yield return file;
            }
            else if (File.Exists(path)) yield return path;
        }
    }
}
