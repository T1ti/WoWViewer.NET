using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WoWLib;
using Formats = WoWLib.Formats;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Streaming;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Services;

namespace WoWRenderLib.DX11.Cache
{
    public static class BLPCache
    {
        private static ComPtr<ID3D11Device>? cachedDevice = null;

        private static readonly ConcurrentDictionary<uint, byte> inFlight = new();

        private static readonly ConcurrentDictionary<uint, ComPtr<ID3D11ShaderResourceView>> Cache = new();
        private static readonly ConcurrentDictionary<uint, List<uint>> Users = new();

        // The old TEX cache supplied an immediate low-resolution texture for
        // many BLPs.  wowlib does not expose that blob cache, so allocating a
        // new D3D texture/SRV for every pending BLP would add two synchronous
        // resource creations to every tile load.  Keep one shared fallback
        // instead; the real BLP is inserted into Cache after the worker has
        // decoded and uploaded it.
        private static ComPtr<ID3D11ShaderResourceView>? pendingTexture;

        private static readonly BackgroundResourceQueue<uint, DecodedBLP> loadQueue =
            new(
                Decode,
                shouldProcess: fileDataId => Users.ContainsKey(fileDataId));

        public static ComPtr<ID3D11ShaderResourceView> GetOrLoad(ComPtr<ID3D11Device> device, uint fileDataId, uint parent)
        {
            cachedDevice ??= device;

            if (Users.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                Users.TryAdd(fileDataId, [parent]);

            if (Cache.TryGetValue(fileDataId, out var value))
                return value;

            pendingTexture ??= BLPLoader.CreatePlaceholderTexture(cachedDevice.Value);

            if (inFlight.TryAdd(fileDataId, 0))
            {
                try
                {
                    loadQueue.Enqueue(fileDataId);
                }
                catch
                {
                    inFlight.TryRemove(fileDataId, out _);
                    throw;
                }
            }

            return pendingTexture.Value;
        }

        private static DecodedBLP Decode(uint fileDataId)
        {
            using var blp = new Formats.BLP.BLP();
            blp.Read(WowlibFileSystem.Current, new FileKey(new FileDataId(fileDataId)));

            if (blp.PreferredFormat == Formats.BLP.PixelFormat.Dxt1 ||
                blp.PreferredFormat == Formats.BLP.PixelFormat.Dxt3 ||
                blp.PreferredFormat == Formats.BLP.PixelFormat.Dxt5)
            {
                var compressedFormat = blp.PreferredFormat switch
                {
                    Formats.BLP.PixelFormat.Dxt1 => Format.FormatBC1Unorm,
                    Formats.BLP.PixelFormat.Dxt3 => Format.FormatBC2Unorm,
                    _ => Format.FormatBC3Unorm
                };
                var mipmaps = new List<MipLevel>((int)blp.MipCount);

                for (uint i = 0; i < blp.MipCount; i++)
                {
                    var width = (int)blp.MipWidth(i);
                    var height = (int)blp.MipHeight(i);
                    if (width == 0 || height == 0)
                        break;

                    mipmaps.Add(new MipLevel
                    {
                        Data = blp.Mip(i),
                        Width = width,
                        Height = height,
                        Level = (int)i
                    });
                }

                return new DecodedBLP
                {
                    FileDataId = fileDataId,
                    IsCompressed = true,
                    CompressedFormat = compressedFormat,
                    MipLevels = mipmaps
                };
            }

            using var image = blp.Decode(0);
            return new DecodedBLP
            {
                FileDataId = fileDataId,
                PixelData = image.Pixels.AsSpan().ToArray(),
                Width = (int)image.Width,
                Height = (int)image.Height,
                IsCompressed = false
            };
        }

        public static ComPtr<ID3D11ShaderResourceView> GetCurrent(uint fileDataId, ComPtr<ID3D11ShaderResourceView> fallback)
        {
            if (Cache.TryGetValue(fileDataId, out var srv))
                return srv;

            if (pendingTexture.HasValue)
                return pendingTexture.Value;

            return fallback;
        }

        public static int Upload(
            Stopwatch queueTimer,
            double budgetMilliseconds = 10d,
            int maxItems = int.MaxValue)
        {
            if (!cachedDevice.HasValue)
                return 0;

            var uploaded = 0;
            var processed = 0;
            while (processed < maxItems && queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds)
            {
                if (!loadQueue.TryDequeue(out var item))
                    break;

                if (item.IsSkipped)
                {
                    CompleteSkippedRequest(item.Request);
                    continue;
                }

                processed++;

                if (item.Error != null)
                {
                    Console.WriteLine($"Failed to decode BLP {item.Request}: {item.Error.Message}");
                    inFlight.TryRemove(item.Request, out _);
                    continue;
                }

                var decoded = item.Value;

                // A tile may have been evicted while the worker was decoding
                // its textures.  Do not submit GPU work for an asset that no
                // longer has users; it will be requested again if needed.
                if (ShouldDiscardDecodedTexture(
                        Users.ContainsKey(decoded.FileDataId),
                        Cache.ContainsKey(decoded.FileDataId)))
                {
                    inFlight.TryRemove(decoded.FileDataId, out _);
                    continue;
                }

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

                inFlight.TryRemove(decoded.FileDataId, out _);
            }

            return uploaded;
        }

        public static void StopWorker()
        {
            loadQueue.StopAsync().GetAwaiter().GetResult();
        }

        public static Task StopWorkerAsync() => loadQueue.StopAsync();

        public static int GetQueueCount()
        {
            return loadQueue.Count;
        }

        internal static AssetPipelineMetrics GetQueueMetrics() => loadQueue.Metrics;

        private static void CompleteSkippedRequest(uint fileDataId)
        {
            inFlight.TryRemove(fileDataId, out _);
            if (Users.ContainsKey(fileDataId) &&
                !Cache.ContainsKey(fileDataId) &&
                inFlight.TryAdd(fileDataId, 0))
            {
                loadQueue.Enqueue(fileDataId);
            }
        }

        internal static bool ShouldDiscardDecodedTexture(bool hasUsers, bool hasCachedTexture) =>
            !hasUsers || hasCachedTexture;

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
            if (pendingTexture.HasValue)
            {
                pendingTexture.Value.Dispose();
                pendingTexture = null;
            }
            cachedDevice = null;
        }
    }
}
