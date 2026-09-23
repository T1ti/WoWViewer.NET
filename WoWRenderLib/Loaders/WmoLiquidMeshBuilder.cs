using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

/// <summary>A portal plane for a shared tile whose neighboring liquid grid overlaps it.</summary>
public readonly record struct WmoLiquidClip(
    Vector2 NeighborMin,
    Vector2 NeighborMax,
    Vector3 Normal,
    float Distance,
    short Side);

/// <summary>Managed copy of one WMO group's MLIQ grid.</summary>
public sealed record WmoLiquidInput
{
    public int XVertices { get; init; }
    public int YVertices { get; init; }
    public int XTiles { get; init; }
    public int YTiles { get; init; }
    public Vector3 Origin { get; init; }
    public uint GroupLiquid { get; init; }
    public uint GroupFlags { get; init; }
    public uint MogiFlags { get; init; }
    public ushort RootFlags { get; init; }
    public uint MaterialId { get; init; }
    public int MaterialCount { get; init; } = int.MaxValue;
    public Vector4 InteriorColor { get; init; } = Vector4.One;
    public float[] Heights { get; init; } = [];
    public byte[] Depths { get; init; } = [];
    public Vector2[] AuthoredUvs { get; init; } = [];
    public byte[] Tiles { get; init; } = [];
    public WmoLiquidClip[] SharedClips { get; init; } = [];
}

/// <summary>
/// Builds the Wisp MLIQ grid and legacy type mapping. Modern LiquidType IDs
/// are passed through to the shared client database catalog.
/// </summary>
public static class WmoLiquidMeshBuilder
{
    public const float GridStep = 1600f / 3f / 16f / 8f;

    public static ParsedWorldLiquid Build(WmoLiquidInput input, IWorldLiquidMaterialCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catalog);
        if (input.XTiles <= 0 || input.YTiles <= 0 ||
            input.XTiles >= input.XVertices || input.YTiles >= input.YVertices ||
            (long)input.XVertices * input.YVertices > 65535 ||
            input.Heights.Length != input.XVertices * input.YVertices ||
            input.Tiles.Length != input.XTiles * input.YTiles ||
            !float.IsFinite(input.Origin.X) || !float.IsFinite(input.Origin.Y) ||
            !float.IsFinite(input.Origin.Z))
            return ParsedWorldLiquid.Empty;

        var typeId = ResolveLiquidType(input);
        var material = catalog.Resolve(typeId, 0);
        if (catalog is WorldLiquidMaterialCatalog databaseCatalog &&
            databaseCatalog.HasLiquidType(1) && !databaseCatalog.HasLiquidType(typeId))
        {
            typeId = 1;
            material = catalog.Resolve(typeId, 0);
        }
        var isInterior = !(((input.GroupFlags & 0x48) != 0 &&
            (input.MogiFlags & 0x48) != 0) || (material.WmoTypeFlags & 0x200) != 0);
        if (isInterior && (input.RootFlags & 0x4) == 0 &&
            input.MaterialId >= input.MaterialCount)
            return ParsedWorldLiquid.Empty;
        if (isInterior && typeId < 21 && typeId > 0 && ((typeId - 1) & 3) == 0 &&
            catalog is WorldLiquidMaterialCatalog databaseCatalogForInterior &&
            databaseCatalogForInterior.HasLiquidType(17))
        {
            typeId = 17;
            material = catalog.Resolve(typeId, 0);
        }
        var color = isInterior ? input.InteriorColor : Vector4.One;
        color.W = 1f;
        material = material with { ShallowColor = color, DeepColor = color };

        var vertices = new List<WorldLiquidVertex>(input.Heights.Length);
        var authoredUvs = material.WmoVertexFormat == 1 &&
            input.AuthoredUvs.Length == input.Heights.Length;
        var modernMagmaUvs = (input.RootFlags & 0x4) != 0 && typeId == 19 &&
            input.AuthoredUvs.Length == input.Heights.Length;
        var modernPlanarUvs = (input.RootFlags & 0x4) != 0 && !modernMagmaUvs;
        for (var row = 0; row < input.YVertices; row++)
        for (var column = 0; column < input.XVertices; column++)
        {
            var index = row * input.XVertices + column;
            if (!float.IsFinite(input.Heights[index]))
                return ParsedWorldLiquid.Empty;
            var position = new Vector3(
                input.Origin.X + column * GridStep,
                input.Origin.Y + row * GridStep,
                input.Heights[index]);
            vertices.Add(new WorldLiquidVertex
            {
                Position = position,
                Depth = material.WmoVertexFormat != 1 &&
                    input.Depths.Length == input.Heights.Length
                    ? Math.Clamp(input.Depths[index] / (float)material.WmoDepthDivisor, 0f, 1f)
                    : 0f,
                TexCoord = modernMagmaUvs
                    ? input.AuthoredUvs[index] * (3f / 256f)
                    : authoredUvs
                        ? input.AuthoredUvs[index] / 256f
                        : modernPlanarUvs
                            ? new Vector2(position.X, position.Y) / (1600f / 3f / 16f)
                            : new Vector2(column * GridStep, row * GridStep) * 0.24000001f,
                CellCoord = new Vector2(
                    column / (float)input.XTiles,
                    row / (float)input.YTiles)
            });
        }

