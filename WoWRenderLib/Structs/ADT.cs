using System.Numerics;
using System.Runtime.InteropServices;

namespace WoWRenderLib.Structs
{

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ADTPerObjectCB
    {
        public Matrix4x4 model_matrix;
        public Matrix4x4 projection_matrix;
        public Matrix4x4 rotation_matrix;
        public Vector3 firstPos;
        public float _pad0; // pad to 16 byte boundary
    }

    public struct ParsedADT
    {
        public uint rootADTFileDataID;
        public uint vao;
        public byte[] vertexBuffer;
        public byte[] indiceBuffer;
        public Vector3 startPos;
        public ParsedADTRenderBatch[] renderBatches;
        public WorldModelBatch[] worldModelBatches;
        public Doodad[] doodads;
        public uint[] blpFileDataIDs;
        public Vector4 heights;
        public Vector4 weights;
        public BoundingBox[] chunkBounds;
    }

    public struct ParsedADTRenderBatch
    {
        public int[] materialFDIDs;
        public int[] heightMaterialFDIDs;
        public byte[][] alphaMaterials;
        public float[] scales;
        public float[] heightScales;
        public float[] heightOffsets;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct LayerData
    {
        public int layerCount;
        public Vector3 lightDirection;
        public Vector3 ambientColor;
        public float _pad0;
        public Vector3 diffuseColor;
        public float _pad1;
        public Vector4 heightScales0;
        public Vector4 heightScales1;
        public Vector4 heightOffsets0;
        public Vector4 heightOffsets1;
        public Vector4 layerScales0;
        public Vector4 layerScales1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ADTVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public Vector4 Color;
    }

    public struct ADTMaterial
    {
        public int texture;
        public uint textureID; // TODO: OpenGL only, move out
        public int heightTexture;
        public uint heightTextureID; // TODO: OpenGL only, move out
        public float scale;
        public float heightScale;
        public float heightOffset;
    }

    public struct Doodad
    {
        public uint fileDataID;
        public Vector3 position;
        public Vector3 rotation;
        public float scale;
        public DoodadBatch m2Model;
    }
}
