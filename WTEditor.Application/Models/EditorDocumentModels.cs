using System.Numerics;

namespace WTEditor.Application.Models;

public readonly record struct EditorObjectId(Guid Value)
{
    public static EditorObjectId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public sealed record ObjectTransform(
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale)
{
    public static ObjectTransform Identity { get; } =
        new(Vector3.Zero, Quaternion.Identity, Vector3.One);
}

public sealed record EditorObjectSnapshot(
    EditorObjectId Id,
    string Name,
    string Kind,
    ObjectTransform Transform,
    IEditorObjectData? Data = null);

/// <summary>
/// Marker for data that is specific to an editor object type. Keeping this data
/// beside the common identity and transform lets inspector providers be added
/// without growing <see cref="EditorObjectSnapshot"/> for every new object kind.
/// </summary>
public interface IEditorObjectData;

public sealed record M2ObjectData(
    uint FileDataId,
    uint ParentFileDataId,
    int GeosetCount,
    bool IsWorldModelDoodad) : IEditorObjectData;

public sealed record WorldModelObjectData(
    uint FileDataId,
    uint ParentFileDataId,
    int GroupCount,
    int DoodadSetCount,
    int ActiveDoodadCount,
    bool IsLoaded) : IEditorObjectData;

public sealed record TerrainObjectData(
    uint FileDataId,
    int TileX,
    int TileY,
    bool IsLoaded) : IEditorObjectData;

public enum EditorDocumentState
{
    New,
    Loading,
    Ready,
    Failed,
    Closed
}

public enum DocumentChangeKind
{
    State,
    ObjectAdded,
    ObjectChanged,
    ObjectRemoved,
    Saved
}

public sealed record DocumentChange(DocumentChangeKind Kind, EditorObjectId? ObjectId = null);
