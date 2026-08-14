using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
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
        public readonly string mogiGroupName { get; init; }
        public readonly int sourceGroupIndex { get; init; }
        public readonly uint groupID { get; init; }
        public readonly BoundingBox boundingBox { get; init; }
        public readonly uint flags { get; init; }
        public readonly uint mogiFlags { get; init; }
        public readonly WmoPortalLink[] portalLinks { get; init; }
        public readonly ushort[] doodadReferences { get; init; }
    }

    public readonly struct WmoPortal
    {
        public readonly Vector3[] Vertices { get; init; }
        public readonly Vector3 Normal { get; init; }
        public readonly float Distance { get; init; }
        public readonly BoundingBox Bounds { get; init; }
    }

    public readonly struct WmoPortalLink
    {
        public readonly ushort PortalIndex { get; init; }
        public readonly ushort TargetGroupIndex { get; init; }
        public readonly short Side { get; init; }
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
        public WmoPortal[] portals;
        public bool portalGraphValid;
        public bool[] doodadsReferencedByGroups;
    }
}
