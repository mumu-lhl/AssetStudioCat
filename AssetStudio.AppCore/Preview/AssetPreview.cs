namespace AssetStudio.AppCore.Preview;

public sealed record AssetPreview(
    byte[]? PngData,
    string? Text,
    string Information,
    bool FromCache = false,
    MeshGeometryData? MeshGeometry = null);
