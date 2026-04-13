using Silk.NET.OpenGL;
using WoWViewer.NET.Loaders;
using WoWViewer.NET.Structs;

namespace WoWViewer.NET.Cache
{
    public static class BLPCache
    {
        private static readonly Dictionary<uint, uint> Cache = [];

        private static GL? cachedGL = null;

        private static readonly HashSet<uint> inFlight = [];

        private static readonly Dictionary<uint, List<uint>> Users = [];

        private static readonly Lock queueLock = new();
        private static readonly Queue<uint> decodeQueue = [];
        private static readonly Queue<DecodedBLP> uploadQueue = [];

        private static CancellationTokenSource? workerCancellation;
        private static Task? workerTask;

        public static uint GetOrLoad(GL gl, uint fileDataId, uint parent)
        {
            cachedGL ??= gl;

            StartWorker();

            if (Users.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                Users.Add(fileDataId, [parent]);

            if (Cache.TryGetValue(fileDataId, out var value))
                return value;

            uint placeholderTextureID = uint.MaxValue;

            if (TEXCache.cachedTEX != null && TEXCache.cachedTEX.Value.blobTextures.TryGetValue((int)fileDataId, out var blobTex))
            {
                try
                {
                    placeholderTextureID = BLPLoader.CreateTextureFromBlob(gl, blobTex, TEXCache.cachedTEX.Value.mipMapData[TEXCache.cachedTEX.Value.txmdOffsetsToIndex[(int)blobTex.txmdOffset]]);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to create texture from BLP blob {fileDataId}: {e.Message}");
                }
            }

            if (placeholderTextureID == uint.MaxValue)
                placeholderTextureID = BLPLoader.CreatePlaceholderTexture(gl);

            Cache.Add(fileDataId, placeholderTextureID);

            lock (queueLock)
            {
                if (inFlight.Contains(fileDataId))
                    return placeholderTextureID;

                inFlight.Add(fileDataId);
                decodeQueue.Enqueue(fileDataId);

                return placeholderTextureID;
            }
        }

        private static void StartWorker()
        {
            if (workerTask != null)
                return;

            workerCancellation = new CancellationTokenSource();
            workerTask = Task.Run(() => DecodeWorker(workerCancellation.Token), workerCancellation.Token);
        }

        private static async Task DecodeWorker(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                uint fileDataId = 0;
                bool hasWork = false;

                lock (queueLock)
                {
                    if (decodeQueue.TryDequeue(out fileDataId))
                        hasWork = true;
                    else
                        continue;
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
                    lock (queueLock)
                        uploadQueue.Enqueue(decoded);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to decode BLP {fileDataId}: {e.Message}");
                }
            }
        }

        public static void Upload()
        {
            if (cachedGL == null)
                return;

            // This may need some tweaking....
            const int maxUploadsPerFrame = 10;
            int uploaded = 0;

            while (uploaded < maxUploadsPerFrame)
            {
                DecodedBLP decoded;
                lock (queueLock)
                {
                    if (!uploadQueue.TryDequeue(out decoded))
                        break;
                }

                if (!Cache.TryGetValue(decoded.FileDataId, out var textureId))
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

                            cachedGL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, decoded.MipLevels.Count - 1);
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

                lock (queueLock)
                    inFlight.Remove(decoded.FileDataId);

                uploaded++;
            }
        }

        public static void StopWorker()
        {
            workerCancellation?.Cancel();
            workerCancellation?.Dispose();
            workerCancellation = null;
            workerTask = null;
        }

        public static int GetQueueCount()
        {
            lock (queueLock)
                return decodeQueue.Count + uploadQueue.Count;
        }

        public static void Release(GL gl, uint fileDataId, uint parent)
        {
            if (Users.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);

                if (users.Count == 0)
                {
                    Users.Remove(fileDataId);
                    if (Cache.TryGetValue(fileDataId, out var textureId))
                    {
                        gl.DeleteTexture(textureId);
                        Cache.Remove(fileDataId);
                    }
                }
                else
                {
                    Users[fileDataId] = users;
                }
            }
        }
    }
}
