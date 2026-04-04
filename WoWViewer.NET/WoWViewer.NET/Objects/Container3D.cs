using Silk.NET.OpenGL;
using System.Numerics;
using WoWViewer.NET.Raycasting;

namespace WoWViewer.NET.Objects
{
    public class Container3D
    {
        public uint ParentFileDataId { get; set; }
        public uint FileDataId { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float Scale;

        public Container3D(GL gl, uint fileDataId, uint shaderProgram, uint parentFileDataId)
        {
            FileDataId = fileDataId;
            ParentFileDataId = parentFileDataId;
        }

        public virtual BoundingSphere? GetBoundingSphere()
        {
            return null;
        }

        public virtual BoundingBox? GetBoundingBox()
        {
            return null;
        }
    }
}
