using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Loaders
{
    class M2Loader
    {
        public static unsafe ParsedDoodadBatch LoadM2(ComPtr<ID3D11Device> device, ParsedM2 parsedM2)
        {
            var doodadBatch = new ParsedDoodadBatch()
            {
                boundingBox = parsedM2.boundingBox,
                boundingRadius = parsedM2.boundingRadius,
                fileDataID = parsedM2.fileDataID,
                usesLegacyDepthFlags = parsedM2.usesLegacyDepthFlags,
                raycastVertices = ExtractRaycastVertices(parsedM2.vertexBytes),
                raycastIndices = MemoryMarshal.Cast<byte, ushort>(parsedM2.indiceBytes).ToArray(),
                mats = parsedM2.mats,
                geosets = parsedM2.geosets,
                submeshes = parsedM2.submeshes,
                vertexCount = parsedM2.vertexCount,
                indexCount = parsedM2.indexCount,
                animationCount = parsedM2.animationCount,
                particleEmitterCount = parsedM2.particleEmitterCount,
                boneCount = parsedM2.boneCount,
                attachmentCount = parsedM2.attachmentCount,
                animation = parsedM2.animation
            };

            foreach (var mat in doodadBatch.mats)
            {
                if (mat.fileDataID != 0)
                    BLPCache.GetOrLoad(device, mat.fileDataID, parsedM2.fileDataID);
            }

            ComPtr<ID3D11Buffer> vertexBuffer = default;

            if (parsedM2.vertexBytes.Length > 0)
            {
                var bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)parsedM2.vertexBytes.Length,
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.VertexBuffer
                };

                fixed (byte* vertexData = parsedM2.vertexBytes)
                {
                    var subresourceData = new SubresourceData
                    {
                        PSysMem = vertexData
                    };

                    SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref vertexBuffer));
                }
            }

            doodadBatch.vertexBuffer = vertexBuffer;

            ComPtr<ID3D11Buffer> indiceBuffer = default;

            if (parsedM2.indiceBytes.Length > 0)
            {
                var bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)parsedM2.indiceBytes.Length,
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.IndexBuffer
                };

                fixed (byte* indiceData = parsedM2.indiceBytes)
                {
                    var subresourceData = new SubresourceData
                    {
                        PSysMem = indiceData
                    };

                    SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref indiceBuffer));
                }
            }

            doodadBatch.indiceBuffer = indiceBuffer;

            return doodadBatch;
        }

        private static Vector3[] ExtractRaycastVertices(byte[] vertexBytes)
        {
            var source = MemoryMarshal.Cast<byte, M2Vertex>(vertexBytes);
            var positions = new Vector3[source.Length];
            for (var index = 0; index < source.Length; index++)
                positions[index] = source[index].Position;
            return positions;
        }

        public static void UnloadM2(ParsedDoodadBatch model)
        {
            model.vertexBuffer.Dispose();
            model.indiceBuffer.Dispose();

            foreach (var material in model.mats)
            {
                if (material.fileDataID != 0)
                    BLPCache.Release(material.fileDataID, model.fileDataID);
            }
        }
    }
}
