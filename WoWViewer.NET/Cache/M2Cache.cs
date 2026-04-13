using Silk.NET.OpenGL;
using System.Diagnostics;
using WoWViewer.NET.Loaders;
using WoWViewer.NET.Structs;

namespace WoWViewer.NET.Cache
{
    public static class M2Cache
    {
        private static readonly Dictionary<uint, DoodadBatch> Cache = [];
        private static readonly Dictionary<uint, List<uint>> Users = [];

        public static DoodadBatch GetOrLoad(GL gl, uint fileDataId, uint shaderProgram, uint parent)
        {
            if (Users.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                Users.Add(fileDataId, [parent]);

            if (Cache.TryGetValue(fileDataId, out DoodadBatch value))
                return value;

            try
            {
                Cache.Add(fileDataId, M2Loader.LoadM2(gl, fileDataId, shaderProgram));
            }
            catch (Exception e)
            {
                Console.WriteLine("Error loading M2 " + fileDataId + ": " + e.Message);
                Cache.Add(fileDataId, M2Loader.LoadM2(gl, 166046, shaderProgram));
            }

            return Cache[fileDataId];
        }

        public static void Release(GL gl, uint fileDataId, uint parent)
        {
            if (Users.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);
                if (users.Count == 0)
                {
                    Users.Remove(fileDataId);
                    if (Cache.TryGetValue(fileDataId, out var model))
                    {
                        // TODO: Dispose model GPU resources (VAO, VBOs) and release BLP textures
                        Cache.Remove(fileDataId);
                    }
                }
                else
                {
                    Users[fileDataId] = users;
                }
            }
        }

        public static void ReleaseAll(GL gl)
        {
            Debug.WriteLine("Releasing " + Cache.Count + " cached M2s.");

            // TODO
        }
    }
}
