using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Diagnostics;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Cache
{
    public static class ADTCache
    {
        private static readonly Dictionary<string, Terrain> Cache = [];
        private static readonly Dictionary<string, List<uint>> Users = [];

        public static Terrain GetOrLoad(ComPtr<ID3D11Device> device, MapTile mapTile, CompiledShader shaderProgram, uint parent)
        {
            var key = (mapTile.wdtFileDataID, mapTile.tileX, mapTile.tileY).ToString();

            if (Users.TryGetValue(key, out var users))
                users.Add(parent);
            else
                Users.Add(key, [parent]);

            if (Cache.TryGetValue(key, out Terrain value))
                return value;

            Cache.TryAdd(key, ADTLoader.LoadADT(device, mapTile, shaderProgram));

            return Cache[key];
        }

        public static void Release(ComPtr<ID3D11Device> device, MapTile mapTile, uint parent)
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
                        ADTLoader.UnloadTerrain(terrain);
                        Cache.Remove(key);
                    }
                }
                else
                {
                    Users[key] = users;
                }
            }
        }

        public static int GetCacheCount()
        {
            return Cache.Count;
        }

        public static void ReleaseAll()
        {
            Debug.WriteLine("Releasing " + Cache.Count + " cached ADTs.");

            foreach (var key in Cache.Keys)
                if (Cache.TryGetValue(key, out var terrain))
                    ADTLoader.UnloadTerrain(terrain);

            Users.Clear();
            Cache.Clear();
        }
    }
}
