using System.Numerics;

namespace WoWViewer.NET.Raycasting
{
    // additional yoink
    public static class IntersectionTests
    {
        public static bool RayIntersectsSphere(Ray ray, BoundingSphere sphere, out float distance)
        {
            distance = 0;

            var m = ray.Origin - sphere.Center;
            var b = Vector3.Dot(m, ray.Direction);
            var c = Vector3.Dot(m, m) - sphere.Radius * sphere.Radius;

            if (c > 0.0f && b > 0.0f)
                return false;

            var discriminant = b * b - c;

            if (discriminant < 0.0f)
                return false;

            distance = -b - MathF.Sqrt(discriminant);

            if (distance < 0.0f)
                distance = 0.0f;

            return true;
        }

        public static bool RayIntersectsBox(Ray ray, BoundingBox box, out float distance)
        {
            distance = 0;
            float tMin = 0.0f;
            float tMax = float.MaxValue;

            for (int i = 0; i < 3; i++)
            {
                float origin = i == 0 ? ray.Origin.X : i == 1 ? ray.Origin.Y : ray.Origin.Z;
                float direction = i == 0 ? ray.Direction.X : i == 1 ? ray.Direction.Y : ray.Direction.Z;
                float min = i == 0 ? box.Min.X : i == 1 ? box.Min.Y : box.Min.Z;
                float max = i == 0 ? box.Max.X : i == 1 ? box.Max.Y : box.Max.Z;

                if (MathF.Abs(direction) < 1e-6f)
                {
                    if (origin < min || origin > max)
                        return false;
                }
                else
                {
                    float t1 = (min - origin) / direction;
                    float t2 = (max - origin) / direction;

                    if (t1 > t2)
                    {
                        (t1, t2) = (t2, t1);
                    }

                    tMin = MathF.Max(tMin, t1);
                    tMax = MathF.Min(tMax, t2);

                    if (tMin > tMax)
                        return false;
                }
            }

            distance = tMin;
            return true;
        }
    }
}
