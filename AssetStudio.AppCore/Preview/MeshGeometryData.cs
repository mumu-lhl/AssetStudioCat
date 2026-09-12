namespace AssetStudio.AppCore.Preview;

using Vector3 = System.Numerics.Vector3;

public sealed class MeshGeometryData
{
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public float[]? CalculatedNormals { get; init; }
    public float[]? Colors { get; init; }
    public required uint[] Indices { get; init; }
    public required int VertexCount { get; init; }
    public required int TriangleCount { get; init; }
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }
    public required Vector3 Center { get; init; }
    public required Vector3 Extents { get; init; }
    public required float BoundingRadius { get; init; }
}
