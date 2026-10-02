using System.Numerics;
using System.Runtime.InteropServices;

namespace WoWRenderLib.Structs;

/// <summary>
/// Renderer-facing liquid families resolved from client database records.
/// Keep this deliberately smaller than the client material-id space.
/// </summary>
public enum WorldLiquidMaterialFamily
{
    Unknown,
    Water,
    Magma,
    Mercury,
    Fog,
    LeyLine,
    Fel,
    Swamp,
    Azerite
}

/// <summary>
/// The procedural depth type carried by LiquidTypeXTexture. These values
/// match the client enum: ocean (0), river (1), and WMO (2). Unknown is used
/// when a legacy table has no explicit procedural-depth row.
/// </summary>
public enum WorldLiquidWaterType
{
    Unknown = -1,
    Ocean = 0,
    River = 1,
    Wmo = 2
}

/// <summary>
/// Effective water palette used when a LightData profile deliberately leaves
/// all four legacy liquid colors at zero. These values are shared by the
/// catalog/UI snapshot and the per-material renderer fallback so both surfaces
/// report and render the same non-black result.
/// </summary>
public static class WorldLiquidColorDefaults
{
    public static readonly Vector3 OceanClose = new(0.16f, 0.48f, 0.82f);
    public static readonly Vector3 OceanFar = new(0.015f, 0.09f, 0.32f);
    public static readonly Vector3 RiverClose = OceanClose;
    public static readonly Vector3 RiverFar = OceanFar;
}

public readonly record struct WorldLiquidMaterialKey(
    ushort LiquidTypeId,
    ushort LiquidObjectOrLvf);

/// <summary>
/// One client liquid texture slot. A slot may contain several animated frames;
/// the renderer selects a frame from the slot without flattening adjacent slots
/// into one animation sequence.
/// </summary>
public sealed record WorldLiquidTextureSlot(uint[] Frames)
{
    public WorldLiquidWaterType ProceduralDepth { get; init; } = WorldLiquidWaterType.Unknown;
}

public sealed record WorldLiquidMaterialDescriptor(
    WorldLiquidMaterialKey Key,
    WorldLiquidMaterialFamily Family,
    Vector4 ShallowColor,
    Vector4 DeepColor,
    float UvScale,
    float FlowDirectionRadians,
    float FlowSpeed,
    uint[] TextureFileDataIds)
{
    /// <summary>
    /// Gets the client water-body type, when one was available in
    /// LiquidTypeXTexture. Unknown descriptors use the renderer's legacy
    /// fallback classification.
    /// </summary>
    public WorldLiquidWaterType WaterType { get; init; } = WorldLiquidWaterType.Unknown;

    /// <summary>
    /// Ordered LiquidType texture slots. Their meanings are material-specific;
    /// later water shaders read slot 2 as wave normal and slot 3 as foam.
    /// The 12340 basic water shader reads slot 0 as surface and slot 1 as depth
    /// gradient. Procedural or absent rows leave an empty slot so the
    /// original client slot numbering remains available to the renderer.
    /// </summary>
    public WorldLiquidTextureSlot[] TextureSlots { get; init; } = [];

    /// <summary>
    /// LiquidType.Coefficient[0..3], evaluated as c0+c1*d+c2*d²+c3*d³
    /// against the normalized MH2O depth byte.
    /// </summary>
    public Vector4 DepthCoefficients { get; init; } = new(0f, 1f, 0f, 0f);

    /// <summary>LiquidType and LiquidMaterial fields used by WMO MLIQ surfaces.</summary>
    public uint WmoTypeFlags { get; init; }
    public int WmoBasicClass { get; init; }
    public int WmoVertexFormat { get; init; }
    public uint WmoMaterialFlags { get; init; }
    public int WmoDepthDivisor { get; init; } = 42;
    public uint WmoAnimationPeriodMilliseconds { get; init; } = 1000;
    public float WmoTextureRotation { get; init; }

    /// <summary>Present only for MPQ 3.3.5.12340; later material IDs have different contracts.</summary>
    public Wrath335LiquidSettings? Wrath335 { get; init; }
}

