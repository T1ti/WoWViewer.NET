using System.Numerics;

namespace WoWRenderLib.Raycasting
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

        public static BoundingSphere Transform(BoundingSphere sphere, Matrix4x4 transform)
        {
            var scaleX = new Vector3(transform.M11, transform.M12, transform.M13).Length();
            var scaleY = new Vector3(transform.M21, transform.M22, transform.M23).Length();
            var scaleZ = new Vector3(transform.M31, transform.M32, transform.M33).Length();
            var maximumScale = MathF.Max(scaleX, MathF.Max(scaleY, scaleZ));

            return new BoundingSphere(
                Vector3.Transform(sphere.Center, transform),
                sphere.Radius * maximumScale);
        }
    }
}
