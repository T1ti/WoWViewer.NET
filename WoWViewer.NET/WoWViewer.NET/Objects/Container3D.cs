using Silk.NET.OpenGL;
using System.Numerics;

namespace WoWViewer.NET.Objects
{
    public class Container3D
    {
        public uint FileDataId { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float Scale;

        public Container3D(GL gl, uint fileDataId, uint shaderProgram)
        {
            FileDataId = fileDataId;
        }
    }
}
