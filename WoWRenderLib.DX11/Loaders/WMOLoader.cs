using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWRenderLib.DX11.Cache;
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
                ambientColor = preppedWMO.AmbientColor,
                flags = preppedWMO.Flags,
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

                wmoBatch.groupBatches[g] = new WorldModelGroupBatches()
                {
                    groupName = preppedGroup.groupName,
                    mogiGroupName = preppedGroup.mogiGroupName,
                    vertexBuffer = vertexBuffer,
                    indiceBuffer = indiceBuffer,
                    verticeCount = (uint)preppedGroup.vertexBuffer.Length / (uint)sizeof(WMOVertex),
                    boundingBox = preppedGroup.boundingBox,
                    sourceGroupIndex = preppedGroup.sourceGroupIndex,
                    groupID = preppedGroup.groupID,
                    flags = preppedGroup.flags,
                    mogiFlags = preppedGroup.mogiFlags,
                    portalLinks = BuildPortalLinks(preppedGroup, preppedWMO.PortalReferences, sourceGroupToRenderGroup),
                    doodadReferences = preppedGroup.doodadReferences ?? []
                };
            }

            var renderBatches = new List<WMORenderBatch>();

            for (var g = 0; g < preppedWMO.PreppedWMOGroups.Length; g++)
            {
                var group = preppedWMO.PreppedWMOGroups[g];
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
                        shader = (uint)mat.Shader,
                        materialIndex = groupBatch.MaterialID,
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
                        if (id != 0 && CASC.FileExists(id))
                            BLPCache.GetOrLoad(device, id, preppedWMO.FileDataID);
                    }

                    renderBatches.Add(renderBatch);
                }
            }

            wmoBatch.doodadSets = preppedWMO.DoodadSets;
            wmoBatch.doodads = preppedWMO.Doodads;
            wmoBatch.preppedMats = preppedWMO.Materials;
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

        private static WmoPortal[] BuildPortals(in PreppedWMO preppedWMO)
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

                var normal = source.Normal.LengthSquared() > 0.000001f
                    ? Vector3.Normalize(source.Normal)
                    : Vector3.Zero;
                result[portalIndex] = new WmoPortal
                {
                    Vertices = vertices,
                    Normal = normal,
                    Distance = normal == Vector3.Zero ? 0f : -Vector3.Dot(normal, vertices[0]),
                    Bounds = new BoundingBox(min, max)
                };
            }
            return result;
        }

        private static WmoPortalLink[] BuildPortalLinks(
            in PreppedWMOGroup group,
            PreppedWMOPortalReference[] references,
            int[] sourceGroupToRenderGroup)
        {
            var end = (int)group.portalStart + group.portalCount;
            if (end > references.Length)
                return [];

            var result = new List<WmoPortalLink>(group.portalCount);
            for (var referenceIndex = group.portalStart; referenceIndex < end; referenceIndex++)
            {
                var reference = references[referenceIndex];
                if (reference.GroupIndex >= sourceGroupToRenderGroup.Length)
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

        private static bool ValidatePortalGraph(
            in PreppedWMO preppedWMO,
            int[] sourceGroupToRenderGroup,
            WmoPortal[] portals)
        {
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
            }

            if (wmo.doodads != null)
            {
                foreach (var model in wmo.doodads)
                    M2Cache.Release(model.filedataid, wmo.rootWMOFileDataID);
            }

            if (wmo.preppedMats != null)
            {
                foreach (var mat in wmo.preppedMats)
                {
                    if (CASC.FileExists(mat.TexFileDataID0))
                        BLPCache.Release(mat.TexFileDataID0, wmo.rootWMOFileDataID);
                    if (CASC.FileExists(mat.TexFileDataID1))
                        BLPCache.Release(mat.TexFileDataID1, wmo.rootWMOFileDataID);
                    if (CASC.FileExists(mat.TexFileDataID2))
                        BLPCache.Release(mat.TexFileDataID2, wmo.rootWMOFileDataID);
                    if (CASC.FileExists(mat.TexFileDataID3))
                        BLPCache.Release(mat.TexFileDataID3, wmo.rootWMOFileDataID);
                    if (CASC.FileExists(mat.TexFileDataID4))
                        BLPCache.Release(mat.TexFileDataID4, wmo.rootWMOFileDataID);
                    if (CASC.FileExists(mat.TexFileDataID5))
                        BLPCache.Release(mat.TexFileDataID5, wmo.rootWMOFileDataID);
                    if (CASC.FileExists(mat.TexFileDataID6))
                        BLPCache.Release(mat.TexFileDataID6, wmo.rootWMOFileDataID);
                    if (CASC.FileExists(mat.TexFileDataID7))
                        BLPCache.Release(mat.TexFileDataID7, wmo.rootWMOFileDataID);
                    if (CASC.FileExists(mat.TexFileDataID8))
                        BLPCache.Release(mat.TexFileDataID8, wmo.rootWMOFileDataID);
                }
            }
        }
    }
}
