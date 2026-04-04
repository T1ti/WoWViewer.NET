using Silk.NET.OpenGL;
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
    }
}
