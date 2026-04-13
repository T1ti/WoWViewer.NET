using Silk.NET.OpenGL;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.TEX;
using WoWFormatLib.Structs.WDT;
using WoWViewer.NET.Loaders;
using WoWViewer.NET.Structs;

namespace WoWViewer.NET.Renderer
{
    public static class Cache
    {
        private static readonly Dictionary<uint, WDT> WDTCache = [];
        private static readonly Dictionary<string, Terrain> ADTCache = [];
        private static readonly Dictionary<uint, WorldModel> WMOCache = [];
        private static readonly Dictionary<uint, DoodadBatch> M2Cache = [];
        private static readonly Dictionary<uint, uint> BLPCache = [];

        private static readonly Dictionary<string, List<uint>> ADTUsers = [];
        private static readonly Dictionary<uint, List<uint>> WMOUsers = [];
        private static readonly Dictionary<uint, List<uint>> M2Users = [];
        private static readonly Dictionary<uint, List<uint>> BLPUsers = [];

        private static GL? cachedGL = null;
        private static TEXFile? cachedTEX = null;

        private static readonly HashSet<uint> wmosInFlight = [];
        private static readonly HashSet<uint> blpsInFlight = [];

        private static readonly Lock wmoQueueLock = new();
        private static readonly Queue<uint> wmoParseQueue = [];
        private static readonly Queue<(uint originalFileDataId, PreppedWMO preppedWMO)> wmoUploadQueue = [];

        private static readonly Lock blpQueueLock = new();
        private static readonly Queue<uint> blpDecodeQueue = [];
        private static readonly Queue<DecodedBLP> blpUploadQueue = [];

        private static CancellationTokenSource? wmoLoaderCancellation;
        private static Task? wmoLoaderTask;

        private static CancellationTokenSource? blpLoaderCancellation;
        private static Task? blpLoaderTask;

        #region M2
        public static DoodadBatch GetOrLoadM2(GL gl, uint fileDataId, uint shaderProgram, uint parent)
        {
            if (M2Users.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                M2Users.Add(fileDataId, [parent]);

            if (M2Cache.TryGetValue(fileDataId, out DoodadBatch value))
                return value;

            try
            {
                M2Cache.Add(fileDataId, M2Loader.LoadM2(gl, fileDataId, shaderProgram));
            }
            catch (Exception e)
            {
                Console.WriteLine("Error loading M2 " + fileDataId + ": " + e.Message);
                M2Cache.Add(fileDataId, M2Loader.LoadM2(gl, 166046, shaderProgram));
            }

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
            if (cachedGL == null)
                cachedGL = gl;

            StartWMOLoader();

            if (WMOUsers.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                WMOUsers.Add(fileDataId, [parent]);

            if (WMOCache.TryGetValue(fileDataId, out WorldModel value))
                return value;

            WorldModel placeholderWMO;

            try
            {
                var preppedWMO = WMOLoader.ParseWMO(112521);
                placeholderWMO = WMOLoader.LoadWMO(preppedWMO, gl, shaderProgram); // missingwmo.wmo
            }
            catch (Exception e)
            {
                Console.WriteLine("!!! Error loading placeholder WMO: " + e.Message);
                placeholderWMO = new WorldModel();
            }

            WMOCache.Add(fileDataId, placeholderWMO);

            lock (wmoQueueLock)
            {
                if (wmosInFlight.Contains(fileDataId))
                    return placeholderWMO;

                wmosInFlight.Add(fileDataId);
                wmoParseQueue.Enqueue(fileDataId);

                return placeholderWMO;
            }
        }

        private static void StartWMOLoader()
        {
            if (wmoLoaderTask != null)
                return;

            wmoLoaderCancellation = new CancellationTokenSource();
            wmoLoaderTask = Task.Run(() => WMOParserWorker(wmoLoaderCancellation.Token), wmoLoaderCancellation.Token);
        }

        private static async Task WMOParserWorker(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                uint fileDataId = 0;
                bool hasWork = false;

                lock (wmoQueueLock)
                {
                    if (wmoParseQueue.Count > 0)
                    {
                        fileDataId = wmoParseQueue.Dequeue();
                        hasWork = true;
                    }
                }

                if (!hasWork)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                try
                {
                    var preppedWMO = WMOLoader.ParseWMO(fileDataId);

                    lock (wmoQueueLock)
                        wmoUploadQueue.Enqueue((fileDataId, preppedWMO));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"!!! Error parsing WMO {fileDataId}: {e.Message}");

                    // Remove from in-flight set so it's not stuck in limbo
                    lock (wmoQueueLock)
                        wmosInFlight.Remove(fileDataId);
                }
            }
        }

