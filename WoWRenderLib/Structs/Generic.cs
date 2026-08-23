using System.Numerics;

namespace WoWRenderLib.Structs
{
    public readonly struct MapTile : IEquatable<MapTile>
    {
        public readonly uint wdtFileDataID { get; init; }
        public readonly byte tileX { get; init; }
        public readonly byte tileY { get; init; }

        public bool Equals(MapTile other)
        {
            return tileX == other.tileX && tileY == other.tileY && wdtFileDataID == other.wdtFileDataID;
        }

        public override bool Equals(object? obj) => obj is MapTile other && Equals(other);
        // MapTile is used as a high-frequency key by the scene tile queues,
        // sets, and bounds cache. HashCode.Combine is deliberately general
        // purpose but does considerably more work than this fixed-width key
        // requires. Keep the WDT id mixed with both coordinates while using
        // a small, allocation-free hash suitable for these internal keys.
        public override int GetHashCode() =>
            unchecked((int)((wdtFileDataID * 397u) ^ ((uint)tileX << 8) ^ tileY));

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
