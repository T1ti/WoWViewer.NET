using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using WoWLib;
using Formats = WoWLib.Formats;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Cache;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

public static class ADTLoader
{
    private const int MaxChunksPerTile = 256;
    private const int VerticesPerChunk = 145;
    private const int IndicesPerChunk = 768;
    private const int FarLodIndicesPerChunk = 384;
    private const int TerrainGridRows = 17;
    private const int TerrainGridRowStride = TerrainGridRows;
    private const int TerrainOuterRowWidth = 9;
    private const int TerrainInnerRowWidth = 8;
    private const int TerrainSubdivisionsPerSide = 8;
    private const int HoleRows = 8;
    private const int HoleColumns = 8;
    private const int LowResolutionHoleColumns = 4;
    private const int IndicesPerHole = 12;
    private const int FarLodIndicesPerHole = 6;
    private const int MaxTextureLayers = 8;
    private const int AlphaMapSize = 64;
    private const int AlphaChannelCount = 4;
    private const float NormalComponentScale = 127f;
    private const float TileSize = 1600f / 3f;
    private const int TileSubdivisionsPerSide = 16;
    private const float WorldOriginOffset = 17066.666f;
    private const float PlacementScaleDenominator = 1024f;
    private const int TextureScaleMask = 0xF0;
    private const int TextureScaleShift = 4;

