using AssetStudio.AppCore.Configuration;

namespace AssetStudio.AppCore.Caching;

public sealed class CacheLayout
{
    public CacheLayout(AppSettings settings)
    {
        Root = settings.CacheRoot ?? throw new ArgumentException("Settings must be normalized first.", nameof(settings));
        Indexes = Path.Combine(Root, "indexes");
        Previews = Path.Combine(Root, "previews");
        Sessions = settings.DecompressionDirectory
            ?? Path.Combine(Root, "decompressed");
    }

    public string Root { get; }

    public string Indexes { get; }

    public string Previews { get; }

    public string Sessions { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Indexes);
        Directory.CreateDirectory(Previews);
        Directory.CreateDirectory(Sessions);
    }

    public async Task VerifyWritableAsync(CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        var probePath = Path.Combine(Root, $".write-test-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(probePath, "AssetStudioCat", cancellationToken);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    public string CreateSessionDirectory()
    {
        EnsureCreated();
        var directory = Path.Combine(
            Sessions,
            $"session-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
