using System.Numerics;
using Formats = WoWLib.Formats;
using WoWLib;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

/// <summary>
/// Converts WowLib's structured MH2O data into renderer-owned managed geometry.
/// Geometry generation belongs here so it can be tested without Direct3D.
/// </summary>
public static class WorldLiquidMeshBuilder
{
    private const int MaxChunksPerTile = 256;
    private const int MaxLiquidDimension = 8;
    private const float TileSize = 1600f / 3f;
    private const float ChunkSize = TileSize / 16f;
    private const float UnitSize = ChunkSize / 8f;

    public static ParsedWorldLiquid Build(
        Formats.ADT.ADT adt,
        IWorldLiquidMaterialCatalog materialCatalog)
    {
        ArgumentNullException.ThrowIfNull(adt);
        ArgumentNullException.ThrowIfNull(materialCatalog);

        // MH2O was introduced with WotLK. Keep the type switch explicit: the
        // base ADT intentionally does not expose Water because older clients
        // have no MH2O payload. Every value copied below is managed before the
        // caller disposes the native-backed ADT.
        return adt switch
        {
            Formats.ADT.ADTWotlk wotlk => Build(
                adt,
                wotlk.Water.Cells,
                materialCatalog),
            Formats.ADT.ADTCataToLegion cataToLegion => Build(
                adt,
                cataToLegion.Water.Cells,
                materialCatalog),
            Formats.ADT.ADTBfaPlus bfaPlus => Build(
                adt,
                bfaPlus.Water.Cells,
                materialCatalog),
            _ => ParsedWorldLiquid.Empty
        };
    }

    /// <summary>
    /// Builds a mesh from already copied layer values. Keeping this overload
    /// public makes the geometry contract directly testable without a client
    /// installation or native WowLib allocation.
    /// </summary>
    public static ParsedWorldLiquid Build(
        IEnumerable<WorldLiquidLayerInput> layers,
        IWorldLiquidMaterialCatalog materialCatalog)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(materialCatalog);

