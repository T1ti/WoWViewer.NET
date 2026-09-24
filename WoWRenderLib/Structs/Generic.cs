using System.Numerics;

namespace WoWRenderLib.Structs
{
    public readonly struct MapTile : IEquatable<MapTile>
    {
        public MapTile() { }

        // MapId and the compact position are the logical tile identity. The
        // path and FileDataID below are optional file-open hints only; neither
        // exists consistently across all supported client generations.
        public int MapId { get; init; } = -1;
        public string WdtPath { get; init; } = string.Empty;
        public uint WdtFileDataId { get; init; }
        public byte TileX { get; init; }
        public byte TileY { get; init; }

        // WDT stores the 64 rows for each column contiguously: row + column*64.
        // In the renderer's coordinates TileX is the row and TileY is the column.
        public int PositionIndex => GetPositionIndex(TileX, TileY);

        public static int GetPositionIndex(byte tileX, byte tileY) => tileX + (tileY * 64);

        public bool Equals(MapTile other)
        {
            return MapId == other.MapId && PositionIndex == other.PositionIndex;
        }

        public override bool Equals(object? obj) => obj is MapTile other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(MapId, PositionIndex);

        public static bool operator ==(MapTile left, MapTile right) => left.Equals(right);
        public static bool operator !=(MapTile left, MapTile right) => !left.Equals(right);
    }

    public struct BoundingBox
    {
        public Vector3 Min { get; set; }
        public Vector3 Max { get; set; }

        public BoundingBox(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public Vector3 Center => (Min + Max) * 0.5f;

        public Vector3 Size => Max - Min;

        public static BoundingBox Transform(BoundingBox box, Matrix4x4 transform)
        {
            var corners = new Vector3[8]
            {
                new Vector3(box.Min.X, box.Min.Y, box.Min.Z),
                new Vector3(box.Min.X, box.Min.Y, box.Max.Z),
                new Vector3(box.Min.X, box.Max.Y, box.Min.Z),
                new Vector3(box.Min.X, box.Max.Y, box.Max.Z),
                new Vector3(box.Max.X, box.Min.Y, box.Min.Z),
                new Vector3(box.Max.X, box.Min.Y, box.Max.Z),
                new Vector3(box.Max.X, box.Max.Y, box.Min.Z),
                new Vector3(box.Max.X, box.Max.Y, box.Max.Z)
            };

            var transformedMin = new Vector3(float.MaxValue);
            var transformedMax = new Vector3(float.MinValue);

            for (int i = 0; i < 8; i++)
            {
                var transformed = Vector3.Transform(corners[i], transform);
                transformedMin = Vector3.Min(transformedMin, transformed);
                transformedMax = Vector3.Max(transformedMax, transformed);
            }

            return new BoundingBox(transformedMin, transformedMax);
        }
    }
}
