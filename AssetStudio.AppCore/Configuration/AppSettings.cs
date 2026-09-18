namespace AssetStudio.AppCore.Configuration;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public BundleDecompressionMode DecompressionMode { get; set; } = BundleDecompressionMode.Auto;

    public string? CacheRoot { get; set; }

    public string? DecompressionDirectory { get; set; }

    public string? CustomUnityVersion { get; set; }

    public string? AssemblyDirectory { get; set; }

    public int PreviewCacheMegabytes { get; set; } = 128;

    public double AutoMemoryFraction { get; set; } = 0.50;

    public long AutoMemoryLimitBytes { get; set; } = 4L * 1024 * 1024 * 1024;

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

    public Gltf.Format GltfFormat { get; set; } = Gltf.Format.Glb;

    public bool GltfExportAnimations { get; set; } = true;

    public bool GltfExportSkins { get; set; } = true;

    public bool GltfExportBlendShapes { get; set; } = true;

    public double GltfScaleFactor { get; set; } = 1.0;

    public bool RestoreLastSource { get; set; } = true;

    public string Language { get; set; } = "auto";

    public bool EnableHttpApi { get; set; } = true;

    public int HttpApiPort { get; set; } = 23333;

    public List<RecentSource> RecentSources { get; set; } = [];

    public AppSettings Normalize(AppDirectories directories)
    {
        SchemaVersion = CurrentSchemaVersion;
        PreviewCacheMegabytes = Math.Clamp(PreviewCacheMegabytes, 32, 4096);
        AutoMemoryFraction = Math.Clamp(AutoMemoryFraction, 0.05, 0.80);
        AutoMemoryLimitBytes = Math.Clamp(
            AutoMemoryLimitBytes,
            128L * 1024 * 1024,
            64L * 1024 * 1024 * 1024);
        FbxScaleFactor = Math.Clamp(FbxScaleFactor, 0.0001, 10_000);
        GltfScaleFactor = Math.Clamp(GltfScaleFactor, 0.0001, 10_000);
        if (HttpApiPort <= 0 || HttpApiPort > 65535)
        {
            HttpApiPort = 23333;
        }
        Language = string.Equals(Language, "zh-CN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Language, "zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : "auto";
        RecentSources ??= [];
        RecentSources = RecentSources
            .Where(source => source.Paths is { Count: > 0 })
            .OrderByDescending(source => source.LastOpenedAt)
            .Take(20)
            .ToList();
        CacheRoot = NormalizePath(CacheRoot, directories.DefaultCacheDirectory);
        DecompressionDirectory = NormalizePath(
            DecompressionDirectory,
            Path.Combine(CacheRoot, "decompressed"));
        CustomUnityVersion = string.IsNullOrWhiteSpace(CustomUnityVersion) ? null : CustomUnityVersion.Trim();
        AssemblyDirectory = string.IsNullOrWhiteSpace(AssemblyDirectory)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(AssemblyDirectory.Trim()));
        return this;
    }

    private static string NormalizePath(string? path, string fallback)
    {
        var selected = string.IsNullOrWhiteSpace(path) ? fallback : path;
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(selected));
    }
}
