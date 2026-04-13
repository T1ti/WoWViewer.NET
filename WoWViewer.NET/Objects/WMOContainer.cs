using Silk.NET.OpenGL;
using System.Numerics;
using WoWViewer.NET.Cache;
using WoWViewer.NET.Raycasting;
using WoWViewer.NET.Renderer;

namespace WoWViewer.NET.Objects
{
    public class WMOContainer : Container3D
    {
        private readonly GL _gl;
        private readonly uint _shaderProgram;
        private bool[]? enabledGroups;
        private bool[]? enabledDoodadSets;

        public bool[] EnabledGroups
        {
            get
            {
                var wmo = GetWMO();
                if (enabledGroups == null || enabledGroups.Length != wmo.groupBatches.Length)
                {
                    enabledGroups = new bool[wmo.groupBatches.Length];
                    Array.Fill(enabledGroups, true);
                }
                return enabledGroups;
            }
        }

        public bool[] EnabledDoodadSets
        {
            get
            {
                var wmo = GetWMO();
                if (enabledDoodadSets == null || enabledDoodadSets.Length != wmo.doodadSets.Length)
                {
                    enabledDoodadSets = new bool[wmo.doodadSets.Length];
                    Array.Fill(enabledDoodadSets, true);
                }
                return enabledDoodadSets;
            }
        }

        public bool IsLoaded
        {
            get
            {
                var wmo = GetWMO();
                return wmo.rootWMOFileDataID == FileDataId && wmo.groupBatches != null && wmo.groupBatches.Length > 0;
            }
        }

        public uint UniqueID;

        public WMOContainer(GL gl, uint fileDataID, uint shaderProgram, uint parentFileDataId) : base(gl, fileDataID, shaderProgram, parentFileDataId)
        {
            _gl = gl;
            _shaderProgram = shaderProgram;

            // Trigger initial array creation
            _ = EnabledGroups;
            _ = EnabledDoodadSets;
        }

        private Structs.WorldModel GetWMO()
        {
            return WMOCache.GetOrLoad(_gl, FileDataId, _shaderProgram, ParentFileDataId);
        }

        public override BoundingSphere? GetBoundingSphere()
        {
            if (!IsLoaded)
                return null;

            var wmo = GetWMO();
            var center = (wmo.boundingBox.min + wmo.boundingBox.max) / 2f;
            var halfExtents = (wmo.boundingBox.max - wmo.boundingBox.min) / 2f;
            var radius = halfExtents.Length();

            var modelMatrix = Matrix4x4.CreateScale(Scale);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (Rotation.Y - 270f));
            modelMatrix *= Matrix4x4.CreateTranslation(Position.X, Position.Z * -1, Position.Y);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

            var transformedCenter = Vector3.Transform(center, modelMatrix);

            return new BoundingSphere(transformedCenter, radius * Scale);
        }

        public override BoundingBox? GetBoundingBox()
        {
            if (!IsLoaded)
                return null;

            var wmo = GetWMO();
            var box = new BoundingBox(wmo.boundingBox.min, wmo.boundingBox.max);

            var modelMatrix = Matrix4x4.CreateScale(Scale);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (Rotation.Y - 270f));
            modelMatrix *= Matrix4x4.CreateTranslation(Position.X, Position.Z * -1, Position.Y);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

            return BoundingBox.Transform(box, modelMatrix);
        }
    }
}
