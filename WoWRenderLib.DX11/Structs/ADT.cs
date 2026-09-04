using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using System.Runtime.InteropServices;
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
        public int[] compatibleRenderRunLengths;
        public WorldModelBatch[] worldModelBatches;
        public Doodad[] doodads;
        public uint[] blpFileDataIDs;
        public Vector4 heights;
        public Vector4 weights;
        public BoundingBox[] chunkBounds;
        // CPU-side terrain data is retained for editor raycasts and brush
        // edits. The GPU buffer holds a compact projection of these vertices.
        public ADTVertex[] vertices;
        public int[] indices;
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

    // CPU terrain vertices retain their full position for editing, culling,
    // and raycasts. The GPU receives only the dynamic attributes; the regular
    // X/Y terrain layout is reconstructed from the vertex and chunk IDs.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ADTGpuVertex
    {
        public float Height;
        public Vector3 Normal;
        public Vector4 Color;

        public static ADTGpuVertex FromCpu(ADTVertex vertex) => new()
        {
            Height = vertex.Position.Z,
            Normal = vertex.Normal,
            Color = vertex.Color
        };
    }
}