        public static void UploadParsedWMOs(uint shaderProgram)
        {
            if (cachedGL == null)
                return;

            const int maxUploadsPerFrame = 5;
            int uploaded = 0;

            while (uploaded < maxUploadsPerFrame)
            {
                uint originalFileDataId;
                PreppedWMO preppedWMO;

                lock (wmoQueueLock)
                {
                    if (wmoUploadQueue.Count == 0)
                        break;

                    (originalFileDataId, preppedWMO) = wmoUploadQueue.Dequeue();
                }

                if (!WMOCache.TryGetValue(originalFileDataId, out var oldWMO))
                    continue;

                try
                {
                    var newWMO = WMOLoader.LoadWMO(preppedWMO, cachedGL, shaderProgram);
                    WMOCache[originalFileDataId] = newWMO;

                    if (oldWMO.groupBatches != null && oldWMO.groupBatches.Length > 0)
                        WMOLoader.UnloadWMO(cachedGL, oldWMO);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"!!! Error uploading WMO {originalFileDataId}: {e.Message}");
                }

                lock (wmoQueueLock)
                    wmosInFlight.Remove(originalFileDataId);

                uploaded++;
            }
        }

        public static void StopWMOLoader()
        {
            wmoLoaderCancellation?.Cancel();
            wmoLoaderCancellation?.Dispose();
            wmoLoaderCancellation = null;
            wmoLoaderTask = null;
        }

        public static int GetWMOLoadQueueCount()
        {
            lock (wmoQueueLock)
                return wmoParseQueue.Count + wmoUploadQueue.Count;
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

            ADTCache.TryAdd(key, ADTLoader.LoadADT(gl, mapTile, shaderProgram));

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
            if (cachedGL == null)
            {
                cachedGL = gl;
                StartBLPLoader();
            }

            if (BLPUsers.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                BLPUsers.Add(fileDataId, [parent]);

            if (BLPCache.TryGetValue(fileDataId, out var value))
                return value;

            uint placeholderTextureID = uint.MaxValue;

            if (cachedTEX != null && cachedTEX.Value.blobTextures.TryGetValue((int)fileDataId, out var blobTex))
            {
                try
                {
                    placeholderTextureID = BLPLoader.CreateTextureFromBlob(gl, blobTex, cachedTEX.Value.mipMapData[cachedTEX.Value.txmdOffsetsToIndex[(int)blobTex.txmdOffset]]);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to create texture from BLP blob {fileDataId}: {e.Message}");
                }
            }

            if (placeholderTextureID == uint.MaxValue)
                placeholderTextureID = BLPLoader.CreatePlaceholderTexture(gl);

            BLPCache.Add(fileDataId, placeholderTextureID);

            lock (blpQueueLock)
            {
                if (blpsInFlight.Contains(fileDataId))
                    return placeholderTextureID;

                blpsInFlight.Add(fileDataId);
                blpDecodeQueue.Enqueue(fileDataId);

                return placeholderTextureID;
            }
        }

        private static void StartBLPLoader()
        {
            if (blpLoaderTask != null)
                return;

            blpLoaderCancellation = new CancellationTokenSource();
            blpLoaderTask = Task.Run(() => BLPDecoderWorker(blpLoaderCancellation.Token), blpLoaderCancellation.Token);
        }

        private static async Task BLPDecoderWorker(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                uint fileDataId = 0;
                bool hasWork = false;

                lock (blpQueueLock)
                {
                    if (blpDecodeQueue.Count > 0)
                    {
                        fileDataId = blpDecodeQueue.Dequeue();
                        hasWork = true;
                    }
                }

                if (!hasWork)
                    continue;

                try
                {
                    using var blp = new BLPSharp.BLPFile(WoWFormatLib.FileProviders.FileProvider.OpenFile(fileDataId));

                    DecodedBLP decoded;

                    if (blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt1 || blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt3 || blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt5)
                    {
                        InternalFormat compressedFormat;

                        if (blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt1 && blp.alphaSize > 0)
                            compressedFormat = InternalFormat.CompressedRgbaS3TCDxt1Ext;
                        else if (blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt1 && blp.alphaSize == 0)
                            compressedFormat = InternalFormat.CompressedRgbS3TCDxt1Ext;
                        else if (blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt3)
                            compressedFormat = InternalFormat.CompressedRgbaS3TCDxt3Ext;
                        else
                            compressedFormat = InternalFormat.CompressedRgbaS3TCDxt5Ext;

                        var mipmaps = new List<MipLevel>(blp.MipMapCount);

                        for (int i = 0; i < blp.MipMapCount; i++)
                        {
                            int scale = (int)Math.Pow(2, i);
                            var width = blp.width / scale;
                            var height = blp.height / scale;

                            if (width == 0 || height == 0)
                                break;

                            var bytes = blp.GetPictureData(i, width, height);
                            mipmaps.Add(new MipLevel
                            {
                                Data = bytes,
                                Width = width,
                                Height = height,
                                Level = i
                            });
                        }

                        decoded = new DecodedBLP
                        {
                            FileDataId = fileDataId,
                            IsCompressed = true,
                            CompressedFormat = compressedFormat,
                            MipLevels = [.. mipmaps]
                        };
                    }
                    else
                    {
                        var pixels = blp.GetPixels(0, out int width, out int height) ?? throw new Exception("BLP pixel data is null!");
                        decoded = new DecodedBLP
                        {
                            FileDataId = fileDataId,
                            PixelData = pixels,
                            Width = width,
                            Height = height,
                            IsCompressed = false
                        };
                    }
                    lock (blpQueueLock)
                        blpUploadQueue.Enqueue(decoded);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to decode BLP {fileDataId}: {e.Message}");
                }
            }
        }

