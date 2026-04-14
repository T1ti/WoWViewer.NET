using Silk.NET.OpenGL;
using System.Numerics;
using WoWRenderLib.Cache;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Objects
{
    public class M2Container : Container3D
    {
        public bool[] EnabledGeosets { get; }

        private Structs.DoodadBatch m2;
        public WMOContainer? ParentWMO { get; set; } = null;
        public Vector3 LocalPosition { get; set; }
        public Quaternion LocalRotation { get; set; }
        public float LocalScale { get; set; } = 1.0f;

        public M2Container(GL gl, uint fileDataID, uint shaderProgram, uint parentFileDataId) : base(gl, fileDataID, shaderProgram, parentFileDataId)
        {
            m2 = M2Cache.GetOrLoad(gl, fileDataID, shaderProgram, parentFileDataId);
            EnabledGeosets = new bool[m2.submeshes.Length];
            Array.Fill(EnabledGeosets, true);
        }

        public override BoundingSphere? GetBoundingSphere()
        {
            var modelMatrix = Matrix4x4.CreateScale(Scale);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (Rotation.Y - 270f));
            modelMatrix *= Matrix4x4.CreateTranslation(Position.X, Position.Z * -1, Position.Y);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

            var transformedCenter = Vector3.Transform(Vector3.Zero, modelMatrix);
            return new BoundingSphere(transformedCenter, m2.boundingRadius * Scale);
        }

        public override BoundingBox? GetBoundingBox()
        {
            var box = new BoundingBox(m2.boundingBox.Min, m2.boundingBox.Max);

            Matrix4x4 modelMatrix;

            if (ParentWMO != null)
            {
                // TODO
                modelMatrix = Matrix4x4.CreateScale(Scale);
            }
            else
            {
                modelMatrix = Matrix4x4.CreateScale(Scale);
                modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (Rotation.Y - 270f));
                modelMatrix *= Matrix4x4.CreateTranslation(Position.X, Position.Z * -1, Position.Y);
                modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);
            }

            return BoundingBox.Transform(box, modelMatrix);
        }
    }
}
