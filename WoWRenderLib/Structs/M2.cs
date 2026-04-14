using System.Numerics;

namespace WoWRenderLib.Structs
{
    public struct DoodadBatch
    {
        public uint vao;
        public uint vertexBuffer;
        public uint indiceBuffer;
        public uint[] indices;
        public BoundingBox boundingBox;
        public float boundingRadius;
        public Submesh[] submeshes;
        public M2Material[] mats;
    }

    public struct M2Vertex
    {
        public Vector3 Normal;
        public Vector2 TexCoord1;
        public Vector2 TexCoord2;
        public Vector3 Position;
    }

    public struct M2Material
    {
        public uint textureID;
        public uint blendMode;
        internal WoWFormatLib.Structs.M2.TextureFlags flags;
    }

    public readonly struct Submesh
    {
        public readonly uint firstFace { get; init; }
        public readonly uint numFaces { get; init; }
        public readonly uint material { get; init; }
        public readonly uint blendType { get; init; }
        public readonly int index { get; init; }
        public readonly uint vertexShaderID { get; init; }
        public readonly uint pixelShaderID { get; init; }
    }
}
