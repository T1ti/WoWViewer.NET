using Silk.NET.OpenGL;
using WoWViewer.NET.Renderer;

namespace WoWViewer.NET.Objects
{
    public class M2Container : Container3D
    {
        public bool[] EnabledGeosets { get; }

        public bool forceRender { get; set; } = false;
        public M2Container(GL gl, uint fileDataID, uint shaderProgram, uint parentFileDataId) : base(gl, fileDataID, shaderProgram, parentFileDataId)
        {
            var m2 = Cache.GetOrLoadM2(gl, fileDataID, shaderProgram, parentFileDataId);
            EnabledGeosets = new bool[m2.submeshes.Length];
            Array.Fill(EnabledGeosets, true);

        }
    }
}
