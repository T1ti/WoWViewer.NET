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

public sealed record AssetReference(uint FileDataId, string Name);

public sealed record ModelAdvancedData(
    int BatchCount,
    int VertexCount,
    int TriangleCount,
    int AnimationCount,
    int ParticleEmitterCount,
    int BoneCount,
    int AttachmentCount);

public enum MapPlacementKind
{
    Mddf,
    Modf
}

public sealed record MapPlacementData(
    MapPlacementKind Kind,
    uint UniqueId,
    ushort Flags,
    ushort? DoodadSet = null,
    ushort? NameSet = null);

public sealed record WorldModelRootData(
    uint AmbientColor,
    ushort Flags);

public sealed record ModelTextureData(int Slot, AssetReference Asset, uint? Flags = null);

public sealed record ModelMaterialData(
    int Index,
    uint BlendMode,
    uint Flags,
    string Shader,
    string VertexShader,
    string PixelShader,
    IReadOnlyList<AssetReference> Textures,
    uint? Color1 = null,
    uint? Color1B = null,
    uint? Color2 = null,
    uint? Color3 = null,
    uint? GroundType = null,
    uint? ExtendedFlags = null,
    IReadOnlyList<ModelTextureData>? TextureSlots = null);

public sealed record ModelBatchData(
    int Index,
    int? GroupIndex,
    int? MaterialIndex,
    uint FirstIndex,
    uint IndexCount,
    uint BlendMode,
    uint Flags,
    string Shader,
    string VertexShader,
    string PixelShader,
    IReadOnlyList<AssetReference> Textures);

public sealed record ModelGeosetData(
    int Index,
    ushort Id,
    string Type,
    ushort Level,
    uint FirstVertex,
    int VertexCount,
    uint FirstIndex,
    int IndexCount,
    bool IsEnabled);

public sealed record WorldModelGroupData(
    int Index,
    string Name,
    string Description,
    uint GroupId,
    int BatchCount,
    int VertexCount,
    int TriangleCount,
    int DoodadReferenceCount,
    uint Flags);

public sealed record M2ObjectData(
    uint FileDataId,
    uint ParentFileDataId,
    int GeosetCount,
    bool IsWorldModelDoodad,
    string FileName = "",
    IReadOnlyList<AssetReference>? Textures = null,
    uint? UniqueId = null,
    string ParentFileName = "",
    ModelAdvancedData? Advanced = null,
    MapPlacementData? Placement = null,
    IReadOnlyList<ModelMaterialData>? Materials = null,
    IReadOnlyList<ModelBatchData>? Batches = null,
    IReadOnlyList<ModelGeosetData>? Geosets = null,
    IReadOnlyList<ModelTextureData>? TextureDetails = null) : IEditorObjectData;

public sealed record WorldModelObjectData(
    uint FileDataId,
    uint ParentFileDataId,
    int GroupCount,
    int DoodadSetCount,
    int ActiveDoodadCount,
    bool IsLoaded,
    string FileName = "",
    IReadOnlyList<AssetReference>? Textures = null,
    uint UniqueId = 0,
    string ParentFileName = "",
    IReadOnlyList<WorldModelGroupData>? Groups = null,
    MapPlacementData? Placement = null,
    WorldModelRootData? Root = null,
    IReadOnlyList<ModelMaterialData>? Materials = null,
    IReadOnlyList<ModelBatchData>? Batches = null,
    IReadOnlyList<string>? DoodadSets = null) : IEditorObjectData;

public sealed record TerrainObjectData(
    uint FileDataId,
    int TileX,
    int TileY,
    bool IsLoaded,
    bool IsModified = false) : IEditorObjectData;

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
