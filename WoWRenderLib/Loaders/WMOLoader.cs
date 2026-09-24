using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWLib;
using Formats = WoWLib.Formats;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Renderer;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

public static class WMOLoader
{
    public static PreppedWMO ParseWMO(uint fileDataId, string fileName = "")
    {
        var fileSystem = WowlibFileSystem.Current;
        WorldLiquidMaterialCatalog.Shared.Configure(fileSystem);
        if (!WowlibFileSystem.AssetExists(fileSystem, fileDataId))
            throw new FileNotFoundException($"WMO {fileDataId} does not exist!");

        using var wmo = Formats.WMO.WMO.ForVersion(fileSystem.Version);
        using var wmoKey = WowlibFileSystem.AssetKey(fileSystem, fileDataId);
        wmo.Read(fileSystem, wmoKey);
        var root = wmo.Root;
        var rootData = ReadRootData(root);
        var groups = wmo.Groups;
        var groupInfos = root.GroupInfos.AsDataSpan();
        var groupNames = root.GroupNames;
        var preppedGroups = new List<PreppedWMOGroup>();
        var materials = ReadMaterials(fileSystem, rootData);
        var rootFlags = root.Header.Flags;

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var body = group.Body;
            var header = body.Header;
            var groupInfo = groupIndex < groupInfos.Length ? groupInfos[groupIndex] : default;
            var nameOffset = groupInfo.NameOffset;
            if (nameOffset == 0)
                nameOffset = checked((int)header.GroupName);
            var groupName = GetString(groupNames, (uint)Math.Max(0, nameOffset)).Replace(" ", "_");
            if (string.Equals(groupName, "antiportal", StringComparison.OrdinalIgnoreCase))
                continue;

            var bodyVertices = body.Vertices.AsDataSpan();
            var bodyNormals = body.Normals.AsDataSpan();
            var bodyColors = body.VertexColors2.AsDataSpan();
            var vertices = new WMOVertex[bodyVertices.Length];
            var textureCoordinates = ReadTextureCoordinates(
                fileSystem,
                fileDataId,
                groupIndex,
                groupIndex < rootData.GroupFileDataIds.Length ? rootData.GroupFileDataIds[groupIndex] : 0,
                vertices.Length);
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new WMOVertex
                {
                    Position = ToVector3(bodyVertices[i]),
                    Normal = i < bodyNormals.Length ? ToVector3(bodyNormals[i]) : Vector3.UnitZ,
                    TexCoord = GetTextureCoordinate(textureCoordinates, 0, i),
                    TexCoord2 = GetTextureCoordinate(textureCoordinates, 1, i),
                    TexCoord3 = GetTextureCoordinate(textureCoordinates, 2, i),
                    TexCoord4 = GetTextureCoordinate(textureCoordinates, 3, i),
                    Color = Vector4.Zero,
                    Color2 = i < bodyColors.Length ? ColorVector(bodyColors[i]) : Vector4.Zero,
                    Color3 = Vector4.Zero
                };
            }

            var indices = body.Indices.AsSpan().ToArray();
            var renderBatches = new List<PreppedWMOGroupBatch>(body.Batches.Count);
            for (var batchIndex = 0; batchIndex < body.Batches.Count; batchIndex++)
            {
                var batch = body.Batches[batchIndex];
                var materialId = batch is Formats.WMO.Group.Chunks.WMOBatchLegionPlus modern && (batch.Flags & 2) != 0
                    ? modern.MaterialIdLarge
                    : batch.MaterialId;
                renderBatches.Add(new PreppedWMOGroupBatch
                {
                    FirstFace = batch.StartIndex,
                    NumFaces = batch.Count,
                    MaterialID = materialId
                });
            }