        var vertices = new List<WorldLiquidVertex>();
        var indices = new List<uint>();
        var batches = new List<ParsedWorldLiquidBatch>();
        var materials = new List<WorldLiquidMaterialDescriptor>();
        var materialIndexes = new Dictionary<WorldLiquidMaterialKey, int>();
        var textureIds = new HashSet<uint>();
        var hasBounds = false;
        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);

        foreach (var layer in layers)
        {
            if (!TryValidateLayer(layer, out var expectedVertexCount))
                continue;

            var material = materialCatalog.Resolve(
                layer.LiquidTypeId,
                layer.LiquidObjectOrLvf);
            if (material is null)
                continue;

            var materialKey = material.Key;
            if (!materialIndexes.TryGetValue(materialKey, out var materialIndex))
            {
                materialIndex = materials.Count;
                materials.Add(material);
                materialIndexes.Add(materialKey, materialIndex);
                foreach (var textureId in material.TextureFileDataIds ?? [])
                {
                    if (textureId != 0)
                        textureIds.Add(textureId);
                }
            }

            var localVertices = new WorldLiquidVertex[expectedVertexCount];
            for (var row = 0; row <= layer.Height; row++)
            {
                for (var column = 0; column <= layer.Width; column++)
                {
                    var vertexIndex = row * (layer.Width + 1) + column;
                    var height = ReadHeight(layer, vertexIndex);
                    if (!float.IsFinite(height))
                        height = float.IsFinite(layer.MinHeight) ? layer.MinHeight : 0f;

                    var position = new Vector3(
                        layer.ChunkPosition.X - (layer.YOffset + row) * UnitSize,
                        layer.ChunkPosition.Y - (layer.XOffset + column) * UnitSize,
                        height);
                    var depth = ReadDepth(layer, vertexIndex);
                    var uv = ReadUv(layer, position, vertexIndex);
                    var cellCoord = new Vector2(
                        column / (float)layer.Width,
                        row / (float)layer.Height);

                    localVertices[vertexIndex] = new WorldLiquidVertex
                    {
                        Position = position,
                        Depth = depth,
                        TexCoord = uv,
                        CellCoord = cellCoord
                    };

                }
            }

            var localIndices = new List<uint>(layer.Width * layer.Height * 6);
            for (var row = 0; row < layer.Height; row++)
            {
                for (var column = 0; column < layer.Width; column++)
                {
                    if (!QuadExists(layer.ExistsBitmap, row * layer.Width + column))
                        continue;

                    var topLeft = (uint)(row * (layer.Width + 1) + column);
                    var topRight = topLeft + 1;
                    var bottomLeft = topLeft + (uint)(layer.Width + 1);
                    var bottomRight = bottomLeft + 1;

                    // The ADT axes decrease in both X and Y as row/column
                    // increase. This order matches the existing terrain mesh
                    // and produces an upward-facing (+Z) normal.
                    localIndices.Add(bottomLeft);
                    localIndices.Add(topLeft);
                    localIndices.Add(topRight);
                    localIndices.Add(topRight);
                    localIndices.Add(bottomRight);
                    localIndices.Add(bottomLeft);
                }
            }

            if (localIndices.Count == 0)
                continue;

            // Bounds describe the emitted topology, rather than every vertex
            // in the rectangular payload. This matters for sparse exists
            // masks: unused vertices must not keep a batch resident or make
            // its culling bounds overly large.
            var localMin = new Vector3(float.MaxValue);
            var localMax = new Vector3(float.MinValue);
            var hasLocalBounds = false;
            foreach (var index in localIndices)
            {
                var position = localVertices[index].Position;
                if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
                    !float.IsFinite(position.Z))
                    continue;

                localMin = Vector3.Min(localMin, position);
                localMax = Vector3.Max(localMax, position);
                hasLocalBounds = true;
            }

            if (!hasLocalBounds)
                continue;

            var firstVertex = (uint)vertices.Count;
            vertices.AddRange(localVertices);
            var firstIndex = (uint)indices.Count;
            foreach (var index in localIndices)
                indices.Add(firstVertex + index);

            var localBounds = new BoundingBox(localMin, localMax);
            batches.Add(new ParsedWorldLiquidBatch(
                layer.ChunkIndex,
                layer.LayerIndex,
                firstIndex,
                (uint)localIndices.Count,
                materialIndex,
                localBounds,
                layer.IsFishable,
                layer.IsDeep)
            {
                LiquidTypeId = layer.LiquidTypeId,
                LiquidObjectOrLvf = layer.LiquidObjectOrLvf,
                Family = material.Family
            });

            if (!hasBounds)
            {
                boundsMin = localMin;
                boundsMax = localMax;
                hasBounds = true;
            }
            else
            {
                boundsMin = Vector3.Min(boundsMin, localMin);
                boundsMax = Vector3.Max(boundsMax, localMax);
            }
        }

        if (batches.Count == 0)
            return ParsedWorldLiquid.Empty;

        return new ParsedWorldLiquid
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Batches = batches.ToArray(),
            Materials = materials.ToArray(),
            TextureFileDataIds = [.. textureIds],
            Bounds = new BoundingBox(boundsMin, boundsMax),
            HasBounds = hasBounds
        };
    }

    private static ParsedWorldLiquid Build(
        Formats.ADT.ADT adt,
        WoWLib.Vector<Formats.ADT.MapChunkLiquid> cells,
        IWorldLiquidMaterialCatalog materialCatalog)
    {
        var layerInputs = new List<WorldLiquidLayerInput>();
        var chunkCount = Math.Min(
            Math.Min(adt.Chunks.Count, cells.Count),
            MaxChunksPerTile);

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var cell = cells[chunkIndex];
            var chunk = adt.Chunks[chunkIndex];
            var chunkPosition = ToVector3(chunk.Header.Position);
            var instances = cell.Instances;
            for (var layerIndex = 0; layerIndex < instances.Count; layerIndex++)
            {
                var instance = instances[layerIndex];
                layerInputs.Add(new WorldLiquidLayerInput
                {
                    ChunkIndex = chunkIndex,
                    LayerIndex = layerIndex,
                    ChunkPosition = chunkPosition,
                    LiquidTypeId = instance.LiquidType,
                    LiquidObjectOrLvf = instance.LiquidObjectOrLvf,
                    VertexFormat = ConvertVertexFormat(instance.VertexFormat),
                    MinHeight = instance.MinHeight,
                    MaxHeight = instance.MaxHeight,
                    XOffset = instance.XOffset,
                    YOffset = instance.YOffset,
                    Width = instance.Width,
                    Height = instance.Height,
                    ExistsBitmap = instance.ExistsBitmap.AsSpan().ToArray(),
                    Heightmap = instance.Heightmap.AsSpan().ToArray(),
                    Depthmap = instance.Depthmap.AsSpan().ToArray(),
                    Uvmap = CopyUvMap(instance.Uvmap),
                    IsFishable = cell.HasAttributes && IsAttributeSet(
                        cell.Fishable,
                        instance.XOffset,
                        instance.YOffset,
                        instance.Width,
                        instance.Height),
                    IsDeep = cell.HasAttributes && IsAttributeSet(
                        cell.Deep,
                        instance.XOffset,
                        instance.YOffset,
                        instance.Width,
                        instance.Height)
                });
            }
        }

        return Build(layerInputs, materialCatalog);
    }

    private static Vector2[] CopyUvMap(WoWLib.Vector<Formats.ADT.Chunks.UvMapEntry> values)
    {
        var result = new Vector2[values.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var value = values[index];
            result[index] = new Vector2(value.X, value.Y);
        }

        return result;
    }

    private static bool IsAttributeSet(
        ulong mask,
        byte xOffset,
        byte yOffset,
        byte width,
        byte height)
    {
        // Fishable/deep masks are defined over the containing 8x8 cell. A
        // layer rectangle can begin at any offset; retain the flag when at
        // least one emitted quad has the corresponding cell attribute.
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var bit = yOffset + row;
                var cellBit = xOffset + column;
                if (bit < 8 && cellBit < 8 && (mask & (1UL << (bit * 8 + cellBit))) != 0)
                    return true;
            }
        }

        return false;
    }

    private static WorldLiquidVertexFormat ConvertVertexFormat(
        Formats.ADT.Chunks.LiquidVertexFormat format) => format switch
        {
            Formats.ADT.Chunks.LiquidVertexFormat.height_depth => WorldLiquidVertexFormat.HeightDepth,
            Formats.ADT.Chunks.LiquidVertexFormat.height_uv => WorldLiquidVertexFormat.HeightUv,
            Formats.ADT.Chunks.LiquidVertexFormat.depth_only => WorldLiquidVertexFormat.DepthOnly,
            Formats.ADT.Chunks.LiquidVertexFormat.height_uv_depth => WorldLiquidVertexFormat.HeightUvDepth,
            _ => WorldLiquidVertexFormat.DepthOnly
        };

    private static bool TryValidateLayer(
        WorldLiquidLayerInput layer,
        out int expectedVertexCount)
    {
        expectedVertexCount = 0;
        if ((uint)layer.ChunkIndex >= MaxChunksPerTile ||
            layer.Width is < 1 or > MaxLiquidDimension ||
            layer.Height is < 1 or > MaxLiquidDimension ||
            layer.XOffset > 7 || layer.YOffset > 7 ||
            layer.XOffset + layer.Width > 8 || layer.YOffset + layer.Height > 8)
            return false;

        expectedVertexCount = checked((layer.Width + 1) * (layer.Height + 1));
        // MH2O flat layers are allowed to omit their height map entirely; the
        // format still carries a min_height value that is the surface height.
        // The same rule applies to optional UV/depth blocks. WowLib decodes
        // those blocks as empty when the offset is absent, so rejecting a
        // shorter array here would discard valid ocean and private-client
        // layers before the per-vertex fallbacks can run.

        if (float.IsFinite(layer.MinHeight) || layer.VertexFormat == WorldLiquidVertexFormat.DepthOnly)
            return true;

        // Height-bearing layouts normally carry a finite MinHeight, but the
        // decoded heightmap is authoritative when a malformed header omits
        // it. Accept the layer if at least one source height is usable; the
        // per-vertex fallback still handles holes in that map safely.
        var carriesHeight = layer.VertexFormat is WorldLiquidVertexFormat.HeightDepth or
            WorldLiquidVertexFormat.HeightUv or
            WorldLiquidVertexFormat.HeightUvDepth;
        return carriesHeight && layer.Heightmap.Any(float.IsFinite);
    }

    private static float ReadHeight(WorldLiquidLayerInput layer, int index)
    {
        var carriesHeight = layer.VertexFormat is WorldLiquidVertexFormat.HeightDepth or
            WorldLiquidVertexFormat.HeightUv or
            WorldLiquidVertexFormat.HeightUvDepth;
        if (carriesHeight && index < layer.Heightmap.Length)
            return layer.Heightmap[index];

        return layer.MinHeight;
    }

    private static float ReadDepth(WorldLiquidLayerInput layer, int index)
    {
        var carriesDepth = layer.VertexFormat is WorldLiquidVertexFormat.HeightDepth or
            WorldLiquidVertexFormat.DepthOnly or
            WorldLiquidVertexFormat.HeightUvDepth;
        if (!carriesDepth)
            return 0f;
        if (index >= layer.Depthmap.Length)
        {
            // Reference viewer defaultDepth is 0 for LVF 0/1/3. Only the
            // depth-only flat layout uses 255 (full/deep) by default.
            return layer.VertexFormat == WorldLiquidVertexFormat.DepthOnly ? 1f : 0f;
        }

        return layer.Depthmap[index] / 255f;
    }

    private static Vector2 ReadUv(WorldLiquidLayerInput layer, Vector3 position, int index)
    {
        var carriesUv = layer.VertexFormat is WorldLiquidVertexFormat.HeightUv or
            WorldLiquidVertexFormat.HeightUvDepth;
        if (carriesUv && index < layer.Uvmap.Length)
        {
            // MH2O UVMapEntry stores signed 16-bit texture coordinates. The
            // client shader converts them with s/t * 3 / 256, not /8.
            return layer.Uvmap[index] * (3f / 256f);
        }

        // This is the planar mapping used by the reference liquid materials
        // when the material requests generated coordinates.
        return new Vector2(position.X * 0.06f, position.Y * 0.06f);
    }

    private static bool QuadExists(byte[] bitmap, int quadIndex) =>
        bitmap.Length == 0 ||
        ((uint)quadIndex >> 3) < (uint)bitmap.Length &&
        (bitmap[quadIndex >> 3] & (1 << (quadIndex & 7))) != 0;

    private static Vector3 ToVector3(Formats.Common.C3Vector value) =>
        new(value.X, value.Y, value.Z);
}
