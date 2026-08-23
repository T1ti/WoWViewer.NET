using System.Numerics;
using System.Runtime.InteropServices;
using WoWLib;
using Formats = WoWLib.Formats;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Cache;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

public static class ADTLoader
{
    public static unsafe ParsedADT ParseADT(MapTile mapTile)
    {
        var wdt = WDTCache.GetOrLoad(mapTile.wdtFileDataID);
        if (!wdt.TryGetTile(mapTile.tileX, mapTile.tileY, out var files) || files.RootAdt == 0)
            throw new FileNotFoundException($"ADT tile {mapTile.tileX}_{mapTile.tileY} is not present in WDT {mapTile.wdtFileDataID}.");

        var fileSystem = WowlibFileSystem.Current;
        using var adt = Formats.ADT.ADT.ForVersion(fileSystem.Version);
        var alphaFormat = (wdt.Flags & (0x4u | 0x80u)) != 0
            ? Formats.ADT.AlphaFormat.highres_8bit
            : Formats.ADT.AlphaFormat.lowres_4bit;
        adt.Read(fileSystem, ResolveFileKey(fileSystem, files.RootAdt), alphaFormat);

        var parsed = new ParsedADT
        {
            rootADTFileDataID = files.RootAdt
        };

        // ADT itself exposes the placement, string, and terrain-chunk fields
        // common to every era in wowlib 0.0.8.  Texture FileDataID tables are
        // still version-specific because older clients store texture names.
        var textureData = ReadTextureData(adt);
        var chunks = adt.Chunks;
        var chunkCount = Math.Min(chunks.Count, 256);
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

        var vertices = new ADTVertex[256 * 145];
        var indices = new int[256 * 768];
        var farLodIndices = new int[256 * 384];
        var chunkBounds = new BoundingBox[256];
        var renderBatches = new ParsedADTRenderBatch[256];
        var indicesOffset = 0;
        var farLodIndicesOffset = 0;
        const float tileSize = 1600.0f / 3.0f;
        const float unitSize = tileSize / 16.0f / 8.0f;
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

            for (var row = 0; row < 17; row++)
            {
                var inner = (row & 1) != 0;
                var halfHeight = row * 0.5f;
                var rowWidth = inner ? 8 : 9;
                for (var column = 0; column < rowWidth; column++)
                {
                    var vertexIndex = GetVertexIndex(row, column);
                    var normal = normals[vertexIndex];
                    var vertex = new ADTVertex
                    {
                        Normal = new Vector3(
                            normal.Normal[0] / 127f,
                            normal.Normal[1] / 127f,
                            normal.Normal[2] / 127f),
                        Color = GetVertexColor(vertexColors, vertexIndex, defaultVertexColor),
                        TexCoord = new Vector2((column + (inner ? 0.5f : 0f)) / 8f, halfHeight / 8f),
                        Position = new Vector3(
                            position.X - halfHeight * unitSize,
                            position.Y - column * unitSize,
                            vertexIndex < heights.Length ? heights[vertexIndex] + position.Z : position.Z)
                    };

                    if (inner)
                        vertex.Position.Y -= 0.5f * unitSize;

                    chunkMin = Vector3.Min(chunkMin, vertex.Position);
                    chunkMax = Vector3.Max(chunkMax, vertex.Position);
                    vertices[chunkIndex * 145 + vertexIndex] = vertex;
                }
            }

            if (chunkIndex == 0)
                parsed.startPos = vertices[0].Position;

            var highResolutionHoles = (flags & 0x10000) != 0;
            var vertexBase = chunkIndex * 145;
            for (var holeRow = 0; holeRow < 8; holeRow++)
            {
                for (var holeColumn = 0; holeColumn < 8; holeColumn++)
                {
                    var j = 9 + holeRow * 17 + holeColumn;
                    var xx = holeColumn;
                    var yy = holeRow;
                    var isHole = highResolutionHoles
                        ? ((header.HolesHighRes >> (yy * 8 + xx)) & 1) != 0
                        : (header.HolesLowRes & (1 << ((xx / 2) + (yy / 2) * 4))) != 0;

                    if (isHole)
                    {
                        for (var i = 0; i < 12; i++)
                            indices[indicesOffset++] = 0;
                        for (var i = 0; i < 6; i++)
                            farLodIndices[farLodIndicesOffset++] = 0;
                    }
                    else
                    {
                        indices[indicesOffset++] = vertexBase + j + 8;
                        indices[indicesOffset++] = vertexBase + j - 9;
                        indices[indicesOffset++] = vertexBase + j;
                        indices[indicesOffset++] = vertexBase + j - 9;
                        indices[indicesOffset++] = vertexBase + j - 8;
                        indices[indicesOffset++] = vertexBase + j;
                        indices[indicesOffset++] = vertexBase + j - 8;
                        indices[indicesOffset++] = vertexBase + j + 9;
                        indices[indicesOffset++] = vertexBase + j;
                        indices[indicesOffset++] = vertexBase + j + 9;
                        indices[indicesOffset++] = vertexBase + j + 8;
                        indices[indicesOffset++] = vertexBase + j;

                        var topLeft = vertexBase + yy * 17 + xx;
                        var topRight = topLeft + 1;
                        var bottomLeft = vertexBase + (yy + 1) * 17 + xx;
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
                modern.DiffuseTextureIds.ToArray(),
                modern.HeightTextureIds.ToArray(),
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
                material.scale = MathF.Pow(2f, (parameter.Flags & 0xF0) >> 4);
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
        var materialIds = new int[8];
        var heightIds = new int[8];
        var scales = new float[8];
        var heightScales = new float[8];
        var heightOffsets = new float[8];
        Array.Fill(materialIds, -1);
        Array.Fill(heightIds, -1);
        Array.Fill(scales, 1f);
        Array.Fill(heightScales, 1f);
        Array.Fill(heightOffsets, 1f);

        // An ADT has at most eight texture layers. Keep the selected alpha
        // maps in a fixed array so conversion does not hash a layer index for
        // every output byte.
        var alphaLayers = new byte[]?[8];
        for (var layerIndex = 0; layerIndex < Math.Min(layers.Length, 8); layerIndex++)
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

            if (layerIndex < alphaMaps.Count)
                alphaLayers[layerIndex] = alphaMaps[layerIndex].ToArray();
        }

        var alphaMaterials = new byte[2][];
        for (var group = 0; group < 2; group++)
        {
            var baseLayer = group * 4;
            var layer0 = alphaLayers[baseLayer];
            var layer1 = alphaLayers[baseLayer + 1];
            var layer2 = alphaLayers[baseLayer + 2];
            var layer3 = alphaLayers[baseLayer + 3];
            if (layer0 is null && layer1 is null && layer2 is null && layer3 is null)
                continue;

            var alphaData = new byte[64 * 64 * 4];
            CopyAlphaChannel(layer0, alphaData, 0);
            CopyAlphaChannel(layer1, alphaData, 1);
            CopyAlphaChannel(layer2, alphaData, 2);
            CopyAlphaChannel(layer3, alphaData, 3);
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

    private static void CopyAlphaChannel(byte[]? source, byte[] destination, int channel)
    {
        if (source is null)
            return;

        var count = Math.Min(source.Length, 64 * 64);
        var destinationIndex = channel;
        for (var sourceIndex = 0; sourceIndex < count; sourceIndex++, destinationIndex += 4)
            destination[destinationIndex] = source[sourceIndex];
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
                0x40);
            var position = ToVector3(placement.Position);
            var rotation = ToVector3(placement.Rotation);
            result[i] = new Doodad
            {
                position = new Vector3(-(position.X - 17066.666f), position.Y, position.Z - 17066.666f),
                rotation = rotation,
                scale = placement.Scale / 1024f,
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
                0x8);
            var position = ToVector3(placement.Position);
            var rotation = ToVector3(placement.Rotation);
            result[i] = new WorldModelBatch
            {
                position = new Vector3(-(position.X - 17066.666f), position.Y, position.Z - 17066.666f),
                rotation = rotation,
                fileDataID = fileDataId,
                uniqueID = placement.UniqueId,
                flags = placement.Flags,
                doodadSet = placement.DoodadSet,
                nameSet = placement.NameSet,
                scale = placement.Scale / 1024f,
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
    internal static int GetVertexIndex(int row, int column) => row * 9 - row / 2 + column;

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
