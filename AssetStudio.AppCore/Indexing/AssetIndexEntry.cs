namespace AssetStudio.AppCore.Indexing;

public sealed record AssetIndexEntry(
    long Id,
    string SourcePath,
    string ObjectSourcePath,
    string SerializedFile,
    long PathId,
    int ClassId,
    string TypeName,
    string Name,
    string? Container,
    long ByteStart,
    uint ByteSize,
    long? GameObjectPathId = null,
    string? GameObjectSerializedFile = null,
    long? ParentTransformPathId = null,
    string? ParentTransformSerializedFile = null);
