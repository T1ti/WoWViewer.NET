using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using System.Runtime.InteropServices;

namespace WoWRenderLib.DX11.Structs
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
    }

    public struct DoodadBatch
    {
        public uint fileDataID;
        public ComPtr<ID3D11Buffer> vertexBuffer;
        public ComPtr<ID3D11Buffer> indiceBuffer;
        public uint[] indices;
        public BoundingBox boundingBox;
        public float boundingRadius;
        public Submesh[] submeshes;
        public M2Material[] mats;
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
        public uint textureID;
        public uint blendMode;
        internal WoWFormatLib.Structs.M2.TextureFlags flags;
    }

    public readonly struct Submesh
    {
        public readonly uint firstFace { get; init; }
        public readonly uint numFaces { get; init; }
        public readonly uint[] material { get; init; }
        public readonly uint blendType { get; init; }
        public readonly int index { get; init; }
        public readonly uint vertexShaderID { get; init; }
        public readonly uint pixelShaderID { get; init; }
    }
}
