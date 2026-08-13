using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.Vulkan;
using System.Numerics;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Loaders
{
    class ADTLoader
    {
        public static unsafe Terrain LoadADT(ComPtr<ID3D11Device> device, ParsedADT parsedADT)
        {
            Terrain result = new();

            var renderBatches = new ADTRenderBatch[256];

            var holesHighRes = new byte[8];
            var defaultVertexColor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
            for (var c = 0; c < parsedADT.renderBatches.Length; c++)
            {
                var renderBatch = parsedADT.renderBatches[c];
                var batch = new ADTRenderBatch();
                var alphaLayerMats = new ComPtr<ID3D11ShaderResourceView>[2];
                for (var i = 0; i < renderBatch.alphaMaterials.Length; i++)
                {
                    if (renderBatch.alphaMaterials[i] == null)
                    {
                        alphaLayerMats[i] = default;
                    }
                    else
                    {
                        alphaLayerMats[i] = BLPLoader.GenerateAlphaTexture(device, renderBatch.alphaMaterials[i]);
                    }
                }

                batch.heightScales = renderBatch.heightScales;
                batch.heightOffsets = renderBatch.heightOffsets;
                batch.materialFDIDs = renderBatch.materialFDIDs;
                batch.heightMaterialFDIDs = renderBatch.heightMaterialFDIDs;
                batch.alphaMaterialID = alphaLayerMats;
                batch.scales = renderBatch.scales;
                renderBatches[c] = batch;
            }

            var bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)parsedADT.vertexBuffer.Length,
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.VertexBuffer
            };

            fixed (byte* vertexData = parsedADT.vertexBuffer)
            {
                var subresourceData = new SubresourceData
                {
                    PSysMem = vertexData
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref result.vertexBuffer));
            }

            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)parsedADT.indiceBuffer.Length,
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.IndexBuffer
            };

            fixed (byte* indexData = parsedADT.indiceBuffer)
            {
                var subresourceData = new SubresourceData
                {
                    PSysMem = indexData
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref result.indiceBuffer));
            }

            foreach (var usedBLP in parsedADT.blpFileDataIDs)
                BLPCache.GetOrLoad(device, usedBLP, parsedADT.rootADTFileDataID);

            result.doodads = parsedADT.doodads;
            result.worldModelBatches = parsedADT.worldModelBatches;
            result.renderBatches = renderBatches;
            result.rootADTFileDataID = parsedADT.rootADTFileDataID;
            result.chunkBounds = parsedADT.chunkBounds;
            result.blpFileDataIDs = parsedADT.blpFileDataIDs;

            return result;
        }

        public static void UnloadTerrain(Terrain terrain)
        {
            if (terrain.renderBatches == null)
                return;

            terrain.vertexBuffer.Dispose();
            terrain.indiceBuffer.Dispose();

            foreach (var usedWMO in terrain.worldModelBatches)
                WMOCache.Release(usedWMO.fileDataID, terrain.rootADTFileDataID);

            foreach (var usedM2 in terrain.doodads)
                M2Cache.Release(usedM2.fileDataID, terrain.rootADTFileDataID);

            foreach (var usedBLP in terrain.blpFileDataIDs)
                BLPCache.Release(usedBLP, terrain.rootADTFileDataID);

            foreach (var batch in terrain.renderBatches)
            {
                // cant dispose material/heightmaterials here, they have to be released by blpcache above when therse no more users

                foreach (var alphaMatID in batch.alphaMaterialID)
                    alphaMatID.Dispose();
            }
        }
    }

}
