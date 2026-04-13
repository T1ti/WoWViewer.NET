using System.Numerics;

namespace WoWViewer.NET.Structs
{
    public readonly struct MapTile
    {
        public readonly uint wdtFileDataID { get; init; }
        public readonly byte tileX { get; init; }
        public readonly byte tileY { get; init; }
    }

    public readonly struct BoundingBox
    {
        public readonly Vector3 min { get; init; }
        public readonly Vector3 max { get; init; }
    }
}
