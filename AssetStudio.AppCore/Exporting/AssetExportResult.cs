namespace AssetStudio.AppCore.Exporting;

public sealed record AssetExportResult(IReadOnlyList<string> Files, string? Note = null);
