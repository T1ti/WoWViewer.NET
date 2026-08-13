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
                boundingBox = preppedWMO.BoundingBox,
                boundingRadius = CalculateBoundingRadius(preppedWMO.BoundingBox.Min, preppedWMO.BoundingBox.Max)
            };

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
                    vertexBuffer = vertexBuffer,
                    indiceBuffer = indiceBuffer,
                    verticeCount = (uint)preppedGroup.vertexBuffer.Length / (uint)sizeof(WMOVertex)
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
            return wmoBatch;
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
