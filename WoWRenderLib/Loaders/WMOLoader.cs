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
        if (!fileSystem.Exists(new FileDataId(fileDataId)))
            throw new FileNotFoundException($"WMO {fileDataId} does not exist!");

        using var wmo = Formats.WMO.WMO.ForVersion(fileSystem.Version);
        wmo.Read(fileSystem, new FileKey(new FileDataId(fileDataId)));
        var root = wmo.Root;
        var rootData = ReadRootData(root);
        var groups = wmo.Groups;
        var groupInfos = root.GroupInfos.AsDataSpan();
        var groupNames = root.GroupNames;
        var preppedGroups = new List<PreppedWMOGroup>();

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

            var indices = body.Indices.ToArray();
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
            var doodadRefs = body.DoodadRefs.ToArray();
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
                boundingBox = new BoundingBox(ToVector3(bounds.Min), ToVector3(bounds.Max)),
                vertexBuffer = MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(),
                indiceBuffer = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray(),
                groupBatches = [.. renderBatches]
            });
        }

        var materials = ReadMaterials(fileSystem, rootData);
        var doodadSets = ReadDoodadSets(root);
        var doodads = ReadDoodads(fileSystem, rootData, doodadSets);
        var rootHeader = root.Header;
        var rootBounds = rootHeader.BoundingBox;

        return new PreppedWMO
        {
            FileDataID = fileDataId,
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

    private sealed record RootData(
        Formats.StringBlock? Textures,
        Formats.StringBlock? DoodadNames,
        uint[] GroupFileDataIds,
        uint[] DoodadFileDataIds,
        WoWLib.Vector<Formats.WMO.Root.Chunks.SmoMaterial> Materials,
        WoWLib.Vector<Formats.WMO.Root.Chunks.SmoDoodadDef> DoodadDefinitions,
        WoWLib.Vector<Formats.WMO.Root.Chunks.SmoDoodadSet> DoodadSets);

    private static RootData ReadRootData(Formats.WMO.Root.WMORoot root)
    {
        return root switch
        {
            Formats.WMO.Root.WMORootVanillaToWod value => new(
                value.Textures,
                value.DoodadNames,
                [],
                [],
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            Formats.WMO.Root.WMORootBfa value => new(
                null,
                null,
                value.GroupFdids.ToArray(),
                value.DoodadFdids.ToArray(),
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            Formats.WMO.Root.WMORootLegion value => new(
                value.Textures,
                value.DoodadNames,
                value.GroupFdids.ToArray(),
                [],
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            Formats.WMO.Root.WMORootShadowlandsToDragonflight value => new(
                null,
                null,
                value.GroupFdids.ToArray(),
                value.DoodadFdids.ToArray(),
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            Formats.WMO.Root.WMORootTheWarWithin value => new(
                null,
                null,
                value.GroupFdids.ToArray(),
                value.DoodadFdids.ToArray(),
                value.Materials,
                value.DoodadDefs,
                value.DoodadSets),
            _ => throw new InvalidDataException($"Unsupported wowlib WMO root type {root.GetType().Name}.")
        };
    }

    private static Vector2[][] ReadTextureCoordinates(
        Fs.FileSystem fileSystem,
        uint groupFileDataId,
        int vertexCount)
    {
        if (groupFileDataId == 0 || vertexCount == 0)
            return [[], [], [], []];

        try
        {
            var bytes = fileSystem.ReadFile(new FileDataId(groupFileDataId));
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
                TexFileDataID3 = runtime.Count > 0 ? runtime[0] : 0,
                TexFileDataID4 = runtime.Count > 1 ? runtime[1] : 0,
                TexFileDataID5 = runtime.Count > 2 ? runtime[2] : 0,
                TexFileDataID6 = runtime.Count > 3 ? runtime[3] : 0
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
            var fileDataId = nameIndex < root.DoodadFileDataIds.Length ? root.DoodadFileDataIds[nameIndex] : 0;
            var filename = string.Empty;
            if (fileDataId == 0)
            {
                filename = GetString(root.DoodadNames, nameIndex);
                fileDataId = ResolvePath(fileSystem, filename);
            }

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
        if (value != 0 && fileSystem.Exists(new FileDataId(value)))
            return value;
        return ResolvePath(fileSystem, GetString(textures, value));
    }

    private static uint ResolvePath(Fs.FileSystem fileSystem, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;
        try { return fileSystem.Resolve(new FileKey(path)).Fdid?.Value ?? 0; }
        catch { return 0; }
    }

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
