using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWLib;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Renderer;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Loaders
{
    public class WMOLoader
    {
        public static unsafe WorldModel LoadWMO(PreppedWMO preppedWMO, ComPtr<ID3D11Device> device)
        {
            var wmoBatch = new WorldModel()
            {
                groupBatches = new WorldModelGroupBatches[preppedWMO.PreppedWMOGroups.Length],
                rootWMOFileDataID = preppedWMO.FileDataID,
                legacyLighting = preppedWMO.LegacyLighting,
                wrath335 = preppedWMO.Wrath335,
                ambientColor = preppedWMO.AmbientColor,
                flags = preppedWMO.Flags,
                fogs = preppedWMO.Fogs,
                boundingBox = preppedWMO.BoundingBox,
                boundingRadius = CalculateBoundingRadius(preppedWMO.BoundingBox.Min, preppedWMO.BoundingBox.Max)
            };

            var sourceGroupToRenderGroup = new int[Math.Max(0, preppedWMO.SourceGroupCount)];
            Array.Fill(sourceGroupToRenderGroup, -1);
            for (var groupIndex = 0; groupIndex < preppedWMO.PreppedWMOGroups.Length; groupIndex++)
            {
                var sourceGroupIndex = preppedWMO.PreppedWMOGroups[groupIndex].sourceGroupIndex;
                if ((uint)sourceGroupIndex < (uint)sourceGroupToRenderGroup.Length)
                    sourceGroupToRenderGroup[sourceGroupIndex] = groupIndex;
            }

            wmoBatch.portals = BuildPortals(preppedWMO);
            wmoBatch.portalGraphValid = ValidatePortalGraph(preppedWMO, sourceGroupToRenderGroup, wmoBatch.portals);

            for (var g = 0; g < preppedWMO.PreppedWMOGroups.Length; g++)
            {
                var preppedGroup = preppedWMO.PreppedWMOGroups[g];
                var raycastVertices = ExtractRaycastVertices(preppedGroup.vertexBuffer);
                var raycastIndices = preppedGroup.raycastIndices ?? MemoryMarshal
                    .Cast<byte, ushort>(preppedGroup.indiceBuffer)
                    .ToArray();

                ComPtr<ID3D11Buffer> vertexBuffer = default;

                var bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)preppedGroup.vertexBuffer.Length,
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.VertexBuffer
                };

                fixed (byte* vertexData = preppedGroup.vertexBuffer)
                {
                    var subresourceData = new SubresourceData
                    {
                        PSysMem = vertexData
                    };

                    SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref vertexBuffer));
                }

                ComPtr<ID3D11Buffer> indiceBuffer = default;

                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)preppedGroup.indiceBuffer.Length,
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.IndexBuffer
                };

                fixed (byte* indiceData = preppedGroup.indiceBuffer)
                {
                    var subresourceData = new SubresourceData
                    {
                        PSysMem = indiceData
                    };

                    SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref indiceBuffer));
                }

                ComPtr<ID3D11Buffer> collisionVertexBuffer = default;
                var collisionBytes = preppedGroup.collisionVertexBuffer;
                if (collisionBytes is { Length: > 0 })
                {
                    bufferDesc = new BufferDesc
                    {
                        ByteWidth = (uint)collisionBytes.Length,
                        Usage = Usage.Default,
                        BindFlags = (uint)BindFlag.VertexBuffer
                    };
                    fixed (byte* collisionData = collisionBytes)
                    {
                        var subresourceData = new SubresourceData { PSysMem = collisionData };
                        SilkMarshal.ThrowHResult(device.CreateBuffer(
                            in bufferDesc, in subresourceData, ref collisionVertexBuffer));
                    }
                }

                wmoBatch.groupBatches[g] = new WorldModelGroupBatches()
                {
                    groupName = preppedGroup.groupName,
                    mogiGroupName = preppedGroup.mogiGroupName,
                    vertexBuffer = vertexBuffer,
                    indiceBuffer = indiceBuffer,
                    collisionVertexBuffer = collisionVertexBuffer,
                    collisionVertexCount = (uint)(collisionBytes?.Length ?? 0) / (uint)sizeof(WMOCollisionVertex),
                    raycastVertices = raycastVertices,
                    raycastIndices = raycastIndices,
                    viewerBsp = preppedGroup.viewerBsp,
                    verticeCount = (uint)preppedGroup.vertexBuffer.Length / (uint)sizeof(WMOVertex),
                    boundingBox = preppedGroup.boundingBox,
                    sourceGroupIndex = preppedGroup.sourceGroupIndex,
                    groupID = preppedGroup.groupID,
                    flags = preppedGroup.flags,
                    hasPrimaryVertexColors = preppedGroup.hasPrimaryVertexColors,
                    mogiFlags = preppedGroup.mogiFlags,
                    fogIds = preppedGroup.fogIds,
                    mogiBoundingBox = preppedGroup.mogiBoundingBox,
                    portalLinks = BuildPortalLinks(preppedGroup, preppedWMO.PortalReferences, sourceGroupToRenderGroup),
                    doodadReferences = preppedGroup.doodadReferences ?? [],
                    liquid = WorldLiquidLoader.Upload(device,
                        preppedGroup.liquid ?? ParsedWorldLiquid.Empty,
                        preppedWMO.FileDataID)
                };
            }

            var renderBatches = new List<WMORenderBatch>();
            var textureReferences = new List<uint>();
            wmoBatch.firstRenderBatchByGroup = new int[preppedWMO.PreppedWMOGroups.Length];
            wmoBatch.renderBatchCountByGroup = new int[preppedWMO.PreppedWMOGroups.Length];

            for (var g = 0; g < preppedWMO.PreppedWMOGroups.Length; g++)
            {
                var group = preppedWMO.PreppedWMOGroups[g];
                wmoBatch.firstRenderBatchByGroup[g] = renderBatches.Count;
                if (group.groupBatches == null) continue;
                for (var i = 0; i < group.groupBatches.Length; i++)
                {
                    var groupBatch = group.groupBatches[i];
                    var mat = preppedWMO.Materials[groupBatch.MaterialID];

                    var renderBatch = new WMORenderBatch
                    {
                        firstFace = groupBatch.FirstFace,
                        numFaces = (uint)groupBatch.NumFaces,
                        blendType = mat.BlendMode,
                        groupID = (uint)g,
                        shader = (uint)WmoMaterialPolicy.ResolveShader(
                            preppedWMO.LegacyLighting, mat.Shader, mat.TexFileDataID1 != 0),
                        materialIndex = groupBatch.MaterialID,
                        lightingMode = WmoMaterialPolicy.ResolveLightingMode(
                            preppedWMO.LegacyLighting,
                            preppedWMO.Flags,
                            group.flags,
                            group.hasPrimaryVertexColors,
                            groupBatch.Category,
                            mat.Flags),
                        category = groupBatch.Category,
                        bounds = groupBatch.Bounds,
                        hasBounds = groupBatch.HasBounds,
                        materialFDIDs = [
                            mat.TexFileDataID0,
                            mat.TexFileDataID1,
                            mat.TexFileDataID2,
                            mat.PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader ? mat.TexFileDataID3 : 0,
                            mat.PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader ? mat.TexFileDataID4 : 0,
                            mat.PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader ? mat.TexFileDataID5 : 0,
                            mat.PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader ? mat.TexFileDataID6 : 0,
                            mat.PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader ? mat.TexFileDataID7 : 0,
                            mat.PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader ? mat.TexFileDataID8 : 0,
                        ]
                    };

                    // Preload BLPs, only do this once here so that we track users properly
                    foreach (var id in renderBatch.materialFDIDs)
                    {
                        if (HasTexture(id))
                        {
                            BLPCache.GetOrLoad(device, id, preppedWMO.FileDataID);
                            textureReferences.Add(id);
                        }
                    }

                    renderBatches.Add(renderBatch);
                }
                wmoBatch.renderBatchCountByGroup[g] = renderBatches.Count - wmoBatch.firstRenderBatchByGroup[g];
            }

            wmoBatch.doodadSets = preppedWMO.DoodadSets;
            wmoBatch.doodads = preppedWMO.Doodads;
            wmoBatch.preppedMats = preppedWMO.Materials;
            wmoBatch.textureReferences = [.. textureReferences];
            //wmoBatch.mats = mats;
            wmoBatch.wmoRenderBatches = [.. renderBatches];
            wmoBatch.doodads = preppedWMO.Doodads;
            wmoBatch.doodadsReferencedByGroups = new bool[wmoBatch.doodads.Length];
            foreach (var group in wmoBatch.groupBatches)
            {
                foreach (var doodadIndex in group.doodadReferences)
                {
                    if (doodadIndex < wmoBatch.doodadsReferencedByGroups.Length)
                        wmoBatch.doodadsReferencedByGroups[doodadIndex] = true;
                    else
                        wmoBatch.portalGraphValid = false;
                }
            }
            return wmoBatch;
        }

        private static Vector3[] ExtractRaycastVertices(byte[] vertexBytes)
        {
            var source = MemoryMarshal.Cast<byte, WMOVertex>(vertexBytes);
            var positions = new Vector3[source.Length];
            for (var index = 0; index < source.Length; index++)
                positions[index] = source[index].Position;
            return positions;
        }

        internal static WmoPortal[] BuildPortals(in PreppedWMO preppedWMO)
        {
            var result = new WmoPortal[preppedWMO.Portals.Length];
            for (var portalIndex = 0; portalIndex < result.Length; portalIndex++)
            {
                var source = preppedWMO.Portals[portalIndex];
                var end = (int)source.StartVertex + source.VertexCount;
                if (source.VertexCount < 3 || end > preppedWMO.PortalVertices.Length)
                    continue;

                var vertices = preppedWMO.PortalVertices
                    .AsSpan(source.StartVertex, source.VertexCount)
                    .ToArray();
                var min = vertices[0];
                var max = vertices[0];
                foreach (var vertex in vertices.AsSpan(1))
                {
                    min = Vector3.Min(min, vertex);
                    max = Vector3.Max(max, vertex);
                }

                var normalLength = source.Normal.Length();
                var exactWrath = preppedWMO.Wrath335 && preppedWMO.LegacyLighting;
                var normal = exactWrath ? source.Normal : normalLength > 0.000001f
                    ? source.Normal / normalLength
                    : Vector3.Zero;
                result[portalIndex] = new WmoPortal
                {
                    Vertices = vertices,
                    Normal = normal,
                    Distance = exactWrath ? source.Distance : normal == Vector3.Zero ? 0f : source.Distance / normalLength,
                    Bounds = new BoundingBox(min, max)
                };
            }
            return result;
        }

        internal static WmoPortalLink[] BuildPortalLinks(
            in PreppedWMOGroup group,
            PreppedWMOPortalReference[] references,
            int[] sourceGroupToRenderGroup)
        {
            var end = (int)group.portalStart + group.portalCount;
            if (end > references.Length)
                return [];

            var result = new List<WmoPortalLink>(group.portalCount);
            for (var referenceIndex = (int)group.portalStart; referenceIndex < end; referenceIndex++)
            {
                var reference = references[referenceIndex];
                if (reference.GroupIndex == ushort.MaxValue ||
                    reference.GroupIndex >= sourceGroupToRenderGroup.Length)
                    continue;
                var targetGroupIndex = sourceGroupToRenderGroup[reference.GroupIndex];
                if (targetGroupIndex < 0)
                    continue;
                result.Add(new WmoPortalLink
                {
                    PortalIndex = reference.PortalIndex,
                    TargetGroupIndex = (ushort)targetGroupIndex,
                    Side = reference.Side
                });
            }
            return [.. result];
        }

        internal static bool ValidatePortalGraph(
            in PreppedWMO preppedWMO,
            int[] sourceGroupToRenderGroup,
            WmoPortal[] portals)
        {
            if (preppedWMO.Wrath335 && preppedWMO.LegacyLighting)
            {
                // 0x7AC194 consumes only MOGP-owned ranges. 0x7AC1A9
                // skips the null destination before reading its portal index.
                // Links are directional; neither a reverse reference nor a
                // nonempty destination range is required.
                foreach (var group in preppedWMO.PreppedWMOGroups)
                {
                    var end = (int)group.portalStart + group.portalCount;
                    if (end > preppedWMO.PortalReferences.Length)
                        return false;
                    for (var index = (int)group.portalStart; index < end; index++)
                    {
                        var reference = preppedWMO.PortalReferences[index];
                        if (reference.GroupIndex == ushort.MaxValue)
                            continue;
                        if (reference.GroupIndex >= sourceGroupToRenderGroup.Length ||
                            reference.PortalIndex >= portals.Length)
                            return false;
                        var portal = portals[reference.PortalIndex];
                        if (portal.Vertices is not { Length: >= 3 } || portal.Normal == Vector3.Zero)
                            return false;
                    }
                }
                return true;
            }

            // A 3.3.5 WMO can have no portal references. The client still runs
            // outdoor MOGI exterior/always-draw group culling for that case;
            // rejecting the empty graph made our fallback render every group.
            if (preppedWMO.LegacyLighting && preppedWMO.PortalReferences.Length == 0 &&
                preppedWMO.PreppedWMOGroups.All(group => group.portalCount == 0))
                return true;

            if (portals.Length == 0 ||
                preppedWMO.PortalReferences.Length == 0 ||
                sourceGroupToRenderGroup.Length == 0)
            {
                return false;
            }

            foreach (var portal in portals)
            {
                if (portal.Vertices == null || portal.Vertices.Length < 3 || portal.Normal == Vector3.Zero)
                    return false;
            }

            foreach (var group in preppedWMO.PreppedWMOGroups)
            {
                if ((int)group.portalStart + group.portalCount > preppedWMO.PortalReferences.Length)
                    return false;
            }

            foreach (var reference in preppedWMO.PortalReferences)
            {
                if (reference.PortalIndex >= portals.Length ||
                    reference.GroupIndex >= sourceGroupToRenderGroup.Length)
                {
                    return false;
                }
            }

            return true;
        }

        private static float CalculateBoundingRadius(Vector3 min, Vector3 max)
        {
            var center = (min + max) * 0.5f;
            return Vector3.Distance(center, max);
        }

        public static void UnloadWMO(WorldModel wmo)
        {
            for (var g = 0; g < wmo.groupBatches.Length; g++)
            {
                wmo.groupBatches[g].vertexBuffer.Dispose();
                wmo.groupBatches[g].indiceBuffer.Dispose();
                wmo.groupBatches[g].collisionVertexBuffer.Dispose();
                var liquid = wmo.groupBatches[g].liquid;
                WorldLiquidLoader.Unload(ref liquid, wmo.rootWMOFileDataID);
            }

            foreach (var fileDataId in wmo.textureReferences ?? [])
                BLPCache.Release(fileDataId, wmo.rootWMOFileDataID);
        }

        private static bool HasTexture(uint fileDataId)
        {
            if (fileDataId == 0)
                return false;

            try
            {
                var fileSystem = WowlibFileSystem.Current;
                return WowlibFileSystem.AssetExists(fileSystem, fileDataId);
            }
            catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
            {
                return false;
            }
        }
    }
}
