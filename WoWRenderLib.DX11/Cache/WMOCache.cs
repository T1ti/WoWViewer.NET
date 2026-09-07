using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Collections.Concurrent;
using System.Diagnostics;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Streaming;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Cache
{
    public static class WMOCache
    {
        private static readonly Dictionary<uint, WorldModel> Cache = [];

        private static readonly ConcurrentDictionary<uint, List<uint>> Users = [];

        private static ComPtr<ID3D11Device>? cachedDevice = null;

        private static readonly HashSet<uint> inFlight = [];
        private static readonly BackgroundResourceQueue<uint, PreppedWMO> loadQueue =
            new(
                fileDataId => WoWRenderLib.Loaders.WMOLoader.ParseWMO(fileDataId),
                shouldProcess: fileDataId => Users.ContainsKey(fileDataId));

        public static WorldModel GetOrLoad(ComPtr<ID3D11Device> device, uint fileDataId, uint parent, bool keepTrack = true)
        {
            cachedDevice ??= device;

            if (keepTrack)
            {
                if (Users.TryGetValue(fileDataId, out var users))
                    users.Add(parent);
                else
                    Users.TryAdd(fileDataId, [parent]);
            }

            if (Cache.TryGetValue(fileDataId, out WorldModel value))
                return value;

            // Pending WMOs are not rendered until IsLoaded becomes true. Keep
            // the placeholder allocation-only; parsing and uploading the full
            // missing-WMO asset here used to block the render thread once per
            // placement.
            var placeholderWMO = CreatePlaceholder();

            Cache.Add(fileDataId, placeholderWMO);

            if (inFlight.Contains(fileDataId))
                return placeholderWMO;

            inFlight.Add(fileDataId);
            try
            {
                loadQueue.Enqueue(fileDataId);
            }
            catch
            {
                inFlight.Remove(fileDataId);
                Cache.Remove(fileDataId);
                throw;
            }

            return placeholderWMO;
        }

        public static int Upload(
            Stopwatch queueTimer,
            double budgetMilliseconds = 10d,
            int maxItems = int.MaxValue)
        {
            if (cachedDevice == null)
                return 0;

            var uploaded = 0;
            var processed = 0;
            while (processed < maxItems && queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds)
            {
                if (!loadQueue.TryDequeue(out var item))
                    return uploaded;

                if (item.IsSkipped)
                {
                    CompleteSkippedRequest(item.Request);
                    continue;
                }

                processed++;

                var originalFileDataId = item.Request;
                if (item.Error != null)
                {
                    Console.WriteLine($"!!! Error parsing WMO {originalFileDataId}: {item.Error.Message}");
                    inFlight.Remove(originalFileDataId);
                    // Memoize a terminal failure with the existing placeholder
                    // until its last owner releases it. Otherwise every lookup
                    // immediately queues the same invalid resource again.
                    if (ResourceCachePolicy.ShouldRemovePlaceholderAfterFailure(
                            Users.ContainsKey(originalFileDataId)))
                    {
                        Cache.Remove(originalFileDataId);
                    }
                    continue;
                }

                var preppedWMO = item.Value;

                if (!Cache.TryGetValue(originalFileDataId, out var oldWMO))
                {
                    inFlight.Remove(originalFileDataId);
                    // A tile may have been evicted while this item was being
                    // parsed. Skip it and continue with the remaining queue.
                    continue;
                }

                try
                {
                    var newWMO = WMOLoader.LoadWMO(preppedWMO, cachedDevice.Value);
                    Cache[originalFileDataId] = newWMO;
                    uploaded++;

                    if (oldWMO.groupBatches != null && oldWMO.groupBatches.Length > 0)
                        WMOLoader.UnloadWMO(oldWMO);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"!!! Error uploading WMO {originalFileDataId}: {e.Message}");
                    if (ResourceCachePolicy.ShouldRemovePlaceholderAfterFailure(
                            Users.ContainsKey(originalFileDataId)))
                    {
                        Cache.Remove(originalFileDataId);
                    }
                }

                inFlight.Remove(originalFileDataId);
            }

            return uploaded;
        }

        public static void StopWorker()
        {
            loadQueue.StopAsync().GetAwaiter().GetResult();
        }

        public static Task StopWorkerAsync() => loadQueue.StopAsync();

        public static int GetLoadQueueCount()
        {
            return loadQueue.Count;
        }

        internal static AssetPipelineMetrics GetQueueMetrics() => loadQueue.Metrics;

        private static void CompleteSkippedRequest(uint fileDataId)
        {
            inFlight.Remove(fileDataId);
            if (Users.ContainsKey(fileDataId))
            {
                inFlight.Add(fileDataId);
                loadQueue.Enqueue(fileDataId);
            }
            else
            {
                Cache.Remove(fileDataId);
            }
        }

        public static void Release(uint fileDataId, uint parent)
        {
            if (Users.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);

                if (users.Count == 0)
                {
                    Users.TryRemove(fileDataId, out _);
                    if (Cache.TryGetValue(fileDataId, out var wmo))
                    {
                        Cache.Remove(fileDataId);
                        WMOLoader.UnloadWMO(wmo);
                    }
                }
                else
                {
                    Users[fileDataId] = users;
                }
            }
        }

        public static void CheckUsers()
        {
            var wmosToRemove = new List<uint>();
            foreach (var cachedWMO in Cache.Keys)
                if (!Users.ContainsKey(cachedWMO))
                    wmosToRemove.Add(cachedWMO);

            foreach (var wmoId in wmosToRemove)
            {
                if (Cache.TryGetValue(wmoId, out var wmo))
                {
                    Cache.Remove(wmoId);
                    WMOLoader.UnloadWMO(wmo);
                }
            }
        }

        public static int GetCacheCount()
        {
            return Cache.Count;
        }

        private static WorldModel CreatePlaceholder() => new()
        {
            groupBatches = [],
            preppedMats = [],
            textureReferences = [],
            wmoRenderBatches = [],
            doodads = [],
            doodadSets = [],
            portals = [],
            doodadsReferencedByGroups = []
        };

        public static void ReleaseAll()
        {
            Debug.WriteLine("Releasing " + Cache.Count + " cached WMOs.");

            foreach (var key in Cache.Keys)
                if (Cache.TryGetValue(key, out var wmo))
                    WMOLoader.UnloadWMO(wmo);

            Cache.Clear();
            Users.Clear();
            inFlight.Clear();
            cachedDevice = null;
        }
    }
}
