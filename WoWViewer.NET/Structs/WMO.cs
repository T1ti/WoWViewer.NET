using System.Numerics;
using WoWViewer.NET.Renderer;

namespace WoWViewer.NET.Structs
{
    public struct WMOVertex
    {
        public Vector3 Normal;
        public Vector2 TexCoord;
        public Vector2 TexCoord2;
        public Vector2 TexCoord3;
        public Vector2 TexCoord4;
        public Vector3 Position;
        public Vector4 Color;
        public Vector4 Color2;
        public Vector4 Color3;
    }

    public  struct WMOMaterial
    {
        public int textureID1;
        public int textureID2;
        public int textureID3;
        public int textureID4;
        public int textureID5;
        public int textureID6;
        public int textureID7;
        public int textureID8;
        public int textureID9;
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
        public int[] materialID;
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

    public struct WorldModel
    {
        public uint rootWMOFileDataID;
        public WorldModelGroupBatches[] groupBatches;
        public WMOMaterial[] mats;
        public WMORenderBatch[] wmoRenderBatch;
        public WMODoodad[] doodads;
        public string[] doodadSets;
        public BoundingBox boundingBox;
        public float boundingRadius;
    }

    public readonly struct PreppedWMO
    {
        public readonly uint FileDataID { get; init; }
        public readonly BoundingBox BoundingBox { get; init; }
        public readonly WMODoodad[] Doodads { get; init; }
        public readonly string[] DoodadSets { get; init; }
        public readonly PreppedWMOMaterial[] Materials { get; init; }
        public readonly PreppedWMOGroup[] PreppedWMOGroups { get; init; }
    }

    public readonly struct PreppedWMOGroup
    {
        public readonly string groupName { get; init; }
        public readonly BoundingBox boundingBox { get; init; }
        public readonly float boundingRadius { get; init; }
        public readonly byte[] vertexBuffer { get; init; }
        public readonly byte[] indiceBuffer { get; init; }
        public readonly PreppedWMOGroupBatch[] groupBatches { get; init; }

    }

    public readonly struct PreppedWMOGroupBatch
    {
        public readonly int MaterialID { get; init; }
        public readonly uint FirstFace { get; init;  }
        public readonly int NumFaces { get; init; }

    }

    public readonly struct WorldModelGroupBatches
    {
        public readonly uint vao { get; init; }
        public readonly uint vertexBuffer { get; init; }
        public readonly uint indiceBuffer { get; init; }
        public readonly uint verticeCount { get; init; }
        public readonly string groupName { get; init; }
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
