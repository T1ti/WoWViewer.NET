using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Structs
{
    public struct Terrain
    {
        public uint rootADTFileDataID;
        public uint vao;
        public ComPtr<ID3D11Buffer> vertexBuffer;
        public ComPtr<ID3D11Buffer> indiceBuffer;
        public ComPtr<ID3D11Buffer> farLodIndiceBuffer;
        public ComPtr<ID3D11Buffer> alphaSliceBuffer;
        public ComPtr<ID3D11Buffer> chunkLayerDataBuffer;
        public ComPtr<ID3D11ShaderResourceView> alphaMaterialArray;
        public Vector3 startPos;
        public ADTRenderBatch[] renderBatches;
        public WorldModelBatch[] worldModelBatches;
        public Doodad[] doodads;
        public uint[] blpFileDataIDs;
        public Vector4 heights;
        public Vector4 weights;
        public BoundingBox[] chunkBounds;
        public BoundingSphere[] chunkBoundingSpheres;
        public BoundingBox terrainBounds;
        public BoundingSphere terrainBoundingSphere;
    }

    public struct ADTRenderBatch
    {
        public int layerCount;
        public bool usesHeightTextures;
        public int[] materialFDIDs;
        public int[] heightMaterialFDIDs;
        public float[] scales;
        public float[] heightScales;
        public float[] heightOffsets;
    }

    public struct ADTChunkLayerData
    {
        public Vector4 heightScales0;
        public Vector4 heightScales1;
        public Vector4 heightOffsets0;
        public Vector4 heightOffsets1;
        public Vector4 layerScales0;
        public Vector4 layerScales1;
    }
}