    public static unsafe ParsedADT ParseADT(MapTile mapTile)
    {
        var wdt = WDTCache.GetOrLoad(mapTile.wdtFileDataID);
        if (!wdt.TryGetTile(mapTile.tileX, mapTile.tileY, out var files) || files.RootAdt == 0)
            throw new FileNotFoundException($"ADT tile {mapTile.tileX}_{mapTile.tileY} is not present in WDT {mapTile.wdtFileDataID}.");

        var fileSystem = WowlibFileSystem.Current;
        using var adt = Formats.ADT.ADT.ForVersion(fileSystem.Version);
        var wdtFlags = (Formats.WDT.Root.Chunks.MapHeaderFlags)wdt.Flags;
        var alphaFormat = (wdtFlags &
            (Formats.WDT.Root.Chunks.MapHeaderFlags.adt_has_big_alpha |
             Formats.WDT.Root.Chunks.MapHeaderFlags.adt_has_height_texturing)) != 0
            ? Formats.ADT.AlphaFormat.highres_8bit
            : Formats.ADT.AlphaFormat.lowres_4bit;
        adt.Read(fileSystem, ResolveFileKey(fileSystem, files.RootAdt), alphaFormat);

        var parsed = new ParsedADT
        {
            rootADTFileDataID = files.RootAdt
        };

        // ADT itself exposes placement, string, and terrain-chunk fields
        // through the version-agnostic 0.0.9 base. Texture FileDataID tables
        // are still version-specific because older clients store texture names.
        var textureData = ReadTextureData(adt);
        var chunks = adt.Chunks;
        var chunkCount = Math.Min(chunks.Count, MaxChunksPerTile);
        if (chunkCount == 0)
            return parsed;

        var modelFilenames = adt.ModelFilenames;
        var modelNameOffsets = adt.ModelNameOffsets;
        var wmoFilenames = adt.WmoFilenames;
        var wmoNameOffsets = adt.WmoNameOffsets;
        var fileIds = new HashSet<uint>();
        var materials = BuildMaterials(
            fileSystem,
            textureData.DiffuseTextureIds,
            textureData.HeightTextureIds,
            textureData.TextureParams,
            adt.Textures,
            fileIds);

        var vertices = new ADTVertex[MaxChunksPerTile * VerticesPerChunk];
        var indices = new int[MaxChunksPerTile * IndicesPerChunk];
        var farLodIndices = new int[MaxChunksPerTile * FarLodIndicesPerChunk];
        var chunkBounds = new BoundingBox[MaxChunksPerTile];
        var renderBatches = new ParsedADTRenderBatch[MaxChunksPerTile];
        var indicesOffset = 0;
        var farLodIndicesOffset = 0;
        const float unitSize = TileSize / TileSubdivisionsPerSide / TerrainSubdivisionsPerSide;
        var defaultVertexColor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            var header = chunk.Header;
            var heights = chunk.Heights.AsSpan();
            var normals = chunk.Normals.AsDataSpan();
            Span<Formats.Common.CImVector.Data> vertexColors = default;
            if (chunk is Formats.ADT.MapChunkCataPlus cata)
                vertexColors = cata.VertexColors.AsDataSpan();
            else if (chunk is Formats.ADT.MapChunkWotlk wotlk)
                vertexColors = wotlk.VertexColors.AsDataSpan();
            var flags = header.Flags;
            var position = ToVector3(header.Position);
            var chunkMin = new Vector3(float.MaxValue);
            var chunkMax = new Vector3(float.MinValue);

            for (var row = 0; row < TerrainGridRows; row++)
            {
                var inner = (row & 1) != 0;
                var halfHeight = row * 0.5f;
                var rowWidth = inner ? TerrainInnerRowWidth : TerrainOuterRowWidth;
                for (var column = 0; column < rowWidth; column++)
                {
                    var vertexIndex = GetVertexIndex(row, column);
                    var normal = normals[vertexIndex];
                    var vertex = new ADTVertex
                    {
                        Normal = new Vector3(
                            normal.Normal[0] / NormalComponentScale,
                            normal.Normal[1] / NormalComponentScale,
                            normal.Normal[2] / NormalComponentScale),
                        Color = GetVertexColor(vertexColors, vertexIndex, defaultVertexColor),
                        TexCoord = new Vector2(
                            (column + (inner ? 0.5f : 0f)) / TerrainSubdivisionsPerSide,
                            halfHeight / TerrainSubdivisionsPerSide),
                        Position = new Vector3(
                            position.X - halfHeight * unitSize,
                            position.Y - column * unitSize,
                            vertexIndex < heights.Length ? heights[vertexIndex] + position.Z : position.Z)
                    };

                    if (inner)
                        vertex.Position.Y -= 0.5f * unitSize;

                    chunkMin = Vector3.Min(chunkMin, vertex.Position);
                    chunkMax = Vector3.Max(chunkMax, vertex.Position);
                    vertices[chunkIndex * VerticesPerChunk + vertexIndex] = vertex;
                }
            }

            if (chunkIndex == 0)
                parsed.startPos = vertices[0].Position;

            var chunkFlags = (Formats.ADT.Chunks.MapChunkFlags)flags;
            var highResolutionHoles = (chunkFlags & Formats.ADT.Chunks.MapChunkFlags.high_res_holes) != 0;
            var vertexBase = chunkIndex * VerticesPerChunk;
            for (var holeRow = 0; holeRow < HoleRows; holeRow++)
            {
                for (var holeColumn = 0; holeColumn < HoleColumns; holeColumn++)
                {
                    var j = TerrainOuterRowWidth + holeRow * TerrainGridRowStride + holeColumn;
                    var xx = holeColumn;
                    var yy = holeRow;
                    var isHole = highResolutionHoles
                        ? ((header.HolesHighRes >> (yy * HoleColumns + xx)) & 1) != 0
                        : (header.HolesLowRes & (1 << ((xx / 2) + (yy / 2) * LowResolutionHoleColumns))) != 0;

                    if (isHole)
                    {
                        for (var i = 0; i < IndicesPerHole; i++)
                            indices[indicesOffset++] = 0;
                        for (var i = 0; i < FarLodIndicesPerHole; i++)
                            farLodIndices[farLodIndicesOffset++] = 0;
                    }
                    else
                    {
                        indices[indicesOffset++] = vertexBase + j + TerrainInnerRowWidth;
                        indices[indicesOffset++] = vertexBase + j - TerrainOuterRowWidth;
                        indices[indicesOffset++] = vertexBase + j;
                        indices[indicesOffset++] = vertexBase + j - TerrainOuterRowWidth;
                        indices[indicesOffset++] = vertexBase + j - TerrainInnerRowWidth;
                        indices[indicesOffset++] = vertexBase + j;
                        indices[indicesOffset++] = vertexBase + j - TerrainInnerRowWidth;
                        indices[indicesOffset++] = vertexBase + j + TerrainOuterRowWidth;
                        indices[indicesOffset++] = vertexBase + j;
                        indices[indicesOffset++] = vertexBase + j + TerrainOuterRowWidth;
                        indices[indicesOffset++] = vertexBase + j + TerrainInnerRowWidth;
                        indices[indicesOffset++] = vertexBase + j;

                        var topLeft = vertexBase + yy * TerrainGridRowStride + xx;
                        var topRight = topLeft + 1;
                        var bottomLeft = vertexBase + (yy + 1) * TerrainGridRowStride + xx;
                        var bottomRight = bottomLeft + 1;
                        farLodIndices[farLodIndicesOffset++] = bottomLeft;
                        farLodIndices[farLodIndicesOffset++] = topLeft;
                        farLodIndices[farLodIndicesOffset++] = topRight;
                        farLodIndices[farLodIndicesOffset++] = topRight;
                        farLodIndices[farLodIndicesOffset++] = bottomRight;
                        farLodIndices[farLodIndicesOffset++] = bottomLeft;
                    }
                }
            }

            renderBatches[chunkIndex] = BuildRenderBatch(
                fileSystem,
                chunk,
                textureData.DiffuseTextureIds,
                materials,
                fileIds,
                adt.Textures);
            chunkBounds[chunkIndex] = new BoundingBox(chunkMin, chunkMax);
        }

