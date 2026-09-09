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
    public static class ADTCache
    {
        internal const int MaxRequestsInFlight = 16;

        private static readonly ConcurrentDictionary<MapTile, Terrain> Cache = [];
        private static readonly ConcurrentDictionary<MapTile, List<uint>> Users = [];
        private static readonly ConcurrentDictionary<MapTile, ADTCallbacks> Callbacks = [];
        private static readonly ResourceFailureTracker<MapTile> failures = new();

        private static readonly Lock inFlightLock = new();
        private static readonly HashSet<MapTile> inFlight = [];
        private static readonly BackgroundResourceQueue<MapTile, ParsedADT> loadQueue = new(
            WoWRenderLib.Loaders.ADTLoader.ParseADT,
            bufferedResultCount: 4,
            bufferedRequestCount: MaxRequestsInFlight,
            shouldProcess: mapTile => Users.ContainsKey(mapTile));

        private readonly record struct ADTCallbacks(
            Action<Terrain>? Loaded,
            Action<Exception>? Failed);

        public static Terrain GetOrLoad(
            MapTile mapTile,
            uint parent,
            Action<Terrain>? onLoaded = null,
            Action<Exception>? onFailed = null,
            bool keepTrack = true)
        {
            if (keepTrack)
            {
                if (Users.TryGetValue(mapTile, out var users))
                    users.Add(parent);
                else
                    Users.TryAdd(mapTile, [parent]);
            }

            lock (inFlightLock)
            {
                if (inFlight.Contains(mapTile))
                {
                    // A tile can leave and re-enter the desired set while its
                    // parse is still running. Attach the new owner before
                    // returning the existing placeholder.
                    if (onLoaded != null || onFailed != null)
                        Callbacks[mapTile] = new ADTCallbacks(onLoaded, onFailed);
                    return Cache[mapTile];
                }
            }

            if (Cache.TryGetValue(mapTile, out Terrain value))
            {
                // return immediately if already loaded
                if (value.renderBatches != null)
                    onLoaded?.Invoke(value);

                return value;
            }

            // TODO: LOD ADT? Better placeholder? Do in ADT container?
            Cache.TryAdd(mapTile, new Terrain());

            // onLoaded here is the callback to the ADT container to fire for when its loaded
            if (onLoaded != null || onFailed != null)
                Callbacks[mapTile] = new ADTCallbacks(onLoaded, onFailed);

            lock(inFlightLock)
                inFlight.Add(mapTile);
            
            try
            {
                loadQueue.Enqueue(mapTile);
            }
            catch
            {
                RollBackRequest(mapTile, parent, keepTrack);
                throw;
            }

            return Cache[mapTile];
        }

        private static void RollBackRequest(MapTile mapTile, uint parent, bool keepTrack)
        {
            lock (inFlightLock)
                inFlight.Remove(mapTile);

            Callbacks.TryRemove(mapTile, out _);
            if (keepTrack && Users.TryGetValue(mapTile, out var users))
            {
                users.Remove(parent);
                if (users.Count == 0)
                    Users.TryRemove(mapTile, out _);
            }

            if (!Users.ContainsKey(mapTile))
                Cache.TryRemove(mapTile, out _);
        }

        private static void CompleteSkippedRequest(MapTile mapTile)
        {
            lock (inFlightLock)
                inFlight.Remove(mapTile);

            // Re-entry may occur after the worker decided this request was
            // stale but before the render thread observes that decision.
            if (Users.ContainsKey(mapTile))
            {
                lock (inFlightLock)
                    inFlight.Add(mapTile);
                loadQueue.Enqueue(mapTile);
                return;
            }

            Callbacks.TryRemove(mapTile, out _);
            Cache.TryRemove(mapTile, out _);
        }

        public static int Upload(
            Stopwatch queueTimer,
            ComPtr<ID3D11Device> device,
            double budgetMilliseconds = 10d,
            int maxItems = int.MaxValue)
        {
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

                var key = item.Request;
                if (item.Error != null)
                {
                    if (failures.TryScheduleRetry(
                            key,
                            Users.ContainsKey(key),
                            loadQueue.Enqueue))
                        continue;

                    lock (inFlightLock)
                        inFlight.Remove(key);

                    if (Callbacks.TryRemove(key, out var failedCallbacks))
                        failedCallbacks.Failed?.Invoke(item.Error);

                    if (!Users.ContainsKey(key))
                        Cache.TryRemove(key, out _);
                    continue;
                }

                var parsedADT = item.Value;

                if (!Cache.TryGetValue(key, out var oldTerrain))
                {
                    lock(inFlightLock)
                        inFlight.Remove(key);
                    
                    Callbacks.TryRemove(key, out _);
                    continue;
                }

                // The tile may have been evicted after parsing started. Do
                // not create GPU buffers for a tile that no scene object owns
                // anymore. A pending placeholder is retained by Release so a
                // tile that re-enters the view can reuse this parse result.
                if (ShouldDiscardParsedTile(Users.ContainsKey(key)))
                {
                    if (Cache.TryRemove(key, out var staleTerrain) && staleTerrain.renderBatches != null)
                        ADTLoader.UnloadTerrain(staleTerrain);

                    lock (inFlightLock)
                        inFlight.Remove(key);

                    Callbacks.TryRemove(key, out _);
                    continue;
                }

                try
                {
                    var newTerrain = ADTLoader.LoadADT(device, parsedADT);
                    Cache[key] = newTerrain;
                    failures.Succeeded(key);
                    uploaded++;

                    if (Callbacks.Remove(key, out var callbacks))
                        callbacks.Loaded?.Invoke(newTerrain);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to upload ADT {parsedADT.rootADTFileDataID}: {e.Message}");
                    if (failures.TryScheduleRetry(
                            key,
                            Users.ContainsKey(key),
                            loadQueue.Enqueue))
                        continue;

                    lock (inFlightLock)
                        inFlight.Remove(key);

                    if (Callbacks.TryRemove(key, out var failedCallbacks))
                        failedCallbacks.Failed?.Invoke(e);

                    if (!Users.ContainsKey(key))
                        Cache.TryRemove(key, out _);
                    continue;
                }

                lock(inFlightLock)
                    inFlight.Remove(key);

            }

            return uploaded;
        }

        public static void StopWorker()
        {
            StopWorkerAsync().GetAwaiter().GetResult();
        }

        public static Task StopWorkerAsync() => loadQueue.StopAsync();

        public static int GetLoadQueueCount() => loadQueue.Count;

        internal static AssetPipelineMetrics GetQueueMetrics() => loadQueue.Metrics;

        public static void Release(MapTile mapTile, uint parent)
        {
            if (Users.TryGetValue(mapTile, out var users))
            {
                users.Remove(parent);
                if (users.Count == 0)
                {
                    Users.TryRemove(mapTile, out _);
                    failures.Forget(mapTile);
                    Callbacks.TryRemove(mapTile, out _);

                    bool isPending;
                    lock (inFlightLock)
                        isPending = inFlight.Contains(mapTile);

                    // Keep an in-flight placeholder alive until its parsed
                    // result reaches Upload. This avoids a key-not-found race
                    // when the tile is requested again before the worker
                    // finishes, while Upload will discard it if it remains
                    // unused.
                    if (!isPending && Cache.TryRemove(mapTile, out var terrain))
                        ADTLoader.UnloadTerrain(terrain);
                }
            }
        }

        internal static bool ShouldDiscardParsedTile(bool hasUsers) => !hasUsers;

        public static int GetCacheCount() => Cache.Count;

        public static void ReleaseAll()
        {
            Debug.WriteLine("Releasing " + Cache.Count + " cached ADTs.");

            StopWorker();

            foreach (var key in Cache.Keys)
                if (Cache.TryGetValue(key, out var terrain))
                    ADTLoader.UnloadTerrain(terrain);

            Callbacks.Clear();
            Users.Clear();
            Cache.Clear();
            failures.Clear();
            lock (inFlightLock)
                inFlight.Clear();
        }
    }
}
