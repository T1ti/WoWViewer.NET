using System.Numerics;

namespace WoWViewer.NET
{
    public class Structs
    {
        public struct Terrain
        {
            public uint rootADTFileDataID;
            public uint vao;
            public uint vertexBuffer;
            public uint indiceBuffer;
            public Vector3 startPos;
            public ADTRenderBatch[] renderBatches;
            public Doodad[] doodads;
            public WorldModelBatch[] worldModelBatches;
            public uint[] blpFileDataIDs;
            public Vector4 heights;
            public Vector4 weights;
            public BoundingBox[] chunkBounds;
        }

        public struct ADTVertex
        {
            public Vector3 Normal;
            public Vector4 Color;
            public Vector2 TexCoord;
            public Vector3 Position;
        }

        public struct M2Vertex
        {
            public Vector3 Normal;
            public Vector2 TexCoord;
            public Vector3 Position;
        }

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

        public struct ADTMaterial
        {
            public uint textureID;
            public uint heightTextureID;
            public float scale;
            public float heightScale;
            public float heightOffset;
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

        public struct M2Material
        {
            public uint textureID;
            public uint blendMode;
            internal WoWFormatLib.Structs.M2.TextureFlags flags;
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

        public struct ADTRenderBatch
        {
            public int[] materialID;
            public int[] alphaMaterialID;
            public float[] scales;
            public int[] heightMaterialIDs;
            public float[] heightScales;
            public float[] heightOffsets;
        }

        public struct Doodad
        {
            public uint fileDataID;
            public Vector3 position;
            public Vector3 rotation;
            public float scale;
            public DoodadBatch m2Model;
        }

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

        public readonly struct BoundingBox
        {
            public readonly Vector3 min { get; init; }
            public readonly Vector3 max { get; init; }
        }

        public readonly struct Submesh
        {
            public readonly uint firstFace { get; init; }
            public readonly uint numFaces { get; init; }
            public readonly uint material { get; init; }
            public readonly uint blendType { get; init; }
            public readonly int index { get; init; }
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

        public readonly struct MapTile
        {
            public readonly uint wdtFileDataID { get; init; }
            public readonly byte tileX { get; init; }
            public readonly byte tileY { get; init; }
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
}