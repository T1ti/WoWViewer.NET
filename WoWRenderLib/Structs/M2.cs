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
        public int fogMode;
        public int doodadMaterialLit;
        public float _pad;
        public Vector3 ambientColor;
        public float globalOpacity;
        public Vector3 diffuseColor;
        public int hasSkinning;
        public Vector4 materialColor;
    }

    public struct ParsedM2
    {
        public uint fileDataID;
        public bool usesLegacyDepthFlags;
        public byte[] vertexBytes;
        public byte[] indiceBytes;
        public BoundingBox boundingBox;
        public float boundingRadius;
        public Wrath335M2Bounds? wrath335Bounds;
        public Submesh[] submeshes;
        public M2Material[] mats;
        public M2Geoset[] geosets;
        public int vertexCount;
        public int indexCount;
        public int animationCount;
        public int particleEmitterCount;
        public int boneCount;
        public int attachmentCount;
        public M2Animation? animation;
    }

    public struct M2Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord1;
        public Vector2 TexCoord2;
        public uint BoneWeights;
        public uint BoneIndices;
    }

    public struct M2Material
    {
        public uint fileDataID;
        public uint blendMode;
        public uint flags;
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

    /// <summary>Authored 12340 batch/section inputs for native element ordering.</summary>
    public readonly record struct Wrath335M2Bounds(Vector3 Minimum, Vector3 Maximum, float Radius);

    public readonly record struct Wrath335M2MeshSortMetadata(
        byte BatchFlags,
        sbyte PriorityPlane,
        ushort MaterialLayer,
        ushort CenterBoneIndex,
        ushort SectionBoneComboIndex,
        Vector3 SortCenter,
        float SortRadius,
        bool RawDistance)
    {
        public ushort BoneInfluences { get; init; }
        public ushort ShaderId { get; init; }
    }

    public readonly struct Submesh
    {
        public readonly uint firstFace { get; init; }
        public readonly uint numFaces { get; init; }
        public readonly uint[] material { get; init; }
        public readonly int[] textureIndices { get; init; }
        public readonly uint[] textureFlags { get; init; }
        public readonly uint blendType { get; init; }
        // 12340 queues a material layer using materialIndex - materialLayer.
        // Null keeps callers without decoded layer metadata on their existing rule.
        public readonly uint? baseBlendType { get; init; }
        // Only the WotLK MPQ loader publishes these client-specific sort inputs.
        public readonly Wrath335M2MeshSortMetadata? wrath335Sort { get; init; }
        public readonly ushort renderFlags { get; init; }
        public readonly ushort geosetId { get; init; }
        public readonly int index { get; init; }
        public readonly uint vertexShaderID { get; init; }
        public readonly uint pixelShaderID { get; init; }
        public readonly int colorIndex { get; init; }
        public readonly int textureWeightIndex { get; init; }
        public readonly int textureTransformIndex1 { get; init; }
        public readonly int textureTransformIndex2 { get; init; }
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
