using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetStudio.AppCore.Configuration;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppDirectories _directories;

    public AppSettingsStore(AppDirectories directories)
    {
        _directories = directories;
    }

    public string SettingsPath => Path.Combine(_directories.ConfigDirectory, "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings().Normalize(_directories);
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
            return (settings ?? new AppSettings()).Normalize(_directories);
        }
        catch (JsonException)
        {
            return new AppSettings().Normalize(_directories);
        }
        catch (IOException)
        {
            return new AppSettings().Normalize(_directories);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Normalize(_directories);
        Directory.CreateDirectory(_directories.ConfigDirectory);

        var temporaryPath = SettingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, SettingsPath, true);
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
