using Silk.NET.OpenGL;
using WoWViewer.NET.Loaders;
using WoWViewer.NET.Structs;

namespace WoWViewer.NET.Cache
{
    public static class ADTCache
    {
        private static readonly Dictionary<string, Terrain> Cache = [];
        private static readonly Dictionary<string, List<uint>> Users = [];

        public static Terrain GetOrLoad(GL gl, MapTile mapTile, uint shaderProgram, uint parent)
        {
            var key = (mapTile.wdtFileDataID, mapTile.tileX, mapTile.tileY).ToString();

            if (Users.TryGetValue(key, out var users))
                users.Add(parent);
            else
                Users.Add(key, [parent]);

            if (Cache.TryGetValue(key, out Terrain value))
                return value;

            Cache.TryAdd(key, ADTLoader.LoadADT(gl, mapTile, shaderProgram));

            return Cache[key];
        }

        public static void Release(GL gl, MapTile mapTile, uint parent)
        {
            var key = (mapTile.wdtFileDataID, mapTile.tileX, mapTile.tileY).ToString();
            if (Users.TryGetValue(key, out var users))
            {
                users.Remove(parent);
                if (users.Count == 0)
                {
                    Users.Remove(key);
                    if (Cache.TryGetValue(key, out var terrain))
                    {
                        ADTLoader.UnloadTerrain(terrain, gl);
                        Cache.Remove(key);
                    }
                }
                else
                {
                    Users[key] = users;
                }
            }
        }
    }
}
