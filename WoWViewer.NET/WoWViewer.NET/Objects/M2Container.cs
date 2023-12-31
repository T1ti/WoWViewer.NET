using Silk.NET.OpenGL;
using WoWViewer.NET.Renderer;

namespace WoWViewer.NET.Objects
{
    public class M2Container : Container3D
    {
        public bool[] EnabledGeosets { get; }

        public bool forceRender { get; set; } = false;
        public M2Container(GL gl, uint fileDataID, uint shaderProgram) : base(gl, fileDataID, shaderProgram)
        {
            var m2 = Cache.GetOrLoadM2(gl, fileDataID, shaderProgram);
            EnabledGeosets = new bool[m2.submeshes.Length];

            // Is there no way to initialize an array of true bools?
            for (int i = 0; i < EnabledGeosets.Length; i++)
                EnabledGeosets[i] = true;
        }
    }
}
