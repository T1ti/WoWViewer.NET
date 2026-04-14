using Silk.NET.OpenGL;
using System.Diagnostics;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Cache
{
    public static class WMOCache
    {
        private static readonly Dictionary<uint, WorldModel> Cache = [];

        private static readonly Dictionary<uint, List<uint>> Users = [];

        private static GL? cachedGL = null;

        private static readonly HashSet<uint> inFlight = [];

        private static readonly Lock queueLock = new();
        private static readonly Queue<uint> parseQueue = [];
        private static readonly Queue<(uint originalFileDataId, PreppedWMO preppedWMO)> uploadQueue = [];

        private static CancellationTokenSource? workerCancellation;
        private static Task? workerTask;

        public static WorldModel GetOrLoad(GL gl, uint fileDataId, uint shaderProgram, uint parent)
        {
            if (cachedGL == null)
                cachedGL = gl;

            StartWorker();

            if (Users.TryGetValue(fileDataId, out var users))
                users.Add(parent);
            else
                Users.Add(fileDataId, [parent]);

            if (Cache.TryGetValue(fileDataId, out WorldModel value))
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

            Cache.Add(fileDataId, placeholderWMO);

            lock (queueLock)
            {
                if (inFlight.Contains(fileDataId))
                    return placeholderWMO;

                inFlight.Add(fileDataId);
                parseQueue.Enqueue(fileDataId);

                return placeholderWMO;
            }
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
                uint fileDataId = 0;
                bool hasWork = false;

                lock (queueLock)
                {
                    if (parseQueue.TryDequeue(out fileDataId))
                        hasWork = true;
                }

                if (!hasWork)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                try
                {
                    var preppedWMO = WMOLoader.ParseWMO(fileDataId);

                    lock (queueLock)
                        uploadQueue.Enqueue((fileDataId, preppedWMO));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"!!! Error parsing WMO {fileDataId}: {e.Message}");

                    // Remove from in-flight set so it's not stuck in limbo
                    lock (queueLock)
                        inFlight.Remove(fileDataId);
                }
            }
        }

        public static void Upload(uint shaderProgram)
        {
            if (cachedGL == null)
                return;

            const int maxUploadsPerFrame = 2;
            int uploaded = 0;

            while (uploaded < maxUploadsPerFrame)
            {
                uint originalFileDataId;
                PreppedWMO preppedWMO;

                lock (queueLock)
                {
                    if (!uploadQueue.TryDequeue(out var item))
                        break;

                    (originalFileDataId, preppedWMO) = item;
                }

                if (!Cache.TryGetValue(originalFileDataId, out var oldWMO))
                    continue;

                try
                {
                    var newWMO = WMOLoader.LoadWMO(preppedWMO, cachedGL, shaderProgram);
                    Cache[originalFileDataId] = newWMO;

                    if (oldWMO.groupBatches != null && oldWMO.groupBatches.Length > 0)
                        WMOLoader.UnloadWMO(cachedGL, oldWMO);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"!!! Error uploading WMO {originalFileDataId}: {e.Message}");
                }

                lock (queueLock)
                    inFlight.Remove(originalFileDataId);

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

        public static int GetLoadQueueCount()
        {
            lock (queueLock)
                return parseQueue.Count + uploadQueue.Count;
        }

        public static void Release(GL gl, uint fileDataId, uint parent)
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
                        WMOLoader.UnloadWMO(gl, wmo);
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

        public static void ReleaseAll(GL gl)
        {
            Debug.WriteLine("Releasing " + Cache.Count + " cached WMOs.");

            foreach (var key in Cache.Keys)
                if (Cache.TryGetValue(key, out var wmo))
                    WMOLoader.UnloadWMO(gl, wmo);

            Cache.Clear();
            Users.Clear();
        }
    }
}
