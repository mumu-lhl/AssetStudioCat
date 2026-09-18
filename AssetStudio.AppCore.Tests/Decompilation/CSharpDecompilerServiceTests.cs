using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Decompilation;
using Xunit;

namespace AssetStudio.AppCore.Tests.Decompilation;

public sealed class CSharpDecompilerServiceTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"assetstudio-decomp-tests-{Guid.NewGuid():N}");

    public CSharpDecompilerServiceTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void AssemblyDirectoryDetector_DetectsManagedDirectory_FromDataFolder()
    {
        var gameRoot = Path.Combine(_testDir, "MyGame");
        var dataFolder = Path.Combine(gameRoot, "MyGame_Data");
        var managedFolder = Path.Combine(dataFolder, "Managed");
        Directory.CreateDirectory(managedFolder);
        File.WriteAllText(Path.Combine(managedFolder, "Assembly-CSharp.dll"), "dummy dll content");

        var dummyBundle = Path.Combine(dataFolder, "data.unity3d");
        File.WriteAllText(dummyBundle, "dummy bundle");

        var detectedFromBundle = AssemblyDirectoryDetector.TryDetectManagedDirectory(dummyBundle);
        Assert.NotNull(detectedFromBundle);
        Assert.Equal(Path.GetFullPath(managedFolder), Path.GetFullPath(detectedFromBundle));

        var detectedFromRoot = AssemblyDirectoryDetector.TryDetectManagedDirectory(gameRoot);
        Assert.NotNull(detectedFromRoot);
        Assert.Equal(Path.GetFullPath(managedFolder), Path.GetFullPath(detectedFromRoot));
    }

    [Fact]
    public void AssemblyDirectoryDetector_ReturnsNull_WhenNoManagedFolder()
    {
        var emptyFolder = Path.Combine(_testDir, "Empty");
        Directory.CreateDirectory(emptyFolder);

        var detected = AssemblyDirectoryDetector.TryDetectManagedDirectory(emptyFolder);
        Assert.Null(detected);
    }

    [Fact]
    public async Task DecompilesActualType_FromRunningAssembly()
    {
        using var service = new CSharpDecompilerService();

        var appCoreAssemblyPath = typeof(AppSettings).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(appCoreAssemblyPath)!;
        var assemblyName = Path.GetFileName(appCoreAssemblyPath);

        service.AssemblyDirectory = assemblyDir;

        Assert.True(service.TryFindAssembly(assemblyName, out var foundPath));
        Assert.Equal(Path.GetFullPath(appCoreAssemblyPath), Path.GetFullPath(foundPath));

        // Test decompiling AppSettings
        var csharp = await service.DecompileTypeAsync(
            assemblyName,
            nameof(AppSettings),
            typeof(AppSettings).Namespace);

        Assert.NotNull(csharp);
        Assert.Contains("class AppSettings", csharp);
        Assert.Contains("AssemblyDirectory", csharp);
        Assert.Contains("PreviewCacheMegabytes", csharp);

        // Test cache: second call should return same string
        var cached = await service.DecompileTypeAsync(
            assemblyName,
            nameof(AppSettings),
            typeof(AppSettings).Namespace);

        Assert.Same(csharp, cached);
    }

    [Fact]
    public async Task DecompileType_ReturnsNull_ForMissingAssemblyOrType()
    {
        using var service = new CSharpDecompilerService();
        service.AssemblyDirectory = _testDir;

        var resultNonExistentAssembly = await service.DecompileTypeAsync(
            "NonExistent.dll",
            "SomeClass",
            "SomeNamespace");
        Assert.Null(resultNonExistentAssembly);

        var appCoreAssemblyPath = typeof(AppSettings).Assembly.Location;
        service.AssemblyDirectory = Path.GetDirectoryName(appCoreAssemblyPath);

        var resultNonExistentType = await service.DecompileTypeAsync(
            Path.GetFileName(appCoreAssemblyPath),
            "NonExistentType",
            "AssetStudio.AppCore");
        Assert.Null(resultNonExistentType);
    }

    [Fact]
    public async Task Decompilation_HandlesCaseInsensitiveAssemblyAndProbePaths()
    {
        using var service = new CSharpDecompilerService();

        var appCoreAssemblyPath = typeof(AppSettings).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(appCoreAssemblyPath)!;
        var assemblyName = Path.GetFileName(appCoreAssemblyPath).ToUpperInvariant();

        // Register via probe path instead of main directory
        service.RegisterProbePath(assemblyDir);

        Assert.True(service.TryFindAssembly(assemblyName, out var foundPath));
        Assert.Equal(Path.GetFullPath(appCoreAssemblyPath), Path.GetFullPath(foundPath), ignoreCase: true);

        var csharp = await service.DecompileTypeAsync(
            assemblyName,
            nameof(AppSettings),
            typeof(AppSettings).Namespace);

        Assert.NotNull(csharp);
        Assert.Contains("class AppSettings", csharp);

        // Test clearing cache
        service.ClearCache();
        var afterClear = await service.DecompileTypeAsync(
            assemblyName,
            nameof(AppSettings),
            typeof(AppSettings).Namespace);

        Assert.NotNull(afterClear);
        Assert.NotSame(csharp, afterClear); // New instance loaded after cache clear
    }
}
