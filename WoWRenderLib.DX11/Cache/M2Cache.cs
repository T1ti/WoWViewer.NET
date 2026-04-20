using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Diagnostics;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Cache
{
    public static class M2Cache
    {
        private static readonly Dictionary<uint, DoodadBatch> Cache = [];
        private static readonly Dictionary<uint, List<uint>> Users = [];

        public static DoodadBatch GetOrLoad(ComPtr<ID3D11Device> device, uint fileDataId, CompiledShader shaderProgram, uint parent, bool keepTrack = true)
        {
            if (keepTrack)
            {
                if (Users.TryGetValue(fileDataId, out var users))
                    users.Add(parent);
                else
                    Users.Add(fileDataId, [parent]);
            }

            if (Cache.TryGetValue(fileDataId, out DoodadBatch value))
                return value;

            try
            {
                Cache.Add(fileDataId, M2Loader.LoadM2(device, fileDataId, shaderProgram));
            }
            catch (Exception e)
            {
                Console.WriteLine("Error loading M2 " + fileDataId + ": " + e.Message);
                Cache.Add(fileDataId, M2Loader.LoadM2(device, 166046, shaderProgram));
            }

            return Cache[fileDataId];
        }

        public static void Release(uint fileDataId, uint parent)
        {
            if (Users.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);
                if (users.Count == 0)
                {
                    Users.Remove(fileDataId);
                    if (Cache.TryGetValue(fileDataId, out var model))
                    {
                        M2Loader.UnloadM2(model);

                        Cache.Remove(fileDataId);
                    }
                }
                else
                {
                    Users[fileDataId] = users;
                }
            }
        }

        public static int GetCacheCount()
        {
            return Cache.Count;
        }

        public static void ReleaseAll()
        {
            Debug.WriteLine("Releasing " + Cache.Count + " cached M2s.");

            foreach (var item in Users)
            {
                var fileDataId = item.Key;
                var parents = new List<uint>(item.Value);
                foreach (var parent in parents)
                    Release(fileDataId, parent);
            }

            Cache.Clear();
            Users.Clear();
        }
    }
}