        parsed.vertexBuffer = MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray();
        parsed.indiceBuffer = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray();
        parsed.farLodIndiceBuffer = MemoryMarshal.AsBytes(farLodIndices.AsSpan()).ToArray();
        parsed.renderBatches = renderBatches;
        parsed.chunkBounds = chunkBounds;
        parsed.doodads = BuildDoodads(
            fileSystem,
            adt.DoodadPlacements.AsDataSpan(),
            modelFilenames,
            modelNameOffsets.AsSpan());
        parsed.worldModelBatches = BuildWmos(
            fileSystem,
            adt.WmoPlacements.AsDataSpan(),
            wmoFilenames,
            wmoNameOffsets.AsSpan());
        parsed.blpFileDataIDs = [.. fileIds];
        return parsed;
    }

    private sealed record TextureData(
        uint[] DiffuseTextureIds,
        uint[] HeightTextureIds,
        WoWLib.Vector<Formats.ADT.Chunks.SMTextureParams>? TextureParams);

    private static TextureData ReadTextureData(Formats.ADT.ADT adt)
    {
        return adt switch
        {
            Formats.ADT.ADTBfaPlus modern => new TextureData(
                modern.DiffuseTextureIds.AsSpan().ToArray(),
                modern.HeightTextureIds.AsSpan().ToArray(),
                modern.TextureParams),
            _ => new TextureData([], [], null)
        };
    }

    private static Vector4 GetVertexColor(
        ReadOnlySpan<Formats.Common.CImVector.Data> vertexColors,
        int index,
        Vector4 fallback)
    {
        if (index < vertexColors.Length)
        {
            var color = vertexColors[index];
            return new Vector4(color.B / 255f, color.G / 255f, color.R / 255f, color.A / 255f);
        }

        return fallback;
    }

    private static Dictionary<uint, ADTMaterial> BuildMaterials(
        Fs.FileSystem fileSystem,
        uint[] textureIds,
        uint[] heightTextureIds,
        WoWLib.Vector<Formats.ADT.Chunks.SMTextureParams>? textureParams,
        Formats.StringBlock textures,
        HashSet<uint> usedIds)
    {
        var materials = new Dictionary<uint, ADTMaterial>();
        var stringCount = textures.Empty ? 0 : textures.Entries().Count;
        for (var i = 0; i < textureIds.Length || i < stringCount; i++)
        {
            var diffuse = i < textureIds.Length ? textureIds[i] : 0;
            if (diffuse == 0)
                diffuse = ResolvePath(fileSystem, GetString(textures, (uint)i));

            var material = new ADTMaterial
            {
                texture = (int)diffuse,
                scale = 1f,
                heightScale = 0f,
                heightOffset = 1f
            };

            if (textureParams != null && i < textureParams.Count)
            {
                var parameter = textureParams[i];
                material.scale = MathF.Pow(
                    2f,
                    (parameter.Flags & TextureScaleMask) >> TextureScaleShift);
                material.heightScale = parameter.HeightScale;
                material.heightOffset = parameter.HeightOffset;
                if (i < heightTextureIds.Length && heightTextureIds[i] != 0 &&
                    fileSystem.Exists(new FileDataId(heightTextureIds[i])))
                    material.heightTexture = (int)heightTextureIds[i];
                else
                    material.heightTexture = (int)diffuse;
            }

            materials[diffuse] = material;
            if (diffuse != 0)
                usedIds.Add(diffuse);
            if (material.heightTexture != 0)
                usedIds.Add((uint)material.heightTexture);
        }

        return materials;
    }

    private static ParsedADTRenderBatch BuildRenderBatch(
        Fs.FileSystem fileSystem,
        Formats.ADT.MapChunk chunk,
        uint[] textureIds,
        Dictionary<uint, ADTMaterial> materials,
        HashSet<uint> usedIds,
        Formats.StringBlock textures)
    {
        var layers = chunk.Layers.AsDataSpan();
        // AlphaMaps is a vector of byte vectors, not a scalar vector. Keep
        // the outer vector typed and only materialize each selected map.
        var alphaMaps = chunk.AlphaMaps;
        var materialIds = new int[MaxTextureLayers];
        var heightIds = new int[MaxTextureLayers];
        var scales = new float[MaxTextureLayers];
        var heightScales = new float[MaxTextureLayers];
        var heightOffsets = new float[MaxTextureLayers];
        Array.Fill(materialIds, -1);
        Array.Fill(heightIds, -1);
        Array.Fill(scales, 1f);
        Array.Fill(heightScales, 1f);
        Array.Fill(heightOffsets, 1f);

        // An ADT has at most eight texture layers. Keep the selected alpha
        // maps in a fixed array so conversion does not hash a layer index for
        // every output byte.
        for (var layerIndex = 0; layerIndex < Math.Min(layers.Length, MaxTextureLayers); layerIndex++)
        {
            var layer = layers[layerIndex];
            var textureIndex = layer.TextureId;
            var diffuse = textureIndex < textureIds.Length
                ? textureIds[textureIndex]
                : ResolvePath(fileSystem, GetString(textures, textureIndex));
            materialIds[layerIndex] = (int)diffuse;
            if (materials.TryGetValue(diffuse, out var material))
            {
                heightIds[layerIndex] = material.heightTexture;
                scales[layerIndex] = material.scale;
                heightScales[layerIndex] = material.heightScale;
                heightOffsets[layerIndex] = material.heightOffset;
            }
            if (diffuse != 0)
                usedIds.Add(diffuse);

        }

        var alphaMaterials = new byte[2][];
        for (var group = 0; group < 2; group++)
        {
            var baseLayer = group * 4;
            var layerCount = Math.Min(AlphaChannelCount, alphaMaps.Count - baseLayer);
            if (layerCount <= 0)
                continue;

            var alphaData = new byte[AlphaMapSize * AlphaMapSize * AlphaChannelCount];
            for (var channel = 0; channel < layerCount; channel++)
            {
                CopyAlphaChannel(
                    alphaMaps[baseLayer + channel].AsSpan(),
                    alphaData,
                    channel);
            }
            alphaMaterials[group] = alphaData;
        }

        return new ParsedADTRenderBatch
        {
            materialFDIDs = materialIds,
            heightMaterialFDIDs = heightIds,
            alphaMaterials = alphaMaterials,
            scales = scales,
            heightScales = heightScales,
            heightOffsets = heightOffsets
        };
    }

    internal static void CopyAlphaChannel(
        ReadOnlySpan<byte> source,
        byte[] destination,
        int channel)
    {
        var count = Math.Min(source.Length, AlphaMapSize * AlphaMapSize);
        if ((uint)channel >= AlphaChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channel));

        var requiredLength = count == 0
            ? 0
            : ((count - 1) * AlphaChannelCount) + channel + 1;
        if (destination.Length < requiredLength)
            throw new ArgumentException("The alpha destination is too small.", nameof(destination));

        ref var sourceStart = ref MemoryMarshal.GetReference(source);
        ref var destinationStart = ref Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(destination),
            channel);
        for (var index = 0; index < count; index++)
        {
            Unsafe.Add(ref destinationStart, index * AlphaChannelCount) =
                Unsafe.Add(ref sourceStart, index);
        }
    }

    private static Doodad[] BuildDoodads(
        Fs.FileSystem fileSystem,
        ReadOnlySpan<Formats.Common.SmDoodadDef.Data> placements,
        Formats.StringBlock modelFilenames,
        ReadOnlySpan<uint> modelNameOffsets)
    {
        var result = new Doodad[placements.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var placement = placements[i];
            var fileDataId = ResolvePlacementFileDataIdSpan(
                fileSystem,
                modelFilenames,
                modelNameOffsets,
                placement.NameId,
                placement.Flags,
                (uint)Formats.Common.DoodadDefFlags.entry_is_fdid);
            var position = ToVector3(placement.Position);
            var rotation = ToVector3(placement.Rotation);
            result[i] = new Doodad
            {
                position = new Vector3(-(position.X - WorldOriginOffset), position.Y, position.Z - WorldOriginOffset),
                rotation = rotation,
                scale = placement.Scale / PlacementScaleDenominator,
                fileDataID = fileDataId,
                uniqueID = placement.UniqueId,
                flags = placement.Flags
            };
        }
        return result;
    }

    private static WorldModelBatch[] BuildWmos(
        Fs.FileSystem fileSystem,
        ReadOnlySpan<Formats.Common.SmMapObjDef.Data> placements,
        Formats.StringBlock wmoFilenames,
        ReadOnlySpan<uint> wmoNameOffsets)
    {
        var result = new WorldModelBatch[placements.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var placement = placements[i];
            var fileDataId = ResolvePlacementFileDataIdSpan(
                fileSystem,
                wmoFilenames,
                wmoNameOffsets,
                placement.NameId,
                placement.Flags,
                (uint)Formats.Common.MapObjDefFlags.entry_is_fdid);
            var position = ToVector3(placement.Position);
            var rotation = ToVector3(placement.Rotation);
            result[i] = new WorldModelBatch
            {
                position = new Vector3(-(position.X - WorldOriginOffset), position.Y, position.Z - WorldOriginOffset),
                rotation = rotation,
                fileDataID = fileDataId,
                uniqueID = placement.UniqueId,
                flags = placement.Flags,
                doodadSet = placement.DoodadSet,
                nameSet = placement.NameSet,
                scale = placement.Scale / PlacementScaleDenominator,
                doodadSetIDs = [placement.DoodadSet]
            };
        }
        return result;
    }

    private static uint ResolveIndexedPath(
        Fs.FileSystem fileSystem,
        Formats.StringBlock? stringBlock,
        ReadOnlySpan<uint> offsets,
        uint index)
    {
        if (stringBlock == null)
            return 0;
        var offset = index < offsets.Length ? offsets[(int)index] : index;
        return ResolvePath(fileSystem, GetString(stringBlock, offset));
    }

    private static uint ResolvePlacementFileDataIdSpan(
        Fs.FileSystem fileSystem,
        Formats.StringBlock? stringBlock,
        ReadOnlySpan<uint> offsets,
        uint nameId,
        uint flags,
        uint entryIsFdidFlag)
    {
        return (flags & entryIsFdidFlag) != 0
            ? nameId
            : ResolveIndexedPath(fileSystem, stringBlock, offsets, nameId);
    }

    internal static uint ResolvePlacementFileDataId(
        Fs.FileSystem fileSystem,
        Formats.StringBlock? stringBlock,
        WoWLib.Vector<uint>? offsets,
        uint nameId,
        uint flags,
        uint entryIsFdidFlag)
    {
        ReadOnlySpan<uint> offsetSpan = offsets is null ? default : offsets.AsSpan();
        return ResolvePlacementFileDataIdSpan(
            fileSystem,
            stringBlock,
            offsetSpan,
            nameId,
            flags,
            entryIsFdidFlag);
    }

    // ADT's 17 rows alternate between nine outer and eight inner vertices.
    // This computes the packed source/destination offset for those rows.
    internal static int GetVertexIndex(int row, int column) =>
        row * TerrainOuterRowWidth - row / 2 + column;

    private static Vector3 ToVector3(Formats.Common.C3Vector value) => new(value.X, value.Y, value.Z);

    private static Vector3 ToVector3(Formats.Common.C3Vector.Data value) => new(value.X, value.Y, value.Z);

    private static string GetString(Formats.StringBlock block, uint offset)
    {
        if (block.Empty)
            return string.Empty;
        try
        {
            return block.At(offset);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static uint ResolvePath(Fs.FileSystem? fileSystem, string path)
    {
        if (fileSystem == null || string.IsNullOrWhiteSpace(path))
            return 0;
        try
        {
            return fileSystem.Resolve(new FileKey(path)).Fdid?.Value ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static FileKey ResolveFileKey(Fs.FileSystem fileSystem, uint fileDataId)
    {
        var key = fileSystem.Resolve(new FileKey(new FileDataId(fileDataId)));
        return string.IsNullOrWhiteSpace(key.Path)
            ? new FileKey(new FileDataId(fileDataId))
            : key;
    }
}
