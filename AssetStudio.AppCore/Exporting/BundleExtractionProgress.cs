namespace AssetStudio.AppCore.Exporting;

public sealed record BundleExtractionProgress(int CompletedSources, int TotalSources, int ExtractedFiles, string SourcePath);
