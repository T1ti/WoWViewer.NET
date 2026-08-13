using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Cache
{
    public static class BLPCache
    {
        private static ComPtr<ID3D11Device>? cachedDevice = null;

        private static readonly HashSet<uint> inFlight = new();

        private static readonly ConcurrentDictionary<uint, ComPtr<ID3D11ShaderResourceView>> Cache = new();
        private static readonly ConcurrentDictionary<uint, List<uint>> Users = new();

        private static readonly ConcurrentQueue<uint> decodeQueue = new();
        private static readonly ConcurrentQueue<DecodedBLP> uploadQueue = new();


        private static CancellationTokenSource? workerCancellation;
        private static Task? workerTask;

        public static ComPtr<ID3D11ShaderResourceView> GetOrLoad(ComPtr<ID3D11Device> device, uint fileDataId, uint parent)
        {
            cachedDevice ??= device;

            StartWorker();

            if (Users.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                Users.TryAdd(fileDataId, [parent]);

            if (Cache.TryGetValue(fileDataId, out var value))
                return value;

            ComPtr<ID3D11ShaderResourceView> placeholderTexture = default;
            var loadedPlaceholder = false;

            if (TEXCache.cachedTEX != null && TEXCache.cachedTEX.Value.blobTextures.TryGetValue((int)fileDataId, out var blobTex))
            {
                try
                {
                    placeholderTexture = BLPLoader.CreateTextureFromBlob(cachedDevice.Value, blobTex, TEXCache.cachedTEX.Value.mipMapData[TEXCache.cachedTEX.Value.txmdOffsetsToIndex[(int)blobTex.txmdOffset]]);
                    loadedPlaceholder = true;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to create texture from BLP blob {fileDataId}: {e.Message}");
                }
            }

            if (!loadedPlaceholder)
                placeholderTexture = BLPLoader.CreatePlaceholderTexture(cachedDevice.Value);

            Cache.TryAdd(fileDataId, placeholderTexture);

            if (inFlight.Contains(fileDataId))
                return placeholderTexture;

            inFlight.Add(fileDataId);
            decodeQueue.Enqueue(fileDataId);

            return placeholderTexture;
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

                if (decodeQueue.TryDequeue(out fileDataId))
                    hasWork = true;

                if (!hasWork)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                try
                {
                    using var blp = new BLPSharp.BLPFile(WoWFormatLib.FileProviders.FileProvider.OpenFile(fileDataId));

                    DecodedBLP decoded;

                    if (blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt1 || blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt3 || blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt5)
                    {
                        Format compressedFormat;

                        if (blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt1)
                            compressedFormat = Format.FormatBC1Unorm;
                        else if (blp.preferredFormat == BLPSharp.BlpPixelFormat.Dxt3)
                            compressedFormat = Format.FormatBC2Unorm;
                        else
                            compressedFormat = Format.FormatBC3Unorm;
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
                            MipLevels = mipmaps
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

                    uploadQueue.Enqueue(decoded);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to decode BLP {fileDataId}: {e.Message}");
                }
            }
        }

        public static ComPtr<ID3D11ShaderResourceView> GetCurrent(uint fileDataId, ComPtr<ID3D11ShaderResourceView> fallback)
        {
            if (Cache.TryGetValue(fileDataId, out var srv))
                return srv;

            return fallback;
        }

        public static int Upload(Stopwatch queueTimer)
        {
            if (!cachedDevice.HasValue)
                return 0;

            var uploaded = 0;
            while (queueTimer.ElapsedMilliseconds < 5)
            {
                if (!uploadQueue.TryDequeue(out var decoded))
                    break;

                try
                {
                    // create DX11 texture and SRV from decoded data
                    var device = cachedDevice.Value;

                    unsafe
                    {
                        var texDesc = new Texture2DDesc
                        {
                            Width = (uint)(decoded.IsCompressed ? decoded.MipLevels![0].Width : decoded.Width),
                            Height = (uint)(decoded.IsCompressed ? decoded.MipLevels![0].Height : decoded.Height),
                            MipLevels = (uint)(decoded.IsCompressed ? decoded.MipLevels!.Count : 1),
                            ArraySize = 1,
                            Format = decoded.IsCompressed ? decoded.CompressedFormat : Format.FormatR8G8B8A8Unorm,
                            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
                            Usage = Usage.Default,
                            BindFlags = (uint)BindFlag.ShaderResource,
                            CPUAccessFlags = 0,
                            MiscFlags = 0
                        };

                        var mipCount = texDesc.MipLevels;
                        var initData = new SubresourceData[mipCount];
                        var sizePerBlock = decoded.CompressedFormat switch { Format.FormatBC1Unorm => 8, Format.FormatBC2Unorm => 16, Format.FormatBC3Unorm => 16, _ => 0 };
                        var handles = new GCHandle[mipCount];

                        ComPtr<ID3D11Texture2D> tex = default;
                        var texCreated = false;
                        try
                        {
                            if (decoded.IsCompressed)
                            {
                                for (int i = 0; i < mipCount; i++)
                                {
                                    var mip = decoded.MipLevels![i];
                                    handles[i] = GCHandle.Alloc(mip.Data, GCHandleType.Pinned);
                                    initData[i].PSysMem = (void*)handles[i].AddrOfPinnedObject();
                                    initData[i].SysMemPitch = (uint)((Math.Max(1, mip.Width / 4)) * sizePerBlock);
                                }
                            }
                            else
                            {
                                fixed (byte* p = decoded.PixelData)
                                {
                                    handles[0] = GCHandle.Alloc(decoded.PixelData, GCHandleType.Pinned);
                                    initData[0].PSysMem = (void*)handles[0].AddrOfPinnedObject();
                                    initData[0].SysMemPitch = (uint)(decoded.Width * 4);
                                }
                            }

                            SilkMarshal.ThrowHResult(device.CreateTexture2D(in texDesc, ref initData[0], ref tex));
                            texCreated = true;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to create texture for BLP {decoded.FileDataId}: {e.Message}");
                            Cache[decoded.FileDataId] = BLPLoader.CreatePlaceholderTexture(device);
                            texCreated = false;
                        }
                        finally
                        {
                            for (int i = 0; i < mipCount; i++)
                            {
                                if (handles[i].IsAllocated)
                                    handles[i].Free();
                            }
                        }

                        var srvDesc = new ShaderResourceViewDesc
                        {
                            Format = texDesc.Format,
                            ViewDimension = D3DSrvDimension.D3D101SrvDimensionTexture2D,
                            Texture2D = new Tex2DSrv { MipLevels = texDesc.MipLevels, MostDetailedMip = 0 }
                        };

                        ComPtr<ID3D11ShaderResourceView> srv = default;
                        if (texCreated)
                            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(tex, in srvDesc, ref srv));
                        else
                            srv = BLPLoader.CreatePlaceholderTexture(device);

                        Cache[decoded.FileDataId] = srv;
                        uploaded++;

                        tex.Dispose();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to upload BLP {decoded.FileDataId}: {e.Message}");
                }

                inFlight.Remove(decoded.FileDataId);
            }

            return uploaded;
        }

        public static void StopWorker()
        {
            StopWorkerAsync().GetAwaiter().GetResult();
        }

        public static async Task StopWorkerAsync()
        {
            var cancellation = workerCancellation;
            var task = workerTask;
            workerCancellation = null;
            workerTask = null;

            if (cancellation == null)
                return;

            cancellation.Cancel();
            try
            {
                if (task != null)
                    await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        public static int GetQueueCount()
        {
            return decodeQueue.Count + uploadQueue.Count;
        }

        public static void Release(uint fileDataId, uint parent)
        {
            if (Users.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);

                if (users.Count == 0)
                {
                    Users.TryRemove(fileDataId, out _);

                    if (Cache.TryRemove(fileDataId, out var srv))
                        srv.Dispose();
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

        public static void CheckUsers()
        {
            var blpsToRemove = new List<uint>();
            foreach (var cachedBLP in Cache.Keys)
                if (!Users.ContainsKey(cachedBLP))
                    blpsToRemove.Add(cachedBLP);

            foreach (var blpId in blpsToRemove)
            {
                if (Cache.TryGetValue(blpId, out var blp))
                {
                    Cache.TryRemove(blpId, out _);
                    blp.Dispose();
                }
            }   
        }

        public static void ReleaseAll()
        {
            Debug.WriteLine("Releasing " + Cache.Count + " cached BLPs.");
            foreach (var kv in Cache)
                kv.Value.Dispose();

            Cache.Clear();
            Users.Clear();
            inFlight.Clear();
            while (decodeQueue.TryDequeue(out _)) { }
            while (uploadQueue.TryDequeue(out _)) { }
            cachedDevice = null;
        }
    }
}
