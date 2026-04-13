using System.Numerics;

namespace WoWViewer.NET.Structs
{
    public struct Terrain
    {
        public uint rootADTFileDataID;
        public uint vao;
        public uint vertexBuffer;
        public uint indiceBuffer;
        public Vector3 startPos;
        public ADTRenderBatch[] renderBatches;
        public WorldModelBatch[] worldModelBatches;
        public Doodad[] doodads;
        public uint[] blpFileDataIDs;
        public Vector4 heights;
        public Vector4 weights;
        public BoundingBox[] chunkBounds;
    }

    public struct Doodad
    {
        public uint fileDataID;
        public Vector3 position;
        public Vector3 rotation;
        public float scale;
        public DoodadBatch m2Model;
    }


    public struct ADTVertex
    {
        public Vector3 Normal;
        public Vector4 Color;
        public Vector2 TexCoord;
        public Vector3 Position;
    }

    public struct ADTMaterial
    {
        public uint textureID;
        public uint heightTextureID;
        public float scale;
        public float heightScale;
        public float heightOffset;
    }


    public struct ADTRenderBatch
    {
        public int[] materialID;
        public int[] alphaMaterialID;
        public float[] scales;
        public int[] heightMaterialIDs;
        public float[] heightScales;
        public float[] heightOffsets;
    }

}
