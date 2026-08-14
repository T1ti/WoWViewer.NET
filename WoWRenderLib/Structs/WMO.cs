using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.Renderer;

namespace WoWRenderLib.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WMOPerObjectCB
    {
        public Matrix4x4 projection_matrix;
        public Matrix4x4 view_matrix;
        public Matrix4x4 model_matrix;
        public int vertexShader;
        public int pixelShader;
        public Vector2 _pad0;
        public Vector3 lightDirection;
        public float alphaRef;
        public Vector3 ambientColor;
        public float _pad1;
        public Vector3 diffuseColor;
        public float _pad2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WMOVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public Vector2 TexCoord2;
        public Vector2 TexCoord3;
        public Vector2 TexCoord4;
        public Vector4 Color;
        public Vector4 Color2;
        public Vector4 Color3;
    }

    public readonly struct PreppedWMOMaterial
    {
        public readonly int Shader { get; init; }
        public readonly ShaderEnums.WMOVertexShader VertexShader { get; init; }
        public readonly ShaderEnums.WMOPixelShader PixelShader { get; init; }
        public readonly uint BlendMode { get; init; }
        public readonly uint TexFileDataID0 { get; init; }
        public readonly uint TexFileDataID1 { get; init; }
        public readonly uint TexFileDataID2 { get; init; }
        public readonly uint TexFileDataID3 { get; init; }
        public readonly uint TexFileDataID4 { get; init; }
        public readonly uint TexFileDataID5 { get; init; }
        public readonly uint TexFileDataID6 { get; init; }
        public readonly uint TexFileDataID7 { get; init; }
        public readonly uint TexFileDataID8 { get; init; }
    }

    public struct WMORenderBatch
    {
        public uint[] materialFDIDs;
        public int[] materialID; // TODO: OpenGL only, move out.
        public uint firstFace;
        public uint numFaces;
        public uint groupID;
        public uint blendType;
        public uint shader;
    }


    public readonly struct WorldModelBatch
    {
        public readonly Vector3 position { get; init; }
        public readonly Vector3 rotation { get; init; }
        public readonly float scale { get; init; }
        public readonly uint fileDataID { get; init; }
        public readonly uint uniqueID { get; init; }
        public readonly uint[] doodadSetIDs { get; init; }
    }

    public struct WMODoodad
    {
        public string filename;
        public uint filedataid;
        public short flags;
        public Vector3 position;
        public Quaternion rotation;
        public float scale;
        public Vector4 color;
        public uint doodadSet;
    }

    public readonly struct PreppedWMO
    {
        public readonly uint FileDataID { get; init; }
        public readonly BoundingBox BoundingBox { get; init; }
        public readonly WMODoodad[] Doodads { get; init; }
        public readonly string[] DoodadSets { get; init; }
        public readonly PreppedWMOMaterial[] Materials { get; init; }
        public readonly PreppedWMOGroup[] PreppedWMOGroups { get; init; }
        public readonly Vector3[] PortalVertices { get; init; }
        public readonly PreppedWMOPortal[] Portals { get; init; }
        public readonly PreppedWMOPortalReference[] PortalReferences { get; init; }
        public readonly int SourceGroupCount { get; init; }
    }

    public readonly struct PreppedWMOPortal
    {
        public readonly ushort StartVertex { get; init; }
        public readonly ushort VertexCount { get; init; }
        public readonly Vector3 Normal { get; init; }
        public readonly float Distance { get; init; }
    }

    public readonly struct PreppedWMOPortalReference
    {
        public readonly ushort PortalIndex { get; init; }
        public readonly ushort GroupIndex { get; init; }
        public readonly short Side { get; init; }
    }

    public readonly struct PreppedWMOGroup
    {
        public readonly string groupName { get; init; }
        public readonly string mogiGroupName { get; init; }
        public readonly BoundingBox boundingBox { get; init; }
        public readonly float boundingRadius { get; init; }
        public readonly byte[] vertexBuffer { get; init; }
        public readonly byte[] indiceBuffer { get; init; }
        public readonly PreppedWMOGroupBatch[] groupBatches { get; init; }
        public readonly int sourceGroupIndex { get; init; }
        public readonly uint groupID { get; init; }
        public readonly uint flags { get; init; }
        public readonly uint mogiFlags { get; init; }
        public readonly ushort portalStart { get; init; }
        public readonly ushort portalCount { get; init; }
        public readonly ushort[] doodadReferences { get; init; }

    }

    public readonly struct PreppedWMOGroupBatch
    {
        public readonly int MaterialID { get; init; }
        public readonly uint FirstFace { get; init; }
        public readonly int NumFaces { get; init; }

    }

    public struct WMOGroup
    {
        public string name;
        public uint verticeOffset;
        public WMOVertex[] vertices;
        public uint[] indices;
        public WMORenderBatch[] renderBatches;
    }
}
