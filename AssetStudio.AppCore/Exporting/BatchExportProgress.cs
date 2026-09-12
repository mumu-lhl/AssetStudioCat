namespace AssetStudio.AppCore.Exporting;

public sealed record BatchExportProgress(int Completed, int Succeeded, int Failed, string AssetName);