            var bounds = header.BoundingBox;
            var doodadRefs = body.DoodadRefs.AsSpan().ToArray();
            var liquidClips = new List<WmoLiquidClip>();
            for (var referenceIndex = (int)header.PortalStart;
                 referenceIndex < (int)header.PortalStart + header.PortalCount &&
                 referenceIndex < root.PortalRefs.Count;
                 referenceIndex++)
            {
                var reference = root.PortalRefs[referenceIndex];
                if (reference.Side == 0 || reference.GroupIndex == groupIndex ||
                    reference.GroupIndex >= groups.Count || reference.PortalIndex >= root.Portals.Count)
                    continue;
                var neighborLiquid = groups[reference.GroupIndex].Body.Liquid;
                if (neighborLiquid.Empty)
                    continue;
                var origin = ToVector3(neighborLiquid.BaseCoords);
                var size = neighborLiquid.TilesDim;
                var portal = root.Portals[reference.PortalIndex];
                liquidClips.Add(new WmoLiquidClip(
                    new Vector2(origin.X, origin.Y),
                    new Vector2(
                        origin.X + size.X * WmoLiquidMeshBuilder.GridStep,
                        origin.Y + size.Y * WmoLiquidMeshBuilder.GridStep),
                    ToVector3(portal.Plane.Normal),
                    portal.Plane.Distance,
                    reference.Side));
            }
            var liquid = ReadLiquid(body.Liquid, header.GroupLiquid, header.Flags,
                groupInfo.Flags, rootFlags, materials, [.. liquidClips]);
            preppedGroups.Add(new PreppedWMOGroup
            {
                sourceGroupIndex = groupIndex,
                groupID = (uint)preppedGroups.Count,
                groupName = groupName,
                mogiGroupName = groupName,
                mogiFlags = groupInfo.Flags,
                flags = header.Flags,
                portalStart = header.PortalStart,
                portalCount = header.PortalCount,
                doodadReferences = doodadRefs,
                liquid = liquid,
                boundingBox = new BoundingBox(ToVector3(bounds.Min), ToVector3(bounds.Max)),
                vertexBuffer = MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(),
                indiceBuffer = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray(),
                groupBatches = [.. renderBatches]
            });
        }

        var doodadSets = ReadDoodadSets(root);
        var doodads = ReadDoodads(fileSystem, rootData, doodadSets);
        var rootHeader = root.Header;
        var rootBounds = rootHeader.BoundingBox;

        return new PreppedWMO
        {
            FileDataID = fileDataId,
            LegacyLighting = fileSystem.Kind == StorageKind.Mpq,
            AmbientColor = PackColor(rootHeader.AmbientColor),
            Flags = rootHeader.Flags,
            BoundingBox = new BoundingBox(ToVector3(rootBounds.Min), ToVector3(rootBounds.Max)),
            Doodads = doodads,
            DoodadSets = doodadSets,
            Materials = materials,
            PreppedWMOGroups = [.. preppedGroups],
            PortalVertices = ReadVector3Array(root.PortalVertices),
            Portals = ReadPortals(root.Portals),
            PortalReferences = ReadPortalReferences(root.PortalRefs),
            SourceGroupCount = groups.Count
        };
    }

    private static ParsedWorldLiquid ReadLiquid(
        Formats.WMO.Group.Chunks.MliqData source,
        uint groupLiquid,
        uint groupFlags,
        uint mogiFlags,
        ushort rootFlags,
        PreppedWMOMaterial[] materials,
        WmoLiquidClip[] sharedClips)
    {
        if (source.Empty)
            return ParsedWorldLiquid.Empty;

        var dimensions = source.VertsDim;
        var tileDimensions = source.TilesDim;
        var sourceVertices = source.Vertices.AsDataSpan();
        var sourceTiles = source.Tiles.AsDataSpan();
        var heights = new float[sourceVertices.Length];
        var depths = new byte[sourceVertices.Length];
        var uvs = new Vector2[sourceVertices.Length];
        for (var index = 0; index < sourceVertices.Length; index++)
        {
            var vertex = sourceVertices[index];
            heights[index] = vertex.Height;
            depths[index] = vertex.Flow1;
            var s = unchecked((short)(vertex.Flow1 | vertex.Flow2 << 8));
            var t = unchecked((short)(vertex.Flow1Pct | vertex.Filler << 8));
            uvs[index] = new Vector2(s, t);
        }
        var tiles = new byte[sourceTiles.Length];
        for (var index = 0; index < tiles.Length; index++)
            tiles[index] = sourceTiles[index].Flags;

        var materialId = source.MaterialId;
        var interiorColor = (rootFlags & 0x4) == 0 && materialId < materials.Length
            ? UnpackColor(materials[materialId].Color3)
            : Vector4.One;
        return WmoLiquidMeshBuilder.Build(new WmoLiquidInput
        {
            XVertices = dimensions.X,
            YVertices = dimensions.Y,
            XTiles = tileDimensions.X,
            YTiles = tileDimensions.Y,
            Origin = ToVector3(source.BaseCoords),
            GroupLiquid = groupLiquid,
            GroupFlags = groupFlags,
            MogiFlags = mogiFlags,
            RootFlags = rootFlags,
            MaterialId = materialId,
            MaterialCount = materials.Length,
            InteriorColor = interiorColor,
            Heights = heights,
            Depths = depths,
            AuthoredUvs = uvs,
            Tiles = tiles,
            SharedClips = sharedClips
        }, WorldLiquidMaterialCatalog.Shared);
    }

    private static Vector4 UnpackColor(uint color) => new(
        ((color >> 16) & 0xff) / 255f,
        ((color >> 8) & 0xff) / 255f,
        (color & 0xff) / 255f,
        ((color >> 24) & 0xff) / 255f);

    private sealed record RootData(
        Formats.StringBlock? Textures,
        Formats.StringBlock? DoodadNames,
        uint[] GroupFileDataIds,
        uint[] DoodadFileDataIds,
        DoodadReferenceKind DoodadReferenceKind,
        WoWLib.Vector<Formats.WMO.Root.Chunks.SmoMaterial> Materials,
        WoWLib.Vector<Formats.WMO.Root.Chunks.SmoDoodadDef> DoodadDefinitions,
        WoWLib.Vector<Formats.WMO.Root.Chunks.SmoDoodadSet> DoodadSets);

    private enum DoodadReferenceKind
    {
        ModnNames,
        ModiFileDataIds
    }

    private static RootData ReadRootData(Formats.WMO.Root.WMORoot root)
    {
        return root switch
        {
            Formats.WMO.Root.WMORootVanillaToWod value => new(
                value.Textures,
                value.DoodadNames,
                [],
                [],
                DoodadReferenceKind.ModnNames,
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            Formats.WMO.Root.WMORootBfa value => new(
                null,
                null,
                value.GroupFdids.AsSpan().ToArray(),
                value.DoodadFdids.AsSpan().ToArray(),
                DoodadReferenceKind.ModiFileDataIds,
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            Formats.WMO.Root.WMORootLegion value => new(
                value.Textures,
                value.DoodadNames,
                value.GroupFdids.AsSpan().ToArray(),
                [],
                DoodadReferenceKind.ModnNames,
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            Formats.WMO.Root.WMORootShadowlandsToDragonflight value => new(
                null,
                null,
                value.GroupFdids.AsSpan().ToArray(),
                value.DoodadFdids.AsSpan().ToArray(),
                DoodadReferenceKind.ModiFileDataIds,
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            Formats.WMO.Root.WMORootTheWarWithin value => new(
                null,
                null,
                value.GroupFdids.AsSpan().ToArray(),
                value.DoodadFdids.AsSpan().ToArray(),
                DoodadReferenceKind.ModiFileDataIds,
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            _ => throw new InvalidDataException($"Unsupported wowlib WMO root type {root.GetType().Name}.")
        };
    }

    private static Vector2[][] ReadTextureCoordinates(
        Fs.FileSystem fileSystem,
        uint rootFileDataId,
        int groupIndex,
        uint groupFileDataId,
        int vertexCount)
    {
        if (fileSystem.Kind == StorageKind.Mpq &&
            LegacyAssetIds.TryGetPath(fileSystem, rootFileDataId, out var rootPath) &&
            rootPath.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase))
        {
            var groupPath = $"{rootPath[..^4]}_{groupIndex:000}.wmo";
            groupFileDataId = WowlibFileSystem.ResolveAssetId(fileSystem, groupPath);
        }

        if (groupFileDataId == 0 || vertexCount == 0)
            return [[], [], [], []];

        try
        {
            var bytes = WowlibFileSystem.ReadAsset(fileSystem, groupFileDataId);
            return ReadTextureCoordinateChunks(bytes, vertexCount);
        }
        catch
        {
            // Some older WMO lineages do not expose group FileDataIDs. Keep
            // geometry usable and use the shader's zero-UV fallback.
            return [[], [], [], []];
        }
    }

    internal static Vector2[][] ReadTextureCoordinateChunks(ReadOnlySpan<byte> bytes, int vertexCount)
    {
        var result = new Vector2[4][];
        if (vertexCount <= 0)
            return result;

        var chunkSize = checked(vertexCount * sizeof(float) * 2);
        var coordinateSet = 0;
        const uint motv = ('M' << 24) | ('O' << 16) | ('T' << 8) | 'V';
        for (var offset = 0; offset + 8 <= bytes.Length && coordinateSet < result.Length; offset += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4)) != motv)
                continue;

            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 4, 4));
            if (size != chunkSize || size < 0 || offset + 8 + size > bytes.Length)
                continue;

            var coordinates = new Vector2[vertexCount];
            for (var i = 0; i < coordinates.Length; i++)
            {
                var coordinateOffset = offset + 8 + i * 8;
                coordinates[i] = new Vector2(
                    BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(coordinateOffset, 4))),
                    BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(coordinateOffset + 4, 4))));
            }

            result[coordinateSet++] = coordinates;
        }

        return result;
    }

    private static Vector2 GetTextureCoordinate(Vector2[][] coordinateSets, int set, int vertex) =>
        set < coordinateSets.Length && coordinateSets[set] is { Length: > 0 } coordinates && vertex < coordinates.Length
            ? coordinates[vertex]
            : Vector2.Zero;

    private static PreppedWMOMaterial[] ReadMaterials(Fs.FileSystem fileSystem, RootData root)
    {
        // SmoMaterial.Data exposes the fixed RunTimeData buffer directly,
        // while the typed wrapper provides the safe live view used here.
        var materials = root.Materials;
        var result = new PreppedWMOMaterial[materials.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var material = materials[i];
            var shader = (int)material.Shader;
            var shaderPair = shader >= 0 && shader < ShaderEnums.WMOShaders.Count
                ? ShaderEnums.WMOShaders[shader]
                : (ShaderEnums.WMOVertexShader.None, ShaderEnums.WMOPixelShader.None);
            var runtime = material.RunTimeData;
            result[i] = new PreppedWMOMaterial
            {
                Shader = shader,
                VertexShader = shaderPair.Item1,
                PixelShader = shaderPair.Item2,
                BlendMode = material.BlendMode,
                Flags = material.Flags,
                Color1 = PackColor(material.SidnColor),
                Color1B = PackColor(material.FrameSidnColor),
                Color2 = material.Color2,
                Color3 = PackColor(material.DiffColor),
                GroundType = material.GroundType,
                Flags3 = material.Flags2,
                TexFileDataID0 = ResolveTexture(fileSystem, root.Textures, material.Texture1),
                TexFileDataID1 = ResolveTexture(fileSystem, root.Textures, material.Texture2),
                TexFileDataID2 = ResolveTexture(fileSystem, root.Textures, material.Texture3),
                TexFileDataID3 = fileSystem.Kind == StorageKind.Casc && runtime.Count > 0 ? runtime[0] : 0,
                TexFileDataID4 = fileSystem.Kind == StorageKind.Casc && runtime.Count > 1 ? runtime[1] : 0,
                TexFileDataID5 = fileSystem.Kind == StorageKind.Casc && runtime.Count > 2 ? runtime[2] : 0,
                TexFileDataID6 = fileSystem.Kind == StorageKind.Casc && runtime.Count > 3 ? runtime[3] : 0
            };
        }
        return result;
    }

    private static string[] ReadDoodadSets(Formats.WMO.Root.WMORoot root)
    {
        // Doodad set names are exposed by the typed wrapper; the Data mirror
        // only contains the fixed wire bytes and has no name helper.
        var doodadSets = root.DoodadSets;
        var result = new string[doodadSets.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = doodadSets[i].name;
        return result;
    }

    private static WMODoodad[] ReadDoodads(Fs.FileSystem fileSystem, RootData root, string[] doodadSets)
    {
        // The typed view supplies derived NameIndex/Orientation accessors;
        // the raw Data mirror stores NameAndFlags and fixed wire fields.
        var doodadDefinitions = root.DoodadDefinitions;
        var doodadSetRecords = root.DoodadSets;
        var result = new WMODoodad[doodadDefinitions.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var doodad = doodadDefinitions[i];
            var nameIndex = doodad.NameIndex;
            var filename = string.Empty;
            var fileDataId = root.DoodadReferenceKind switch
            {
                DoodadReferenceKind.ModnNames => ResolveLegacyDoodadModel(
                    fileSystem,
                    root.DoodadNames,
                    nameIndex,
                    out filename),
                DoodadReferenceKind.ModiFileDataIds => nameIndex < root.DoodadFileDataIds.Length
                    ? root.DoodadFileDataIds[nameIndex]
                    : 0u,
                _ => 0u
            };

            var setIndex = 0u;
            for (var set = 0; set < doodadSetRecords.Count; set++)
            {
                var record = doodadSetRecords[set];
                if ((uint)i >= record.StartIndex && (uint)i < record.StartIndex + record.Count)
                {
                    setIndex = (uint)set;
                    break;
                }
            }

            var nameAndFlags = doodad.NameAndFlags;
            var orientation = doodad.Orientation;
            result[i] = new WMODoodad
            {
                filename = filename,
                filedataid = fileDataId,
                flags = (short)(nameAndFlags >> 24),
                position = ToVector3(doodad.Position),
                rotation = new Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W),
                scale = doodad.Scale,
                color = ColorVector(doodad.Color),
                doodadSet = setIndex
            };
        }
        return result;
    }

    private static Vector3[] ReadVector3Array(WoWLib.Vector<Formats.Common.C3Vector> source)
    {
        var sourceData = source.AsDataSpan();
        var result = new Vector3[sourceData.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = ToVector3(sourceData[i]);
        return result;
    }

    private static PreppedWMOPortal[] ReadPortals(WoWLib.Vector<Formats.WMO.Root.Chunks.SmoPortal> source)
    {
        var sourceData = source.AsDataSpan();
        var result = new PreppedWMOPortal[sourceData.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var portal = sourceData[i];
            result[i] = new PreppedWMOPortal
            {
                StartVertex = portal.StartVertex,
                VertexCount = portal.Count,
                Normal = ToVector3(portal.Plane.Normal),
                Distance = portal.Plane.Distance
            };
        }
        return result;
    }

    private static PreppedWMOPortalReference[] ReadPortalReferences(WoWLib.Vector<Formats.WMO.Root.Chunks.SmoPortalRef> source)
    {
        var sourceData = source.AsDataSpan();
        var result = new PreppedWMOPortalReference[sourceData.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var reference = sourceData[i];
            result[i] = new PreppedWMOPortalReference
            {
                PortalIndex = reference.PortalIndex,
                GroupIndex = reference.GroupIndex,
                Side = reference.Side
            };
        }
        return result;
    }

    private static uint ResolveTexture(Fs.FileSystem fileSystem, Formats.StringBlock? textures, uint value)
    {
        if (value != 0 && fileSystem.Kind == StorageKind.Casc && fileSystem.Exists(new FileDataId(value)))
            return value;
        return ResolvePath(fileSystem, GetString(textures, value));
    }

    private static uint ResolvePath(Fs.FileSystem fileSystem, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;
        try { return WowlibFileSystem.ResolveAssetId(fileSystem, path); }
        catch { return 0; }
    }

    private static uint ResolveDoodadModelPath(Fs.FileSystem fileSystem, string path)
    {
        var fileDataId = ResolvePath(fileSystem, path);
        if (fileDataId == 0 && GetLegacyDoodadModelPath(path) is { } fallbackPath)
            fileDataId = ResolvePath(fileSystem, fallbackPath);
        return fileDataId;
    }

    private static uint ResolveLegacyDoodadModel(
        Fs.FileSystem fileSystem,
        Formats.StringBlock? names,
        uint nameOffset,
        out string filename)
    {
        filename = GetString(names, nameOffset);
        return ResolveDoodadModelPath(fileSystem, filename);
    }

    internal static string? GetLegacyDoodadModelPath(string path) =>
        path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(path, ".m2")
            : null;

    private static string GetString(Formats.StringBlock? block, uint offset)
    {
        if (block == null || block.Empty)
            return string.Empty;
        try { return block.At(offset); }
        catch { return string.Empty; }
    }

    private static Vector3 ToVector3(Formats.Common.C3Vector value) => new(value.X, value.Y, value.Z);

    private static Vector3 ToVector3(Formats.Common.C3Vector.Data value) => new(value.X, value.Y, value.Z);

    private static Vector4 ColorVector(Formats.Common.CImVector color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);

    private static Vector4 ColorVector(Formats.Common.CImVector.Data color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);

    private static uint PackColor(Formats.Common.CArgb color) =>
        (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);

    private static uint PackColor(Formats.Common.CImVector color) =>
        (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
}
