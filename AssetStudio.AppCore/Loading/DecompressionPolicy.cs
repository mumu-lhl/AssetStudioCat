using AssetStudio.AppCore.Configuration;

namespace AssetStudio.AppCore.Loading;

public sealed record DecompressionDecision(
    BundleDecompressionMode RequestedMode,
    BundleDecompressionMode EffectiveMode,
    long EstimatedUncompressedBytes,
    long AvailableMemoryBytes,
    long MemoryBudgetBytes,
    string Reason);

public static class DecompressionPolicy
{
    public static DecompressionDecision Decide(
        AppSettings settings,
        long estimatedUncompressedBytes,
        long availableMemoryBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedUncompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(availableMemoryBytes);

        var fractionBudget = (long)(availableMemoryBytes * settings.AutoMemoryFraction);
        var memoryBudget = Math.Min(settings.AutoMemoryLimitBytes, fractionBudget);
        var requested = settings.DecompressionMode;

        if (requested == BundleDecompressionMode.Memory)
        {
            return new DecompressionDecision(
                requested,
                BundleDecompressionMode.Memory,
                estimatedUncompressedBytes,
                availableMemoryBytes,
                memoryBudget,
                estimatedUncompressedBytes <= memoryBudget
                    ? "Memory mode was selected by the user."
                    : "Memory mode was selected by the user, but the estimate exceeds the recommended memory budget.");
        }

        if (requested == BundleDecompressionMode.Disk)
        {
            return new DecompressionDecision(
                requested,
                BundleDecompressionMode.Disk,
                estimatedUncompressedBytes,
                availableMemoryBytes,
                memoryBudget,
                "Disk mode was selected by the user.");
        }

        var effective = estimatedUncompressedBytes <= memoryBudget
            ? BundleDecompressionMode.Memory
            : BundleDecompressionMode.Disk;
        return new DecompressionDecision(
            requested,
            effective,
            estimatedUncompressedBytes,
            availableMemoryBytes,
            memoryBudget,
            effective == BundleDecompressionMode.Memory
                ? "The estimate fits within the automatic memory budget."
                : "The estimate exceeds the automatic memory budget.");
    }
}
