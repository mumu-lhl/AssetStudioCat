namespace AssetStudio.AppCore.Configuration;

public sealed record AppDirectories(string ConfigDirectory, string DefaultCacheDirectory)
{
    public static AppDirectories Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            return new AppDirectories(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetStudioCat"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssetStudioCat", "Cache"));
        }

        if (OperatingSystem.IsMacOS())
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new AppDirectories(
                Path.Combine(profile, "Library", "Application Support", "AssetStudioCat"),
                Path.Combine(profile, "Library", "Caches", "AssetStudioCat"));
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return new AppDirectories(
            Path.Combine(string.IsNullOrWhiteSpace(configHome) ? Path.Combine(userProfile, ".config") : configHome, "AssetStudioCat"),
            Path.Combine(string.IsNullOrWhiteSpace(cacheHome) ? Path.Combine(userProfile, ".cache") : cacheHome, "AssetStudioCat"));
    }
}
