namespace AssetStudio.AppCore.Configuration;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public BundleDecompressionMode DecompressionMode { get; set; } = BundleDecompressionMode.Auto;

    public string? CacheRoot { get; set; }

    public string? DecompressionDirectory { get; set; }

    public string? CustomUnityVersion { get; set; }

    public int PreviewCacheMegabytes { get; set; } = 128;

    public double AutoMemoryFraction { get; set; } = 0.30;

    public long AutoMemoryLimitBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    public ConvertedImageFormat ConvertedImageFormat { get; set; } = ConvertedImageFormat.Png;

    public bool ExportSpriteWithMask { get; set; } = true;

    public bool ConvertAudioToWav { get; set; } = true;

    public bool FbxExportAnimations { get; set; } = true;

    public bool FbxExportSkins { get; set; } = true;

    public bool FbxExportBlendShapes { get; set; } = true;

    public bool FbxExportAllNodes { get; set; } = true;

    public bool FbxEulerFilter { get; set; } = true;

    public bool FbxAscii { get; set; }

    public double FbxScaleFactor { get; set; } = 1.0;

    public bool RestoreLastSource { get; set; } = true;

    public List<RecentSource> RecentSources { get; set; } = [];

    public AppSettings Normalize(AppDirectories directories)
    {
        SchemaVersion = CurrentSchemaVersion;
        PreviewCacheMegabytes = Math.Clamp(PreviewCacheMegabytes, 32, 4096);
        AutoMemoryFraction = Math.Clamp(AutoMemoryFraction, 0.05, 0.80);
        AutoMemoryLimitBytes = Math.Clamp(
            AutoMemoryLimitBytes,
            128L * 1024 * 1024,
            int.MaxValue);
        FbxScaleFactor = Math.Clamp(FbxScaleFactor, 0.0001, 10_000);
        RecentSources ??= [];
        RecentSources = RecentSources
            .Where(source => source.Paths is { Count: > 0 })
            .OrderByDescending(source => source.LastOpenedAt)
            .Take(10)
            .ToList();
        CacheRoot = NormalizePath(CacheRoot, directories.DefaultCacheDirectory);
        DecompressionDirectory = NormalizePath(
            DecompressionDirectory,
            Path.Combine(CacheRoot, "decompressed"));
        CustomUnityVersion = string.IsNullOrWhiteSpace(CustomUnityVersion) ? null : CustomUnityVersion.Trim();
        return this;
    }

    private static string NormalizePath(string? path, string fallback)
    {
        var selected = string.IsNullOrWhiteSpace(path) ? fallback : path;
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(selected));
    }
}
