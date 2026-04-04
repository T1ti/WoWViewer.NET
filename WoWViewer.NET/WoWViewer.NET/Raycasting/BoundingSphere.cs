using System.Numerics;

namespace WoWViewer.NET.Raycasting
{
    public struct BoundingSphere
    {
        public Vector3 Center { get; set; }
        public float Radius { get; set; }

        public BoundingSphere(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public static BoundingSphere Transform(BoundingSphere sphere, Vector3 position, float scale)
        {
            return new BoundingSphere(sphere.Center + position, sphere.Radius * scale);
        }
    }
}
