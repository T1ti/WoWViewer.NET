using Silk.NET.OpenGL;
using WoWViewer.NET.Structs;

namespace WoWViewer.NET.Objects
{
    public class ADTContainer : Container3D
    {
        public Terrain Terrain { get; }
        public MapTile mapTile;

        public ADTContainer(GL gl, Terrain terrain, MapTile mapTile, uint shaderProgram) : base(gl, mapTile.wdtFileDataID, shaderProgram, mapTile.wdtFileDataID)
        {
            Terrain = terrain;
            this.mapTile = mapTile;
        }
    }
}
