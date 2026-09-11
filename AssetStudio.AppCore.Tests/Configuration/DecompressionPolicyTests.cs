using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Loading;

namespace AssetStudio.AppCore.Tests.Configuration;

public sealed class DecompressionPolicyTests
{
    [Fact]
    public void AutoUsesMemoryWhenEstimateFitsBudget()
    {
        var settings = CreateSettings();

        var result = DecompressionPolicy.Decide(
            settings,
            estimatedUncompressedBytes: 512L * 1024 * 1024,
            availableMemoryBytes: 8L * 1024 * 1024 * 1024);

        Assert.Equal(BundleDecompressionMode.Memory, result.EffectiveMode);
        Assert.Equal(2L * 1024 * 1024 * 1024, result.MemoryBudgetBytes);
    }

    [Fact]
    public void AutoUsesDiskWhenEstimateExceedsBudget()
    {
        var settings = CreateSettings();

        var result = DecompressionPolicy.Decide(
            settings,
            estimatedUncompressedBytes: 4L * 1024 * 1024 * 1024,
            availableMemoryBytes: 8L * 1024 * 1024 * 1024);

        Assert.Equal(BundleDecompressionMode.Disk, result.EffectiveMode);
    }

    [Fact]
    public void ExplicitMemoryModeIsPreservedButWarnedAbout()
    {
        var settings = CreateSettings();
        settings.DecompressionMode = BundleDecompressionMode.Memory;

        var result = DecompressionPolicy.Decide(
            settings,
            estimatedUncompressedBytes: 4L * 1024 * 1024 * 1024,
            availableMemoryBytes: 8L * 1024 * 1024 * 1024);

        Assert.Equal(BundleDecompressionMode.Memory, result.EffectiveMode);
        Assert.Contains("exceeds", result.Reason);
    }

    private static AppSettings CreateSettings()
    {
        return new AppSettings
        {
            AutoMemoryFraction = 0.30,
            AutoMemoryLimitBytes = 2L * 1024 * 1024 * 1024,
        };
    }
}
