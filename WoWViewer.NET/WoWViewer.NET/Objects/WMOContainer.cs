using Silk.NET.OpenGL;
using System.Numerics;
using WoWViewer.NET.Raycasting;
using WoWViewer.NET.Renderer;

namespace WoWViewer.NET.Objects
{
    public class WMOContainer : Container3D
    {
        public bool[] EnabledGroups { get; }
        public bool[] EnabledDoodadSets { get; }

        public uint UniqueID;
        private Renderer.Structs.WorldModel wmo;
        public WMOContainer(GL gl, uint fileDataID, uint shaderProgram, uint parentFileDataId) : base(gl, fileDataID, shaderProgram, parentFileDataId)
        {
            wmo = Cache.GetOrLoadWMO(gl, fileDataID, shaderProgram, parentFileDataId);

            EnabledGroups = new bool[wmo.groupBatches.Length];
            Array.Fill(EnabledGroups, true);

            EnabledDoodadSets = new bool[wmo.doodadSets.Length];
            Array.Fill(EnabledDoodadSets, true);
        }

        public override BoundingSphere? GetBoundingSphere()
        {
            var modelMatrix = Matrix4x4.CreateScale(Scale);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (Rotation.Y - 270f));
            modelMatrix *= Matrix4x4.CreateTranslation(Position.X, Position.Z * -1, Position.Y);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

            var transformedCenter = Vector3.Transform(Vector3.Zero, modelMatrix);

            return new BoundingSphere(transformedCenter, wmo.boundingRadius * Scale);
        }

        public override BoundingBox? GetBoundingBox()
        {
            if (wmo.boundingBox == null || wmo.boundingBox.Length < 2)
                return null;

            var box = new BoundingBox(wmo.boundingBox[0], wmo.boundingBox[1]);

            var modelMatrix = Matrix4x4.CreateScale(Scale);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (Rotation.Y - 270f));
            modelMatrix *= Matrix4x4.CreateTranslation(Position.X, Position.Z * -1, Position.Y);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

            return BoundingBox.Transform(box, modelMatrix);
        }
    }
}
