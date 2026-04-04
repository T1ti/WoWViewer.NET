using System.Numerics;

namespace WoWViewer.NET.Raycasting
{
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
