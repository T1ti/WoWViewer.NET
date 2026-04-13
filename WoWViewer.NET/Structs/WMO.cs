using System.Numerics;

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

    public struct WMOMaterial
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
        internal int texture1;
        internal int texture2;
        internal int texture3;
        internal int texture4;
        internal int texture5;
        internal int texture6;
        internal int texture7;
        internal int texture8;
        internal int texture9;
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
        public Vector3[] boundingBox;
        public float boundingRadius;
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
