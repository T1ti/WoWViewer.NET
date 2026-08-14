using System.Numerics;
using System.Runtime.InteropServices;

namespace WoWRenderLib.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    public struct M2PerObjectCB
    {
        public Matrix4x4 projection_matrix;
        public Matrix4x4 view_matrix;
        public Matrix4x4 model_matrix;
        public Matrix4x4 texMatrix1;
        public Matrix4x4 texMatrix2;

        public int vertexShader;
        public int pixelShader;
        public int hasTexMatrix1;
        public int hasTexMatrix2;

        public Vector3 lightDirection;
        public float alphaRef;
        public float blendMode;
        public Vector3 _pad;
        public Vector3 ambientColor;
        public float _pad1;
        public Vector3 diffuseColor;
        public float _pad2;
    }

    public struct ParsedM2
    {
        public uint fileDataID;
        public byte[] vertexBytes;
        public byte[] indiceBytes;
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

    public struct M2Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord1;
        public Vector2 TexCoord2;
    }

    public struct M2Material
    {
        public uint fileDataID;
        public uint blendMode;
        public WoWFormatLib.Structs.M2.TextureFlags flags;
    }

    public readonly struct M2Geoset
    {
        public readonly ushort id { get; init; }
        public readonly ushort level { get; init; }
        public readonly uint firstVertex { get; init; }
        public readonly ushort vertexCount { get; init; }
        public readonly uint firstIndex { get; init; }
        public readonly ushort indexCount { get; init; }
    }

    public readonly struct Submesh
    {
        public readonly uint firstFace { get; init; }
        public readonly uint numFaces { get; init; }
        public readonly uint[] material { get; init; }
        public readonly int[] textureIndices { get; init; }
        public readonly uint blendType { get; init; }
        public readonly ushort renderFlags { get; init; }
        public readonly ushort geosetId { get; init; }
        public readonly int index { get; init; }
        public readonly uint vertexShaderID { get; init; }
        public readonly uint pixelShaderID { get; init; }
    }

    public struct DoodadBatch
    {
        public uint fileDataID;
        public byte[] vertexBuffer;
        public byte[] indiceBuffer;
        public BoundingBox boundingBox;
        public float boundingRadius;
        public Submesh[] submeshes;
        public M2Material[] mats;
    }
}
