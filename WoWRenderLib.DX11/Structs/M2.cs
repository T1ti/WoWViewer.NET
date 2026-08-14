using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Structs
{
    public struct ParsedDoodadBatch
    {
        public uint fileDataID;
        public ComPtr<ID3D11Buffer> vertexBuffer;
        public ComPtr<ID3D11Buffer> indiceBuffer;
        public BoundingBox boundingBox;
        public float boundingRadius;
        public Submesh[] submeshes;
        public M2Material[] mats;
        public M2Geoset[] geosets;
        public int vertexCount;
        public int indexCount;
        public int animationCount;
        public int particleEmitterCount;
        public int boneCount;
        public int attachmentCount;
    }
}
