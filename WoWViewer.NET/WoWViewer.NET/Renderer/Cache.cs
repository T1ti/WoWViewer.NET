using Silk.NET.OpenGL;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.WDT;
using WoWViewer.NET.Loaders;
using static WoWViewer.NET.Renderer.Structs;
using static WoWViewer.NET.Structs;

namespace WoWViewer.NET.Renderer
{
    public static class Cache
    {
        private static Dictionary<uint, WDT> WDTCache = new();
        private static Dictionary<string, Terrain> ADTCache = new();
        private static Dictionary<uint, WorldModel> WMOCache = new();
        private static Dictionary<uint, DoodadBatch> M2Cache = new();
        private static Dictionary<uint, uint> BLPCache = new();

        private static Dictionary<string, List<uint>> ADTUsers = [];
        private static Dictionary<uint, List<uint>> WMOUsers = [];
        private static Dictionary<uint, List<uint>> M2Users = [];
        private static Dictionary<uint, List<uint>> BLPUsers = [];

        #region M2
        public static DoodadBatch GetOrLoadM2(GL gl, uint fileDataId, uint shaderProgram, uint parent)
        {
            if (M2Users.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                M2Users.Add(fileDataId, [parent]);

            if (M2Cache.TryGetValue(fileDataId, out DoodadBatch value))
                return value;

            M2Cache.Add(fileDataId, M2Loader.LoadM2(gl, fileDataId, shaderProgram));

            return M2Cache[fileDataId];
        }

        public static void ReleaseM2(GL gl, uint fileDataId, uint parent)
        {
            if (M2Users.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);
                if (users.Count == 0)
                {
                    M2Users.Remove(fileDataId);
                    if (M2Cache.TryGetValue(fileDataId, out var model))
                    {
                        // TODO: Dispose model GPU resources (VAO, VBOs) and release BLP textures
                        M2Cache.Remove(fileDataId);
                    }
                }
                else
                {
                    M2Users[fileDataId] = users;
                }
            }
        }
        #endregion

        #region WMO
        public static WorldModel GetOrLoadWMO(GL gl, uint fileDataId, uint shaderProgram, uint parent)
        {
            if (WMOUsers.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                WMOUsers.Add(fileDataId, [parent]);

            if (WMOCache.TryGetValue(fileDataId, out WorldModel value))
                return value;

            WMOCache.Add(fileDataId, WMOLoader.LoadWMO(gl, fileDataId, shaderProgram));

            return WMOCache[fileDataId];
        }

        public static void ReleaseWMO(GL gl, uint fileDataId, uint parent)
        {
            if (WMOUsers.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);
                if (users.Count == 0)
                {
                    WMOUsers.Remove(fileDataId);
                    if (WMOCache.TryGetValue(fileDataId, out var wmo))
                    {
                        WMOCache.Remove(fileDataId);
                        WMOLoader.UnloadWMO(gl, wmo);
                    }
                }
                else
                {
                    WMOUsers[fileDataId] = users;
                }
            }
        }
        #endregion

        #region ADT
        public static Terrain GetOrLoadADT(GL gl, MapTile mapTile, uint shaderProgram, uint parent)
        {
            var key = (mapTile.wdtFileDataID, mapTile.tileX, mapTile.tileY).ToString();

            if (ADTUsers.TryGetValue(key, out var users))
                users.Add(parent);
            else
                ADTUsers.Add(key, [parent]);

            if (ADTCache.TryGetValue(key, out Terrain value))
                return value;

            ADTCache.Add(key, ADTLoader.LoadADT(gl, mapTile, shaderProgram));

            return ADTCache[key];
        }

        public static void ReleaseADT(GL gl, MapTile mapTile, uint parent)
        {
            var key = (mapTile.wdtFileDataID, mapTile.tileX, mapTile.tileY).ToString();
            if (ADTUsers.TryGetValue(key, out var users))
            {
                users.Remove(parent);
                if (users.Count == 0)
                {
                    ADTUsers.Remove(key);
                    if (ADTCache.TryGetValue(key, out var terrain))
                    {
                        ADTLoader.UnloadTerrain(terrain, gl);
                        ADTCache.Remove(key);
                    }
                }
                else
                {
                    ADTUsers[key] = users;
                }
            }
        }
        #endregion

        #region BLP
        public static uint GetOrLoadBLP(GL gl, uint fileDataId, uint parent)
        {
            if (BLPUsers.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                BLPUsers.Add(fileDataId, [parent]);

            if (BLPCache.TryGetValue(fileDataId, out uint value))
                return value;

            BLPCache.Add(fileDataId, BLPLoader.LoadTexture(gl, fileDataId));

            return BLPCache[fileDataId];
        }

        public static void ReleaseBLP(GL gl, uint fileDataId, uint parent)
        {
            if (BLPUsers.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);

                if (users.Count == 0)
                {
                    BLPUsers.Remove(fileDataId);
                    if (BLPCache.TryGetValue(fileDataId, out var textureId))
                    {
                        Console.WriteLine("Deleting BLP texture " + textureId);
                        gl.DeleteTexture(textureId);
                        BLPCache.Remove(fileDataId);
                    }
                }
                else
                {
                    BLPUsers[fileDataId] = users;
                }
            }
        }
        #endregion

        #region WDT
        public static WDT GetOrLoadWDT(uint fileDataID)
        {
            if (WDTCache.TryGetValue(fileDataID, out WDT value))
                return value;

            var wdtReader = new WDTReader();
            wdtReader.LoadWDT(fileDataID);
            WDTCache.Add(fileDataID, wdtReader.wdtfile);

            return WDTCache[fileDataID];
        }

        public static void ReleaseWDT(uint fileDataID)
        {
            // TODO: Do we also want to automatically remove ADTs?
            WDTCache.Remove(fileDataID);
        }
        #endregion
    }
}
