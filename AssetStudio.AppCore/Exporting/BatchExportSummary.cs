namespace AssetStudio.AppCore.Exporting;

public sealed record BatchExportSummary(int Completed, int Succeeded, int Failed, string? ErrorLogPath);
