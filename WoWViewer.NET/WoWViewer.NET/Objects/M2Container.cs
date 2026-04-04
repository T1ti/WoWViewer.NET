using Silk.NET.OpenGL;
using System.Numerics;
using WoWViewer.NET.Raycasting;
using WoWViewer.NET.Renderer;

namespace WoWViewer.NET.Objects
{
    public class M2Container : Container3D
    {
        public bool[] EnabledGeosets { get; }

        public bool forceRender { get; set; } = false;

        private Renderer.Structs.DoodadBatch m2;

        public M2Container(GL gl, uint fileDataID, uint shaderProgram, uint parentFileDataId) : base(gl, fileDataID, shaderProgram, parentFileDataId)
        {
            m2 = Cache.GetOrLoadM2(gl, fileDataID, shaderProgram, parentFileDataId);
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
            var box = new BoundingBox(m2.boundingBox.min, m2.boundingBox.max);

            var modelMatrix = Matrix4x4.CreateScale(Scale);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (Rotation.Y - 270f));
            modelMatrix *= Matrix4x4.CreateTranslation(Position.X, Position.Z * -1, Position.Y);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

            return BoundingBox.Transform(box, modelMatrix);
        }
    }
}
