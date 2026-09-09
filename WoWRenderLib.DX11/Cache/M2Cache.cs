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
    public static class M2Cache
    {
        private static readonly Dictionary<uint, ParsedDoodadBatch> Cache = [];
        private static readonly ConcurrentDictionary<uint, List<uint>> Users = [];
        private static readonly ResourceFailureTracker<uint> failures = new();

        private static ComPtr<ID3D11Device>? cachedDevice = null;

        private static readonly HashSet<uint> inFlight = [];
        private static readonly BackgroundResourceQueue<uint, ParsedM2> loadQueue =
            new(
                WoWRenderLib.Loaders.M2Loader.ParseM2,
                shouldProcess: fileDataId => Users.ContainsKey(fileDataId));

        public static ParsedDoodadBatch GetOrLoad(ComPtr<ID3D11Device> device, uint fileDataId, uint parent, bool keepTrack = true)
        {
            cachedDevice ??= device;

            if (keepTrack)
            {
                if (Users.TryGetValue(fileDataId, out var users))
                    users.Add(parent);
                else
                    Users.TryAdd(fileDataId, [parent]);
            }

            if (Cache.TryGetValue(fileDataId, out ParsedDoodadBatch value))
                return value;

            // Do not parse/upload a complete placeholder for every pending M2.
            // The renderer already treats fileDataID == 0 as not ready, so an
            // empty value avoids doing synchronous GPU work while the real M2
            // is parsed by the worker.
            var placeholder = CreatePlaceholder();

            Cache.Add(fileDataId, placeholder);

            if (inFlight.Contains(fileDataId))
                return placeholder;

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

            return placeholder;
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
                    Console.WriteLine($"!!! Error parsing M2 {originalFileDataId}: {item.Error.Message}");
                    if (failures.TryScheduleRetry(
                            originalFileDataId,
                            Users.ContainsKey(originalFileDataId),
                            loadQueue.Enqueue))
                        continue;
                    inFlight.Remove(originalFileDataId);
                    if (ResourceCachePolicy.ShouldRemovePlaceholderAfterFailure(
                            Users.ContainsKey(originalFileDataId)))
                    {
                        Cache.Remove(originalFileDataId);
                    }
                    continue;
                }

                var parsedM2 = item.Value;

                if (!Cache.TryGetValue(originalFileDataId, out var oldBatch))
                {
                    inFlight.Remove(originalFileDataId);
                    // A tile can be evicted while its M2 is being parsed. Do
                    // not let that stale item block all later uploads.
                    continue;
                }

                try
                {
                    var newBatch = M2Loader.LoadM2(cachedDevice.Value, parsedM2);
                    Cache[originalFileDataId] = newBatch;
                    failures.Succeeded(originalFileDataId);
                    uploaded++;

                    unsafe
                    {
                        if (oldBatch.vertexBuffer.Handle != null)
                            M2Loader.UnloadM2(oldBatch);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"!!! Error uploading M2 {originalFileDataId}: {e.Message}");
                    if (failures.TryScheduleRetry(
                            originalFileDataId,
                            Users.ContainsKey(originalFileDataId),
                            loadQueue.Enqueue))
                        continue;
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

        public static int GetLoadQueueCount() => loadQueue.Count;

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
                    failures.Forget(fileDataId);
                    if (Cache.TryGetValue(fileDataId, out var model))
                    {
                        Cache.Remove(fileDataId);
                        M2Loader.UnloadM2(model);
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

        private static ParsedDoodadBatch CreatePlaceholder() => new()
        {
            submeshes = [],
            mats = [],
            geosets = []
        };

        public static void CheckUsers()
        {
            var m2sToRemove = new List<uint>();
            foreach (var cachedM2 in Cache.Keys)
                if (!Users.ContainsKey(cachedM2))
                    m2sToRemove.Add(cachedM2);

            foreach (var m2Id in m2sToRemove)
            {
                if (Cache.TryGetValue(m2Id, out var m2))
                {
                    Cache.Remove(m2Id);
                    M2Loader.UnloadM2(m2);
                }
            }
        }

        public static void ReleaseAll()
        {
            Debug.WriteLine("Releasing " + Cache.Count + " cached M2s.");

            foreach (var key in Cache.Keys)
                if (Cache.TryGetValue(key, out var model))
                    M2Loader.UnloadM2(model);

            Cache.Clear();
            Users.Clear();
            inFlight.Clear();
            failures.Clear();
            cachedDevice = null;
        }
    }
}
