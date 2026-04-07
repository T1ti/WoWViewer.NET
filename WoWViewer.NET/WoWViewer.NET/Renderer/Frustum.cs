using System.Numerics;

namespace WoWViewer.NET.Renderer
{
    public class Frustum
    {
        private readonly Plane[] planes = new Plane[6];

        public enum PlaneIndex
        {
            Left = 0,
            Right = 1,
            Top = 2,
            Bottom = 3,
            Near = 4,
            Far = 5
        }

        public void ExtractFromMatrix(Matrix4x4 viewProjection)
        {
            // Left plane
            planes[(int)PlaneIndex.Left] = Plane.Normalize(new Plane(
                viewProjection.M14 + viewProjection.M11,
                viewProjection.M24 + viewProjection.M21,
                viewProjection.M34 + viewProjection.M31,
                viewProjection.M44 + viewProjection.M41
            ));

            // Right plane
            planes[(int)PlaneIndex.Right] = Plane.Normalize(new Plane(
                viewProjection.M14 - viewProjection.M11,
                viewProjection.M24 - viewProjection.M21,
                viewProjection.M34 - viewProjection.M31,
                viewProjection.M44 - viewProjection.M41
            ));

            // Top plane
            planes[(int)PlaneIndex.Top] = Plane.Normalize(new Plane(
                viewProjection.M14 - viewProjection.M12,
                viewProjection.M24 - viewProjection.M22,
                viewProjection.M34 - viewProjection.M32,
                viewProjection.M44 - viewProjection.M42
            ));

            // Bottom plane
            planes[(int)PlaneIndex.Bottom] = Plane.Normalize(new Plane(
                viewProjection.M14 + viewProjection.M12,
                viewProjection.M24 + viewProjection.M22,
                viewProjection.M34 + viewProjection.M32,
                viewProjection.M44 + viewProjection.M42
            ));

            // Near plane
            planes[(int)PlaneIndex.Near] = Plane.Normalize(new Plane(
                viewProjection.M14 + viewProjection.M13,
                viewProjection.M24 + viewProjection.M23,
                viewProjection.M34 + viewProjection.M33,
                viewProjection.M44 + viewProjection.M43
            ));

            // Far plane
            planes[(int)PlaneIndex.Far] = Plane.Normalize(new Plane(
                viewProjection.M14 - viewProjection.M13,
                viewProjection.M24 - viewProjection.M23,
                viewProjection.M34 - viewProjection.M33,
                viewProjection.M44 - viewProjection.M43
            ));
        }

        public bool IsBoxVisible(Vector3 min, Vector3 max)
        {
            for (int i = 0; i < 6; i++)
            {
                var plane = planes[i];
                
                var positiveVertex = new Vector3(
                    plane.Normal.X >= 0 ? max.X : min.X,
                    plane.Normal.Y >= 0 ? max.Y : min.Y,
                    plane.Normal.Z >= 0 ? max.Z : min.Z
                );

                if (Plane.DotCoordinate(plane, positiveVertex) < 0)
                    return false;
            }

            return true;
        }

        public bool IsSphereVisible(Vector3 center, float radius)
        {
            for (int i = 0; i < 6; i++)
            {
                float distance = Plane.DotCoordinate(planes[i], center);
                if (distance < -radius)
                    return false;
            }
            return true;
        }

        public bool IsPointVisible(Vector3 point)
        {
            for (int i = 0; i < 6; i++)
            {
                if (Plane.DotCoordinate(planes[i], point) < 0)
                    return false;
            }
            return true;
        }
    }
}