        public static void UploadDecodedBLPs()
        {
            if (cachedGL == null)
                return;

            // This may need some tweaking....
            const int maxUploadsPerFrame = 10;
            int uploaded = 0;

            while (uploaded < maxUploadsPerFrame)
            {
                DecodedBLP decoded;
                lock (blpQueueLock)
                {
                    if (blpUploadQueue.Count == 0)
                        break;

                    decoded = blpUploadQueue.Dequeue();
                }

                if (!BLPCache.TryGetValue(decoded.FileDataId, out var textureId))
                    continue;

                unsafe
                {
                    try
                    {
                        cachedGL.BindTexture(TextureTarget.Texture2D, textureId);

                        if (decoded.IsCompressed)
                        {
                            foreach (var mip in decoded.MipLevels)
                            {
                                fixed (byte* ptr = mip.Data)
                                    cachedGL.CompressedTexImage2D(TextureTarget.Texture2D, mip.Level, decoded.CompressedFormat,
                                        (uint)mip.Width, (uint)mip.Height, 0, (uint)mip.Data.Length, ptr);
                            }

                            cachedGL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, decoded.MipLevels.Length - 1);
                            cachedGL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                        }
                        else
                        {
                            fixed (byte* ptr = decoded.PixelData)
                                cachedGL.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                                    (uint)decoded.Width, (uint)decoded.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);

                            cachedGL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                        }

                        cachedGL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                        cachedGL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                        cachedGL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to upload BLP {decoded.FileDataId}: {e.Message}");
                    }
                }

                lock (blpQueueLock)
                    blpsInFlight.Remove(decoded.FileDataId);

                uploaded++;
            }
        }

        public static void StopBLPLoader()
        {
            blpLoaderCancellation?.Cancel();
            blpLoaderCancellation?.Dispose();
            blpLoaderCancellation = null;
            blpLoaderTask = null;
        }

        public static int GetBLPLoadQueueCount()
        {
            lock (blpQueueLock)
                return blpDecodeQueue.Count + blpUploadQueue.Count;
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

        #region TEX
        public static void PreloadTEX(uint fileDataID)
        {
            var texReader = new TEXReader();
            cachedTEX = texReader.LoadTEX(fileDataID);
        }
        #endregion
    }
}
