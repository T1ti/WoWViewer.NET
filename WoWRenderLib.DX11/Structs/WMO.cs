using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Structs
{
    public readonly struct WorldModelGroupBatches
    {
        public readonly uint vao { get; init; }
        public readonly ComPtr<ID3D11Buffer> vertexBuffer { get; init; }
        public readonly ComPtr<ID3D11Buffer> indiceBuffer { get; init; }
        public readonly uint verticeCount { get; init; }
        public readonly string groupName { get; init; }
    }

    public struct WorldModel
    {
        public uint rootWMOFileDataID;
        public WorldModelGroupBatches[] groupBatches;
        public PreppedWMOMaterial[] preppedMats;
        public WMORenderBatch[] wmoRenderBatches;
        public WMODoodad[] doodads;
        public string[] doodadSets;
        public BoundingBox boundingBox;
        public float boundingRadius;
    }
}
