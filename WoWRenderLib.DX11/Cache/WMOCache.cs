using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Collections.Concurrent;
using System.Diagnostics;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Cache
{
    public static class WMOCache
    {
        private static readonly Dictionary<uint, WorldModel> Cache = [];

        private static readonly Dictionary<uint, List<uint>> Users = [];

        private static ComPtr<ID3D11Device>? cachedDevice = null;

        private static readonly HashSet<uint> inFlight = [];

        private static readonly ConcurrentQueue<uint> parseQueue = [];
        private static readonly ConcurrentQueue<(uint originalFileDataId, PreppedWMO preppedWMO)> uploadQueue = [];

        private static CancellationTokenSource? workerCancellation;
        private static Task? workerTask;

        public static WorldModel GetOrLoad(ComPtr<ID3D11Device> device, uint fileDataId, uint parent, bool keepTrack = true)
        {
            cachedDevice ??= device;

            StartWorker();

            if (keepTrack)
            {
                if (Users.TryGetValue(fileDataId, out var users))
                    users.Add(parent);
                else
                    Users.Add(fileDataId, [parent]);
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
            parseQueue.Enqueue(fileDataId);

            return placeholderWMO;
        }

        private static void StartWorker()
        {
            if (workerTask != null)
                return;

            workerCancellation = new CancellationTokenSource();
            workerTask = Task.Run(() => ParseWorker(workerCancellation.Token), workerCancellation.Token);
        }

        private static async Task ParseWorker(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool hasWork = false;

                if (parseQueue.TryDequeue(out var fileDataId))
                    hasWork = true;

                if (!hasWork)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                try
                {
                    var preppedWMO = WoWRenderLib.Loaders.WMOLoader.ParseWMO(fileDataId);
                    uploadQueue.Enqueue((fileDataId, preppedWMO));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"!!! Error parsing WMO {fileDataId}: {e.Message}");

                    // Remove from in-flight set so it's not stuck in limbo
                    inFlight.Remove(fileDataId);
                }
            }
        }

        public static int Upload(Stopwatch queueTimer)
        {
            if (cachedDevice == null)
                return 0;

            var uploaded = 0;
            while (queueTimer.ElapsedMilliseconds < 5)
            {
                if (!uploadQueue.TryDequeue(out var item))
                    return uploaded;

                uint originalFileDataId;
                PreppedWMO preppedWMO;

                (originalFileDataId, preppedWMO) = item;

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
                }

                inFlight.Remove(originalFileDataId);
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

        public static int GetLoadQueueCount()
        {
            return parseQueue.Count + uploadQueue.Count;
        }

        public static void Release(uint fileDataId, uint parent)
        {
            if (Users.TryGetValue(fileDataId, out var users))
            {
                users.Remove(parent);

                if (users.Count == 0)
                {
                    Users.Remove(fileDataId);
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
            while (parseQueue.TryDequeue(out _)) { }
            while (uploadQueue.TryDequeue(out _)) { }
            cachedDevice = null;
        }
    }
}
