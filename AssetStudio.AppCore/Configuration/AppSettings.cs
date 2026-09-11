namespace AssetStudio.AppCore.Configuration;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public BundleDecompressionMode DecompressionMode { get; set; } = BundleDecompressionMode.Auto;

    public string? CacheRoot { get; set; }

    public string? DecompressionDirectory { get; set; }

    public int PreviewCacheMegabytes { get; set; } = 128;

    public double AutoMemoryFraction { get; set; } = 0.30;

    public long AutoMemoryLimitBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    public AppSettings Normalize(AppDirectories directories)
    {
        SchemaVersion = CurrentSchemaVersion;
        PreviewCacheMegabytes = Math.Clamp(PreviewCacheMegabytes, 32, 4096);
        AutoMemoryFraction = Math.Clamp(AutoMemoryFraction, 0.05, 0.80);
        AutoMemoryLimitBytes = Math.Clamp(
            AutoMemoryLimitBytes,
            128L * 1024 * 1024,
            int.MaxValue);
        CacheRoot = NormalizePath(CacheRoot, directories.DefaultCacheDirectory);
        DecompressionDirectory = NormalizePath(
            DecompressionDirectory,
            Path.Combine(CacheRoot, "decompressed"));
        return this;
    }

    private static string NormalizePath(string? path, string fallback)
    {
        var selected = string.IsNullOrWhiteSpace(path) ? fallback : path;
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(selected));
    }
}