        var indices = new List<uint>(input.Tiles.Length * 6);
        for (var row = 0; row < input.YTiles; row++)
        for (var column = 0; column < input.XTiles; column++)
        {
            var tile = input.Tiles[row * input.XTiles + column];
            if ((tile & 0x0f) == 0x0f)
                continue;
            var topLeft = (uint)(row * input.XVertices + column);
            var topRight = topLeft + 1;
            var bottomLeft = topLeft + (uint)input.XVertices;
            var bottomRight = bottomLeft + 1;
            if ((tile & 0x80) != 0 && input.SharedClips.Length > 0)
            {
                var polygon = new List<WorldLiquidVertex>(4)
                {
                    vertices[(int)topLeft], vertices[(int)bottomLeft],
                    vertices[(int)bottomRight], vertices[(int)topRight]
                };
                var tileMin = new Vector2(vertices[(int)topLeft].Position.X,
                    vertices[(int)topLeft].Position.Y);
                var tileMax = tileMin + new Vector2(GridStep);
                foreach (var clip in input.SharedClips)
                {
                    if (clip.NeighborMin.X >= tileMax.X || clip.NeighborMin.Y >= tileMax.Y ||
                        clip.NeighborMax.X <= tileMin.X || clip.NeighborMax.Y <= tileMin.Y)
                        continue;
                    polygon = ClipPolygon(polygon, clip);
                    if (polygon.Count < 3)
                        break;
                }
                if (polygon.Count < 3)
                    continue;
                var first = (uint)vertices.Count;
                vertices.AddRange(polygon);
                for (var point = 1; point + 1 < polygon.Count; point++)
                {
                    indices.Add(first);
                    indices.Add(first + (uint)point);
                    indices.Add(first + (uint)point + 1);
                }
            }
            else
            {
                indices.Add(topLeft);
                indices.Add(bottomLeft);
                indices.Add(bottomRight);
                indices.Add(topLeft);
                indices.Add(bottomRight);
                indices.Add(topRight);
            }
        }
        if (indices.Count == 0)
            return ParsedWorldLiquid.Empty;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var index in indices)
        {
            var position = vertices[(int)index].Position;
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        var bounds = new BoundingBox(min, max);
        return new ParsedWorldLiquid
        {
            Vertices = [.. vertices],
            Indices = [.. indices],
            Batches = [new ParsedWorldLiquidBatch(0, 0, 0, (uint)indices.Count, 0, bounds, false, false)
            {
                LiquidTypeId = typeId,
                Family = material.Family,
                IsWmoInterior = isInterior,
                IsWmo = true
            }],
            Materials = [material],
            TextureFileDataIds = material.TextureFileDataIds.Distinct().Where(id => id != 0).ToArray(),
            Bounds = bounds,
            HasBounds = true
        };
    }

    private static List<WorldLiquidVertex> ClipPolygon(
        List<WorldLiquidVertex> polygon, WmoLiquidClip clip)
    {
        const float epsilon = 0.00000011920929f;
        var result = new List<WorldLiquidVertex>(polygon.Count + 2);
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            var aDistance = (Vector3.Dot(clip.Normal, a.Position) + clip.Distance) * clip.Side;
            var bDistance = (Vector3.Dot(clip.Normal, b.Position) + clip.Distance) * clip.Side;
            var aInside = aDistance >= -epsilon;
            var bInside = bDistance >= -epsilon;
            if (aInside)
                result.Add(a);
            if (aInside != bInside)
            {
                var fraction = aDistance / (aDistance - bDistance);
                result.Add(new WorldLiquidVertex
                {
                    Position = Vector3.Lerp(a.Position, b.Position, fraction),
                    Depth = a.Depth + (b.Depth - a.Depth) * fraction,
                    TexCoord = Vector2.Lerp(a.TexCoord, b.TexCoord, fraction),
                    CellCoord = Vector2.Lerp(a.CellCoord, b.CellCoord, fraction)
                });
            }
        }
        return result;
    }

    public static ushort ResolveLiquidType(WmoLiquidInput input)
    {
        var raw = input.GroupLiquid;
        if ((input.RootFlags & 0x4) == 0)
            raw = raw == 15 ? 0 : raw + 1;
        if (raw is > 0 and < 21)
            return LegacyType(raw, input.GroupFlags);
        if (raw is > 0 and <= ushort.MaxValue)
            return (ushort)raw;
        foreach (var tile in input.Tiles)
            if ((tile & 0x0f) != 0x0f)
                return LegacyType((uint)(tile & 0x0f) + 1, input.GroupFlags);
        return 1;
    }

    private static ushort LegacyType(uint raw, uint groupFlags) => ((raw - 1) & 3) switch
    {
        0 => (ushort)((groupFlags & 0x80000) != 0 ? 14 : 13),
        1 => 14,
        2 => 19,
        _ => 20
    };
}
