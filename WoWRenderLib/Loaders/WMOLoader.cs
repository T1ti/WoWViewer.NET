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
        var portalVertices = ReadVector3Array(root.PortalVertices);
        var portals = ReadPortals(root.Portals);
        var portalReferences = ReadPortalReferences(root.PortalRefs);
        var groupFlags = groupInfos.ToArray().Select(static info => info.Flags).ToArray();

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
            var groupBytes = ReadGroupBytes(
                fileSystem,
                fileDataId,
                groupIndex,
                groupIndex < rootData.GroupFileDataIds.Length ? rootData.GroupFileDataIds[groupIndex] : 0);
            var textureCoordinates = ReadTextureCoordinateChunks(groupBytes, vertices.Length);
            var colorSets = ReadVertexColorChunks(groupBytes, vertices.Length);
            var hasPrimaryColors = colorSets[0] is { Length: > 0 };
            if (fileSystem.Kind == StorageKind.Mpq && hasPrimaryColors)
            {
                var firstNonTransition = 0;
                for (var batchIndex = 0; batchIndex < Math.Min(header.TransBatchCount, body.Batches.Count); batchIndex++)
                    firstNonTransition = Math.Max(firstNonTransition, body.Batches[batchIndex].MaxIndex + 1);
                firstNonTransition = Math.Min(firstNonTransition, bodyVertices.Length);
                LegacyFixColorVertexAlpha(colorSets[0], firstNonTransition, rootFlags);
                var positions = new Vector3[firstNonTransition];
                for (var i = 0; i < positions.Length && i < bodyVertices.Length; i++)
                    positions[i] = ToVector3(bodyVertices[i]);
                AttenuateTransitionColors(colorSets[0], positions, rootFlags,
                    header.PortalStart, header.PortalCount,
                    portalVertices, portals, portalReferences, groupFlags);
            }
            var neutralColor = (rootFlags & 0x2) != 0
                ? new Vector4(0f, 0f, 0f, 1f)
                : new Vector4(0.5f, 0.5f, 0.5f, 1f);
            for (var i = 0; i < vertices.Length; i++)
            {
                var primaryUv = GetTextureCoordinate(textureCoordinates, 0, i);
                vertices[i] = new WMOVertex
                {
                    Position = ToVector3(bodyVertices[i]),
                    Normal = i < bodyNormals.Length ? ToVector3(bodyNormals[i]) : Vector3.UnitZ,
                    TexCoord = primaryUv,
                    // Wisp forwards UV0 when a legacy group has no second
                    // MOTV stream; zero UVs would sample one texel everywhere.
                    TexCoord2 = GetTextureCoordinate(textureCoordinates, 1, i,
                        fileSystem.Kind == StorageKind.Mpq ? primaryUv : Vector2.Zero),
                    TexCoord3 = GetTextureCoordinate(textureCoordinates, 2, i),
                    TexCoord4 = GetTextureCoordinate(textureCoordinates, 3, i),
                    Color = hasPrimaryColors ? colorSets[0][i] : neutralColor,
                    Color2 = colorSets[1] is { Length: > 0 } secondColors
                        ? secondColors[i]
                        : i < bodyColors.Length ? ColorVector(bodyColors[i]) : Vector4.Zero,
                    Color3 = Vector4.Zero
                };
            }

            var indices = body.Indices.AsSpan().ToArray();
            var collisionVertices = ReadCollisionVertexBuffer(groupBytes, vertices, indices);
            if (groupBytes.Length == 0)
            {
                if (body is Formats.WMO.Group.WMOGroupBodyDragonflightPlus modernBody &&
                    modernBody.Polys2.Count != 0)
                    collisionVertices = BuildCollisionVertexBuffer(
                        MemoryMarshal.AsBytes(modernBody.Polys2.AsDataSpan()), true, vertices, indices);
                else if (body.Polys.Count != 0)
                    collisionVertices = BuildCollisionVertexBuffer(
                        MemoryMarshal.AsBytes(body.Polys.AsDataSpan()), false, vertices, indices);
            }
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
                    Category = (byte)(batchIndex < header.TransBatchCount ? 0
                        : batchIndex < header.TransBatchCount + header.IntBatchCount ? 1 : 2),
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
                hasPrimaryVertexColors = hasPrimaryColors,
                portalStart = header.PortalStart,
                portalCount = header.PortalCount,
                doodadReferences = doodadRefs,
                liquid = liquid,
                boundingBox = new BoundingBox(ToVector3(bounds.Min), ToVector3(bounds.Max)),
                vertexBuffer = MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(),
                indiceBuffer = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray(),
                collisionVertexBuffer = collisionVertices,
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
            PortalVertices = portalVertices,
            Portals = portals,
            PortalReferences = portalReferences,
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

    private static byte[] ReadGroupBytes(
        Fs.FileSystem fileSystem,
        uint rootFileDataId,
        int groupIndex,
        uint groupFileDataId)
    {
        if (fileSystem.Kind == StorageKind.Mpq &&
            LegacyAssetIds.TryGetPath(fileSystem, rootFileDataId, out var rootPath) &&
            rootPath.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase))
        {
            var groupPath = $"{rootPath[..^4]}_{groupIndex:000}.wmo";
            groupFileDataId = WowlibFileSystem.ResolveAssetId(fileSystem, groupPath);
        }

        if (groupFileDataId == 0)
            // TODO(WMO): Surface missing group bytes in diagnostics. Falling
            // back to neutral MOCV and zero UVs can hide an asset-path error.
            return [];

        try
        {
            return WowlibFileSystem.ReadAsset(fileSystem, groupFileDataId);
        }
        catch
        {
            // TODO(WMO): Report this group read failure with the source path.
            // Some WMO lineages do not expose group FileDataIDs. Keep geometry
            // usable with neutral colors and the shader's zero-UV fallback.
            return [];
        }
    }

    // Wowlib 0.0.9 exposes MOC2 but omits the primary (and second) MOCV
    // chunk, so decode those from the same group bytes used for MOTV.
    internal static Vector4[][] ReadVertexColorChunks(ReadOnlySpan<byte> bytes, int vertexCount)
    {
        var result = new Vector4[2][];
        if (vertexCount <= 0)
            return result;

        var chunkSize = checked(vertexCount * 4);
        const uint mocv = ('M' << 24) | ('O' << 16) | ('C' << 8) | 'V';
        var offsets = FindGroupChunkPayloads(bytes, mocv, chunkSize, 4);
        for (var colorSet = 0; colorSet < Math.Min(offsets.Count, result.Length); colorSet++)
        {
            var colors = new Vector4[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                var pixel = bytes.Slice(offsets[colorSet] + i * 4, 4);
                colors[i] = new Vector4(pixel[2] / 255f, pixel[1] / 255f,
                    pixel[0] / 255f, pixel[3] / 255f);
            }
            result[colorSet] = colors;
        }
        return result;
    }

    // for old clients, In 3.3.5a this function is called ONLY when MOHD flag 0x8 ("flag_do_not_fix_vertex_color_alpha") is NOT set.
    // TODO : This changed in Build 18179
    internal static void LegacyFixColorVertexAlpha(Vector4[] colors, int firstNonTransition, ushort rootFlags)
    {
        if ((rootFlags & 0x8) != 0)
            return;

        for (var i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            var alpha = (uint)Math.Clamp((int)MathF.Round(color.W * 255f), 0, 255);
            static float Fix(float channel, uint alpha, bool transition)
            {
                var value = (uint)Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);
                var fixedValue = transition ? value >> 1 : Math.Min(255u, (value + ((alpha * value) >> 6)) >> 1);
                return fixedValue / 255f;
            }
            var transition = i < firstNonTransition;
            colors[i] = new Vector4(
                Fix(color.X, alpha, transition), Fix(color.Y, alpha, transition),
                Fix(color.Z, alpha, transition), transition ? color.W : 1f);
        }
    }

    // The client's AttenTransVerts rewrites transition MOCV near portals.
    // Its alpha drives the two-pass blend; RGB approaches the neutral 0x7f.
    internal static void AttenuateTransitionColors(Vector4[] colors, ReadOnlySpan<Vector3> positions,
        ushort rootFlags, ushort portalStart, ushort portalCount, ReadOnlySpan<Vector3> portalVertices,
        ReadOnlySpan<PreppedWMOPortal> portals, ReadOnlySpan<PreppedWMOPortalReference> references,
        ReadOnlySpan<uint> groupFlags)
    {
        if ((rootFlags & 0x1) != 0)
            return;

        for (var vertexIndex = 0; vertexIndex < Math.Min(colors.Length, positions.Length); vertexIndex++)
        {
            var position = positions[vertexIndex];
            var weight = 0f;
            var forcedInterior = false;
            for (var referenceIndex = (int)portalStart;
                 referenceIndex < (int)portalStart + portalCount && referenceIndex < references.Length;
                 referenceIndex++)
            {
                var reference = references[referenceIndex];
                if (reference.PortalIndex >= portals.Length || reference.GroupIndex >= groupFlags.Length)
                    continue;
                var portal = portals[reference.PortalIndex];
                if (portal.VertexCount < 3 || portal.StartVertex + portal.VertexCount > portalVertices.Length)
                    continue;
                var polygon = portalVertices.Slice(portal.StartVertex, portal.VertexCount);
                var signedDistance = Vector3.Dot(portal.Normal, position) + portal.Distance;
                var projected = position - portal.Normal * signedDistance;
                float distance;
                if (PointInPortal(projected, polygon, portal.Normal))
                {
                    distance = reference.Side == 1 ? signedDistance : -signedDistance;
                }
                else
                {
                    distance = float.PositiveInfinity;
                    for (var edge = 0; edge < polygon.Length; edge++)
                        distance = Math.Min(distance,
                            DistanceToSegment(position, polygon[edge], polygon[(edge + 1) % polygon.Length]));
                }

                if ((groupFlags[reference.GroupIndex] & 0x48) != 0)
                {
                    var contribution = 1f - 0.15f * Math.Max(distance, 0f);
                    if (contribution > 0.001f)
                        weight += contribution;
                }
                else if (distance > -1f && distance < 1f)
                {
                    forcedInterior = true;
                    break;
                }
            }

            weight = forcedInterior || weight <= 0.001f ? 0f : Math.Min(weight, 1f);
            var source = colors[vertexIndex];
            static float BlendChannel(float channel, float amount)
            {
                var value = (int)MathF.Round(channel * 255f);
                return (int)(value + amount * (127 - value)) / 255f;
            }
            colors[vertexIndex] = new Vector4(
                BlendChannel(source.X, weight), BlendChannel(source.Y, weight),
                BlendChannel(source.Z, weight), (int)(weight * 255f) / 255f);
        }
    }

    private static bool PointInPortal(Vector3 point, ReadOnlySpan<Vector3> polygon, Vector3 normal)
    {
        var positive = false;
        var negative = false;
        for (var i = 0; i < polygon.Length; i++)
        {
            var side = Vector3.Dot(Vector3.Cross(
                polygon[(i + 1) % polygon.Length] - polygon[i], point - polygon[i]), normal);
            positive |= side > 0f;
            negative |= side < 0f;
            if (positive && negative)
                return false;
        }
        return true;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        var along = lengthSquared > 0f
            ? Math.Clamp(Vector3.Dot(point - start, segment) / lengthSquared, 0f, 1f)
            : 0f;
        return Vector3.Distance(point, start + along * segment);
    }

    internal static Vector2[][] ReadTextureCoordinateChunks(ReadOnlySpan<byte> bytes, int vertexCount)
    {
        var result = new Vector2[4][];
        if (vertexCount <= 0)
            return result;

        var chunkSize = checked(vertexCount * sizeof(float) * 2);
        const uint motv = ('M' << 24) | ('O' << 16) | ('T' << 8) | 'V';
        var offsets = FindGroupChunkPayloads(bytes, motv, chunkSize, 1);
        for (var coordinateSet = 0; coordinateSet < Math.Min(offsets.Count, result.Length); coordinateSet++)
        {
            var coordinates = new Vector2[vertexCount];
            for (var i = 0; i < coordinates.Length; i++)
            {
                var coordinateOffset = offsets[coordinateSet] + i * 8;
                coordinates[i] = new Vector2(
                    BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(coordinateOffset, 4))),
                    BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(coordinateOffset + 4, 4))));
            }

            result[coordinateSet] = coordinates;
        }

        return result;
    }

    // WMO child chunks follow their declared sizes, not 4-byte alignment.
    // In particular, an odd number of MOPY triangles puts later MOCV/MOTV
    // headers two bytes off a 4-byte boundary.
    private static List<int> FindGroupChunkPayloads(ReadOnlySpan<byte> bytes, uint chunkId,
        int minimumSize, int sizeMultiple)
    {
        const uint mogp = ('M' << 24) | ('O' << 16) | ('G' << 8) | 'P';
        const int mogpHeaderSize = 68;
        var offsets = new List<int>();

        static void Walk(ReadOnlySpan<byte> data, int start, int end,
            uint desiredId, int minimumSize, int sizeMultiple,
            List<int> found, bool enterGroup)
        {
            for (var offset = start; offset <= end - 8;)
            {
                var size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4));
                var payload = offset + 8;
                if (size < 0 || size > end - payload)
                    break;

                var id = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                if (id == desiredId && size >= minimumSize && size % sizeMultiple == 0)
                    found.Add(payload);
                else if (enterGroup && id == mogp && size >= mogpHeaderSize)
                    Walk(data, payload + mogpHeaderSize, payload + size,
                        desiredId, minimumSize, sizeMultiple, found, false);

                offset = payload + size;
            }
        }

        Walk(bytes, 0, bytes.Length, chunkId, minimumSize, sizeMultiple, offsets, true);
        return offsets;
    }

    internal static byte[] ReadCollisionVertexBuffer(
        ReadOnlySpan<byte> groupBytes,
        ReadOnlySpan<WMOVertex> vertices,
        ReadOnlySpan<ushort> indices)
    {
        var triangleCount = indices.Length / 3;
        if (triangleCount == 0 || vertices.IsEmpty)
            return [];

        const uint mopy = ('M' << 24) | ('O' << 16) | ('P' << 8) | 'Y';
        const uint mpy2 = ('M' << 24) | ('P' << 16) | ('Y' << 8) | '2';
        // MPY2 replaces MOPY from 10.0 onward. Read the on-disk face records
        // so this diagnostic works across Wowlib's versioned group wrappers.
        var modernOffsets = FindGroupChunkPayloads(groupBytes, mpy2, triangleCount * 4, 4);
        var modern = modernOffsets.Count != 0;
        var offsets = modern ? modernOffsets
            : FindGroupChunkPayloads(groupBytes, mopy, triangleCount * 2, 2);
        if (offsets.Count == 0)
            return [];

        var records = groupBytes.Slice(offsets[0], triangleCount * (modern ? 4 : 2));
        return BuildCollisionVertexBuffer(records, modern, vertices, indices);
    }

    private static byte[] BuildCollisionVertexBuffer(
        ReadOnlySpan<byte> records,
        bool modern,
        ReadOnlySpan<WMOVertex> vertices,
        ReadOnlySpan<ushort> indices)
    {
        var triangleCount = Math.Min(indices.Length / 3, records.Length / (modern ? 4 : 2));
        var visibleTriangleCount = 0;
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            if (IsInvisibleCollisionFace(records, triangle, modern) &&
                indices[triangle * 3] < vertices.Length &&
                indices[triangle * 3 + 1] < vertices.Length &&
                indices[triangle * 3 + 2] < vertices.Length)
                visibleTriangleCount++;
        }

        if (visibleTriangleCount == 0)
            return [];

        var result = new WMOCollisionVertex[visibleTriangleCount * 3];
        var destination = 0;
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            if (!IsInvisibleCollisionFace(records, triangle, modern))
                continue;

            var firstIndex = indices[triangle * 3];
            var secondIndex = indices[triangle * 3 + 1];
            var thirdIndex = indices[triangle * 3 + 2];
            if (firstIndex >= vertices.Length || secondIndex >= vertices.Length || thirdIndex >= vertices.Length)
                continue;

            result[destination++] = new WMOCollisionVertex
            {
                Position = vertices[firstIndex].Position,
                Barycentric = new Vector2(1f, 0f)
            };
            result[destination++] = new WMOCollisionVertex
            {
                Position = vertices[secondIndex].Position,
                Barycentric = new Vector2(0f, 1f)
            };
            result[destination++] = new WMOCollisionVertex
            {
                Position = vertices[thirdIndex].Position,
                Barycentric = Vector2.Zero
            };
        }

        return MemoryMarshal.AsBytes(result.AsSpan()).ToArray();
    }

    private static bool IsInvisibleCollisionFace(ReadOnlySpan<byte> records, int triangle, bool modern)
    {
        var recordOffset = triangle * (modern ? 4 : 2);
        var flags = modern
            ? BinaryPrimitives.ReadUInt16LittleEndian(records.Slice(recordOffset, 2))
            : records[recordOffset];
        var materialId = modern
            ? BinaryPrimitives.ReadUInt16LittleEndian(records.Slice(recordOffset + 2, 2))
            : records[recordOffset + 1];

        // MOPY's 0xFF material is Wisp's collision/no-draw sentinel. Flag 0x08
        // also marks a collision face; only show it here when 0x20 (render) is
        // clear, so the overlay does not repaint normal visible geometry.
        return materialId == (modern ? 0xFFFF : 0xFF) ||
               (flags & 0x08) != 0 && (flags & 0x20) == 0;
    }

    internal static Vector2 GetTextureCoordinate(Vector2[][] coordinateSets, int set,
        int vertex, Vector2 fallback = default) =>
        set < coordinateSets.Length && coordinateSets[set] is { Length: > 0 } coordinates && vertex < coordinates.Length
            ? coordinates[vertex]
            : fallback;

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
