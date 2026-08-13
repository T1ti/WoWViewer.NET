using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Numerics;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Loaders
{
    class ADTLoader
    {
        public static unsafe Terrain LoadADT(ComPtr<ID3D11Device> device, ParsedADT parsedADT)
        {
            Terrain result = new();

            var renderBatches = new ADTRenderBatch[256];

            for (var c = 0; c < parsedADT.renderBatches.Length; c++)
            {
                var renderBatch = parsedADT.renderBatches[c];
                var batch = new ADTRenderBatch();
                batch.layerCount = Math.Clamp(
                    Array.FindLastIndex(renderBatch.materialFDIDs, fileDataId => fileDataId > 0) + 1,
                    1,
                    8);
                batch.usesHeightTextures =
                    renderBatch.heightScales.Any(scale => MathF.Abs(scale) > 0.000001f) ||
                    renderBatch.heightOffsets.Any(offset => MathF.Abs(offset - 1f) > 0.000001f);
                batch.heightScales = renderBatch.heightScales;
                batch.heightOffsets = renderBatch.heightOffsets;
                batch.materialFDIDs = renderBatch.materialFDIDs;
                batch.heightMaterialFDIDs = renderBatch.heightMaterialFDIDs;
                batch.scales = renderBatch.scales;
                renderBatches[c] = batch;
            }

            (result.alphaMaterialArray, result.alphaSliceBuffer) =
                CreateAlphaMaterials(device, parsedADT.renderBatches);
            result.chunkLayerDataBuffer = CreateChunkLayerDataBuffer(device, renderBatches);

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

            bufferDesc.ByteWidth = (uint)parsedADT.farLodIndiceBuffer.Length;
            fixed (byte* indexData = parsedADT.farLodIndiceBuffer)
            {
                var subresourceData = new SubresourceData
                {
                    PSysMem = indexData
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref result.farLodIndiceBuffer));
            }

            foreach (var usedBLP in parsedADT.blpFileDataIDs)
                BLPCache.GetOrLoad(device, usedBLP, parsedADT.rootADTFileDataID);

            result.doodads = parsedADT.doodads;
            result.worldModelBatches = parsedADT.worldModelBatches;
            result.renderBatches = renderBatches;
            result.rootADTFileDataID = parsedADT.rootADTFileDataID;
            result.chunkBounds = parsedADT.chunkBounds;
            result.chunkBoundingSpheres = CreateChunkBoundingSpheres(parsedADT.chunkBounds);
            (result.terrainBounds, result.terrainBoundingSphere) =
                CreateTerrainBounds(parsedADT.chunkBounds);
            result.blpFileDataIDs = parsedADT.blpFileDataIDs;

            return result;
        }

        private static BoundingSphere[] CreateChunkBoundingSpheres(BoundingBox[] bounds)
        {
            var spheres = new BoundingSphere[bounds.Length];
            for (var index = 0; index < bounds.Length; index++)
            {
                var center = (bounds[index].Min + bounds[index].Max) * 0.5f;
                spheres[index] = new BoundingSphere(
                    center,
                    Vector3.Distance(center, bounds[index].Max));
            }

            return spheres;
        }

        private static (BoundingBox Bounds, BoundingSphere Sphere) CreateTerrainBounds(
            BoundingBox[] chunkBounds)
        {
            if (chunkBounds.Length == 0)
                return (new BoundingBox(Vector3.Zero, Vector3.Zero), new BoundingSphere(Vector3.Zero, 0f));

            var min = chunkBounds[0].Min;
            var max = chunkBounds[0].Max;
            for (var index = 1; index < chunkBounds.Length; index++)
            {
                min = Vector3.Min(min, chunkBounds[index].Min);
                max = Vector3.Max(max, chunkBounds[index].Max);
            }

            var center = (min + max) * 0.5f;
            return (
                new BoundingBox(min, max),
                new BoundingSphere(center, Vector3.Distance(center, max)));
        }

        public static void UnloadTerrain(Terrain terrain)
        {
            if (terrain.renderBatches == null)
                return;

            terrain.vertexBuffer.Dispose();
            terrain.indiceBuffer.Dispose();
            terrain.farLodIndiceBuffer.Dispose();
            terrain.alphaSliceBuffer.Dispose();
            terrain.chunkLayerDataBuffer.Dispose();
            terrain.alphaMaterialArray.Dispose();

            foreach (var usedWMO in terrain.worldModelBatches)
                WMOCache.Release(usedWMO.fileDataID, terrain.rootADTFileDataID);

            foreach (var usedM2 in terrain.doodads)
                M2Cache.Release(usedM2.fileDataID, terrain.rootADTFileDataID);

            foreach (var usedBLP in terrain.blpFileDataIDs)
                BLPCache.Release(usedBLP, terrain.rootADTFileDataID);

            // Diffuse and height textures are released through BLPCache above.
        }

        private static unsafe (
            ComPtr<ID3D11ShaderResourceView> MaterialArray,
            ComPtr<ID3D11Buffer> SliceBuffer) CreateAlphaMaterials(
            ComPtr<ID3D11Device> device,
            ParsedADTRenderBatch[] renderBatches)
        {
            const int width = 64;
            const int height = 64;
            const int bytesPerPixel = 4;
            const int layersPerChunk = 2;
            const int bytesPerSlice = width * height * bytesPerPixel;

            var slices = new List<byte[]> { new byte[bytesPerSlice] };
            var sliceIndices = new uint[256 * layersPerChunk];
            for (var chunkIndex = 0; chunkIndex < renderBatches.Length; chunkIndex++)
            {
                var alphaMaterials = renderBatches[chunkIndex].alphaMaterials;
                for (var layerIndex = 0; layerIndex < Math.Min(alphaMaterials.Length, layersPerChunk); layerIndex++)
                {
                    var source = alphaMaterials[layerIndex];
                    if (source == null)
                        continue;

                    sliceIndices[chunkIndex * layersPerChunk + layerIndex] = (uint)slices.Count;
                    slices.Add(source);
                }
            }

            var pixels = new byte[slices.Count * bytesPerSlice];
            for (var sliceIndex = 1; sliceIndex < slices.Count; sliceIndex++)
            {
                var source = slices[sliceIndex];
                source.AsSpan(0, Math.Min(source.Length, bytesPerSlice))
                    .CopyTo(pixels.AsSpan(sliceIndex * bytesPerSlice, bytesPerSlice));
            }

            var textureDesc = new Texture2DDesc
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = (uint)slices.Count,
                Format = Format.FormatR8G8B8A8Unorm,
                SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.ShaderResource,
                CPUAccessFlags = 0,
                MiscFlags = 0
            };

            fixed (byte* pixelData = pixels)
            {
                var initialData = stackalloc SubresourceData[slices.Count];
                for (var sliceIndex = 0; sliceIndex < slices.Count; sliceIndex++)
                {
                    initialData[sliceIndex] = new SubresourceData
                    {
                        PSysMem = pixelData + sliceIndex * bytesPerSlice,
                        SysMemPitch = width * bytesPerPixel
                    };
                }

                ComPtr<ID3D11Texture2D> texture = default;
                SilkMarshal.ThrowHResult(device.CreateTexture2D(
                    in textureDesc,
                    ref initialData[0],
                    ref texture));

                var viewDesc = new ShaderResourceViewDesc
                {
                    Format = textureDesc.Format,
                    ViewDimension = D3DSrvDimension.D3D101SrvDimensionTexture2Darray,
                    Texture2DArray = new Tex2DArraySrv
                    {
                        MostDetailedMip = 0,
                        MipLevels = 1,
                        FirstArraySlice = 0,
                        ArraySize = (uint)slices.Count
                    }
                };

                ComPtr<ID3D11ShaderResourceView> view = default;
                SilkMarshal.ThrowHResult(device.CreateShaderResourceView(texture, in viewDesc, ref view));
                texture.Dispose();

                var sliceBufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)(sliceIndices.Length * sizeof(uint)),
                    Usage = Usage.Immutable,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };
                fixed (uint* sliceData = sliceIndices)
                {
                    var sliceInitialData = new SubresourceData { PSysMem = sliceData };
                    ComPtr<ID3D11Buffer> sliceBuffer = default;
                    SilkMarshal.ThrowHResult(device.CreateBuffer(
                        in sliceBufferDesc,
                        in sliceInitialData,
                        ref sliceBuffer));
                    return (view, sliceBuffer);
                }
            }
        }

        private static unsafe ComPtr<ID3D11Buffer> CreateChunkLayerDataBuffer(
            ComPtr<ID3D11Device> device,
            ADTRenderBatch[] renderBatches)
        {
            var data = new ADTChunkLayerData[renderBatches.Length];
            for (var index = 0; index < renderBatches.Length; index++)
            {
                var batch = renderBatches[index];
                data[index] = new ADTChunkLayerData
                {
                    heightScales0 = ReadVector(batch.heightScales, 0, Vector4.Zero),
                    heightScales1 = ReadVector(batch.heightScales, 4, Vector4.Zero),
                    heightOffsets0 = ReadVector(batch.heightOffsets, 0, Vector4.One),
                    heightOffsets1 = ReadVector(batch.heightOffsets, 4, Vector4.One),
                    layerScales0 = ReadVector(batch.scales, 0, Vector4.One),
                    layerScales1 = ReadVector(batch.scales, 4, Vector4.One)
                };
            }

            var bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(data.Length * sizeof(ADTChunkLayerData)),
                Usage = Usage.Immutable,
                BindFlags = (uint)BindFlag.ConstantBuffer
            };
            fixed (ADTChunkLayerData* dataPointer = data)
            {
                var initialData = new SubresourceData { PSysMem = dataPointer };
                ComPtr<ID3D11Buffer> buffer = default;
                SilkMarshal.ThrowHResult(device.CreateBuffer(
                    in bufferDesc,
                    in initialData,
                    ref buffer));
                return buffer;
            }
        }

        private static Vector4 ReadVector(float[]? values, int start, Vector4 fallback) =>
            values is { Length: >= 8 }
                ? new Vector4(values[start], values[start + 1], values[start + 2], values[start + 3])
                : fallback;
    }

}