public sealed record Wrath335LiquidSettings(
    int MaterialId, int DepthMode, float TextureScale, float TextureRotation,
    float DepthScale, uint AnimationPeriodMilliseconds);

public interface IWorldLiquidMaterialCatalog
{
    WorldLiquidMaterialDescriptor Resolve(ushort liquidTypeId, ushort liquidObjectOrLvf);
}

/// <summary>
/// Compact CPU/GPU transfer vertex for a decoded ADT or WMO liquid surface.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct WorldLiquidVertex
{
    public Vector3 Position;
    public float Depth;
    public Vector2 TexCoord;
    public Vector2 CellCoord;
}

public readonly record struct ParsedWorldLiquidBatch(
    int ChunkIndex,
    int LayerIndex,
    uint FirstIndex,
    uint IndexCount,
    int MaterialIndex,
    BoundingBox Bounds,
    bool IsFishable,
    bool IsDeep)
{
    public ushort LiquidTypeId { get; init; }
    public ushort LiquidObjectOrLvf { get; init; }
    public WorldLiquidMaterialFamily Family { get; init; }
    public bool IsWmoInterior { get; init; }
    public bool IsWmo { get; init; }
}

/// <summary>
/// The four layouts decoded by WowLib for an MH2O instance. This managed enum is
/// intentionally independent from the package enum so geometry tests do not
/// need to construct native-backed WowLib objects.
/// </summary>
public enum WorldLiquidVertexFormat
{
    HeightDepth,
    HeightUv,
    DepthOnly,
    HeightUvDepth
}

/// <summary>
/// Renderer-owned copy of one MH2O layer. It is also the input used by the
/// asset-independent mesh-builder tests.
/// </summary>
public sealed record WorldLiquidLayerInput
{
    public int ChunkIndex { get; init; }
    public int LayerIndex { get; init; }
    public Vector3 ChunkPosition { get; init; }
    public ushort LiquidTypeId { get; init; }
    public ushort LiquidObjectOrLvf { get; init; }
    public WorldLiquidVertexFormat VertexFormat { get; init; }
    public float MinHeight { get; init; }
    public float MaxHeight { get; init; }
    public byte XOffset { get; init; }
    public byte YOffset { get; init; }
    public byte Width { get; init; }
    public byte Height { get; init; }
    public byte[] ExistsBitmap { get; init; } = [];
    public float[] Heightmap { get; init; } = [];
    public byte[] Depthmap { get; init; } = [];
    public Vector2[] Uvmap { get; init; } = [];
    public bool IsFishable { get; init; }
    public bool IsDeep { get; init; }
}

/// <summary>
/// Fully managed liquid payload copied before the native-backed WowLib file is disposed.
/// </summary>
public sealed class ParsedWorldLiquid
{
    public static ParsedWorldLiquid Empty { get; } = new();

    public WorldLiquidVertex[] Vertices { get; init; } = [];
    public uint[] Indices { get; init; } = [];
    public ParsedWorldLiquidBatch[] Batches { get; init; } = [];
    public WorldLiquidMaterialDescriptor[] Materials { get; init; } = [];
    public WorldLiquidQueryGrid[] QueryGrids { get; init; } = [];
    public uint[] TextureFileDataIds { get; init; } = [];
    public BoundingBox Bounds { get; init; }
    public bool HasBounds { get; init; }

    public bool IsEmpty => Vertices.Length == 0 || Indices.Length == 0 || Batches.Length == 0;
}

/// <summary>
/// Original CPU query grid, independent of clipped/triangulated draw geometry.
/// Terrain uses a chunk origin, offsets and a bit mask; WMO uses local origin
/// and tile bytes. Heights retain the original vertex stride.
/// </summary>
public sealed record WorldLiquidQueryGrid(ushort LiquidTypeId, uint TypeFlags,
    bool IsWmo, Vector3 Origin, int Width, int Height, int VertexStride,
    float[] Heights, byte[] Tiles, int XOffset = 0, int YOffset = 0);
