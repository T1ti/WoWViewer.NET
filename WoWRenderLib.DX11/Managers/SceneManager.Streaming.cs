using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MapObjDefFlags = WoWLib.Formats.Common.MapObjDefFlags;
using WoWRenderLib.Cache;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Profiling;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Streaming;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Persistence;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Renderer;
using WoWRenderLib.Structs;
using WoWRenderLib.Diagnostics;
using WoWRenderLib.Services;
using WoWRenderLib.Loaders;

namespace WoWRenderLib.DX11.Managers
{
    public partial class SceneManager
    {
        public void LoadWDT(uint wdtFileDataID)
        {
            if (CurrentWDTFileDataID != wdtFileDataID)
            {
                loadedTiles.Clear();
                tilesToLoad.Clear();
                tilesQueuedForLoad.Clear();
                tilesInFlight.Clear();
                desiredTiles.Clear();
                failedDesiredTiles.Clear();
                availableWdtTiles.Clear();

                foreach (var bounds in tileSceneBounds.Values)
                    bounds.Dispose();
                tileSceneBounds.Clear();
                tileSceneBoundsByRoot.Clear();

                CompletePendingTileUnloads();
                lock (SceneObjectLock)
                {
                    foreach (var adt in adtContainers)
                    {
                        adt.LoadCallback -= OnADTContainerLoaded;
                        adt.LoadFailedCallback -= OnADTContainerLoadFailed;
                        adt.Unload();
                    }
                    foreach (var wmo in SceneObjects.OfType<WMOContainer>())
                        WMOCache.Release(wmo.FileDataId, wmo.ParentFileDataId);
                    foreach (var m2 in SceneObjects.OfType<M2Container>())
                        M2Cache.Release(m2.FileDataId, m2.ParentFileDataId);
                    SceneObjects.Clear();
                    adtContainers.Clear();
                }
                pendingAdtPopulations.Clear();
                pendingWMODoodads.Clear();
                uuidUsers.Clear();
                wmoInstances.Clear();
                m2Instances.Clear();
                m2InstancePackets.Clear();
                SelectedObject = null;

                CurrentWDTFileDataID = wdtFileDataID;
                currentWDT = WDTCache.GetOrLoad(CurrentWDTFileDataID);
                UpdateMapHighestUniqueId();
                RebuildAvailableTileIndex();
                SpawnGlobalWmo();
                WMOCache.CheckUsers();
                M2Cache.CheckUsers();
                BLPCache.CheckUsers();
            }
        }

        private void SpawnGlobalWmo()
        {
            if (currentWDT?.GlobalWmoPlacement is not { FileDataId: not 0 } placement)
                return;

            var container = new WMOContainer(_device, placement.FileDataId, CurrentWDTFileDataID)
            {
                Position = placement.Position,
                Rotation = placement.Rotation,
                Scale = placement.Scale,
                UniqueID = placement.UniqueId,
                PlacementFlags = placement.Flags,
                PlacementDoodadSet = placement.DoodadSet,
                PlacementNameSet = placement.NameSet,
                OnDoodadSetsChanged = RefreshWMODoodads,
                OnGroupsChanged = _ => UpdateWMOInstanceList()
            };
            container.SetDoodadSetsToEnable([placement.DoodadSet]);
            lock (SceneObjectLock)
                SceneObjects.Add(container);
            pendingWMODoodads.Enqueue(new PendingWmoDoodadPopulation(container));
            UpdateWMOInstanceList();
        }

        public void PreloadTEX()
        {
            if (currentWDT == null)
                return;

            var texFileDataID = currentWDT.TexFileDataId;
            if (texFileDataID != 0)
                TEXCache.Preload(texFileDataID);
        }

        public WdtFile? GetCurrentWDT()
        {
            if (currentWDT == null)
            {
                currentWDT = WDTCache.GetOrLoad(CurrentWDTFileDataID);
                UpdateMapHighestUniqueId();
                RebuildAvailableTileIndex();
                SpawnGlobalWmo();
            }
            return currentWDT;
        }

        private void UpdateMapHighestUniqueId()
        {
            if (currentWDT != null)
                CurrentMapHighestUniqueId = WowlibFileSystem.Current.Kind == WoWLib.StorageKind.Mpq
                    ? MapUniqueIdScanner.ScanMap(currentWDT)
                    : MapUniqueIdStore.GetOrScan(CurrentWDTFileDataID, currentWDT);
        }

        private void RebuildAvailableTileIndex()
        {
            availableWdtTiles.Clear();
            if (currentWDT == null)
                return;

            foreach (var tile in currentWDT.Tiles)
                availableWdtTiles.Add((tile.tileX, tile.tileY));
        }

        public (byte x, byte y) GetFirstMapTile()
        {
            if (currentWDT == null || currentWDT.Tiles.Count == 0)
                return (0, 0);

            var tile = currentWDT.Tiles[0];
            return (tile.tileX, tile.tileY);
        }

        public void UpdateTilesByCameraPos(Vector3 cameraPosition)
        {
            if (currentWDT == null)
                return;

            var (x, y) = GetTileFromPosition(cameraPosition);

            var orderedDesiredTiles = TileStreamingPolicy.BuildDesiredTiles(
                CurrentWDTFileDataID,
                x,
                y,
                TileLoadingDistance,
                availableWdtTiles);
            var nextDesiredTiles = orderedDesiredTiles.ToHashSet();
            var desiredTilesChanged = !desiredTiles.SetEquals(nextDesiredTiles);
            failedDesiredTiles.RemoveWhere(tile => !nextDesiredTiles.Contains(tile));

            desiredTiles.Clear();
            desiredTiles.UnionWith(nextDesiredTiles);

            if (desiredTilesChanged)
            {
                RebuildPendingLoadQueue(orderedDesiredTiles);
            }
            else
            {
                foreach (var mapTile in orderedDesiredTiles)
                    QueueTileForLoadIfNeeded(mapTile);
            }

            foreach (var adt in adtContainers)
            {
                if (desiredTiles.Contains(adt.mapTile) || adt.IsModified)
                    adt.CancelUnload();
                else
                    adt.ScheduleUnload(streamingClock);
            }
        }

        private void QueueTileForLoadIfNeeded(MapTile mapTile)
        {
            if (!loadedTiles.Contains(mapTile) &&
                !tilesQueuedForLoad.Contains(mapTile) &&
                !tilesInFlight.Contains(mapTile) &&
                !failedDesiredTiles.Contains(mapTile))
            {
                tilesToLoad.Enqueue(mapTile);
                tilesQueuedForLoad.Add(mapTile);
            }
        }

        private void RebuildPendingLoadQueue(IReadOnlyList<MapTile> orderedDesiredTiles)
        {
            tilesToLoad.Clear();
            tilesQueuedForLoad.Clear();
            foreach (var tile in orderedDesiredTiles)
                QueueTileForLoadIfNeeded(tile);
        }

        public void ProcessUnloadQueue()
        {
            var unloadTimer = Stopwatch.StartNew();
            ProcessUnloadQueue(unloadTimer, 10d);
        }

        private void ProcessUnloadQueue(Stopwatch queueTimer, double budgetMilliseconds)
        {
            if (queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds)
            {
                var adtToRemove = adtContainers.FirstOrDefault(adt =>
                    adt.IsUnloadDue(streamingClock, TileUnloadDelay));
                if (adtToRemove != null)
                {
                    if (adtToRemove.IsModified)
                    {
                        adtToRemove.CancelUnload();
                    }
                    else
                    {
                        BeginTileUnload(adtToRemove);
                    }
                }
            }

            ProcessPendingTileUnloads(queueTimer, budgetMilliseconds);
        }

        private void BeginTileUnload(ADTContainer container)
        {
            var tile = container.mapTile;
            tilesInFlight.Remove(tile);
            loadedTiles.Remove(tile);
            container.LoadCallback -= OnADTContainerLoaded;
            container.LoadFailedCallback -= OnADTContainerLoadFailed;

            lock (SceneObjectLock)
            {
                SceneObjects.Remove(container);
                adtContainers.Remove(container);
            }

            if (!container.IsLoaded)
            {
                container.Unload();
                return;
            }

            var rootId = container.Terrain.rootADTFileDataID;
            RemovePendingAdtPopulation(tile);
            RemovePendingWmosForTile(rootId);
            var worldModels = new List<WMOContainer>();
            var doodads = new List<M2Container>();
            lock (SceneObjectLock)
            {
                for (var index = SceneObjects.Count - 1; index >= 0; index--)
                {
                    switch (SceneObjects[index])
                    {
                        case WMOContainer worldModel when worldModel.ParentFileDataId == rootId:
                            worldModels.Add(worldModel);
                            SceneObjects.RemoveAt(index);
                            break;
                        case M2Container doodad when doodad.ParentFileDataId == rootId:
                            doodads.Add(doodad);
                            SceneObjects.RemoveAt(index);
                            break;
                    }
                }
            }
            foreach (var worldModel in worldModels)
                uuidUsers.Remove(worldModel.UniqueID);
            HideTileInstances(rootId);

            if (tileSceneBounds.Remove(GetTileBoundsKey(tile), out var bounds))
            {
                tileSceneBoundsByRoot.Remove(rootId);
                bounds.Dispose();
            }

            pendingTileUnloads.Enqueue(new PendingTileUnload(
                container,
                worldModels,
                doodads));
        }

        private void HideTileInstances(uint rootFileDataId)
        {
            foreach (var key in wmoInstances.Keys.ToArray())
            {
                var instances = wmoInstances[key];
                instances.RemoveAll(instance => instance.ParentFileDataId == rootFileDataId);
                if (instances.Count == 0)
                    wmoInstances.Remove(key);
            }

            foreach (var fileDataId in m2Instances.Keys.ToArray())
            {
                var instances = m2Instances[fileDataId];
                instances.RemoveAll(instance => instance.ParentFileDataId == rootFileDataId);
                if (instances.Count == 0)
                {
                    m2Instances.Remove(fileDataId);
                    m2InstancePackets.Remove(fileDataId);
                }
                else if (m2InstancePackets.TryGetValue(fileDataId, out var packet))
                {
                    packet.Invalidate();
                }
            }
        }

        private void ProcessPendingTileUnloads(Stopwatch queueTimer, double budgetMilliseconds)
        {
            while (pendingTileUnloads.Count > 0 &&
                   queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds)
            {
                var pending = pendingTileUnloads.Peek();
                if (pending.NextWorldModel < pending.WorldModels.Count)
                {
                    ReleaseWorldModel(pending.WorldModels[pending.NextWorldModel++]);
                    continue;
                }

                if (pending.NextDoodad < pending.Doodads.Count)
                {
                    ReleaseDoodad(pending.Doodads[pending.NextDoodad++]);
                    continue;
                }

                pending.Container.Unload();
                pendingTileUnloads.Dequeue();
            }
        }

        private void CompletePendingTileUnloads()
        {
            while (pendingTileUnloads.TryDequeue(out var pending))
            {
                while (pending.NextWorldModel < pending.WorldModels.Count)
                    ReleaseWorldModel(pending.WorldModels[pending.NextWorldModel++]);
                while (pending.NextDoodad < pending.Doodads.Count)
                    ReleaseDoodad(pending.Doodads[pending.NextDoodad++]);
                pending.Container.Unload();
            }
        }

        private void ReleaseWorldModel(WMOContainer container)
        {
            container.ActiveDoodads.Clear();
            WMOCache.Release(container.FileDataId, container.ParentFileDataId);
        }

        private void ReleaseDoodad(M2Container container)
        {
            M2Cache.Release(container.FileDataId, container.ParentFileDataId);
        }

        private void RemovePendingWmosForTile(uint rootAdtFileDataId)
        {
            var remaining = pendingWMODoodads.Count;
            for (var index = 0; index < remaining; index++)
            {
                var pending = pendingWMODoodads.Dequeue();
                if (pending.Container.ParentFileDataId != rootAdtFileDataId)
                    pendingWMODoodads.Enqueue(pending);
            }
        }

        private void RemovePendingAdtPopulation(MapTile tile)
        {
            var remaining = pendingAdtPopulations.Count;
            for (var index = 0; index < remaining; index++)
            {
                var pending = pendingAdtPopulations.Dequeue();
                if (pending.Container.mapTile != tile)
                    pendingAdtPopulations.Enqueue(pending);
            }
        }

        public void UpdateM2InstanceList()
        {
            m2Instances.Clear();
            foreach (var sceneObject in SceneObjects)
            {
                if (sceneObject is M2Container m2)
                {
                    if (!m2Instances.ContainsKey(m2.FileDataId))
                        m2Instances[m2.FileDataId] = [];
                    m2Instances[m2.FileDataId].Add(m2);
                }
            }
            RebuildM2InstancePackets();
        }

        public void UpdateWMOInstanceList()
        {
            wmoInstances.Clear();
            foreach (var sceneObject in SceneObjects)
            {
                if (sceneObject is WMOContainer wmo)
                {
                    var key = (wmo.FileDataId, WMOContainer.CreateEnabledGroupSignature(wmo.EnabledGroups));
                    if (!wmoInstances.ContainsKey(key))
                        wmoInstances[key] = [];
                    wmoInstances[key].Add(wmo);
                }
            }
        }

        public void UpdateInstanceList()
        {
            wmoInstances.Clear();
            m2Instances.Clear();

            foreach (var sceneObject in SceneObjects)
            {
                if (sceneObject is WMOContainer wmo)
                {
                    var key = (wmo.FileDataId, WMOContainer.CreateEnabledGroupSignature(wmo.EnabledGroups));
                    if (!wmoInstances.ContainsKey(key))
                        wmoInstances[key] = [];

                    wmoInstances[key].Add(wmo);
                }
                else if (sceneObject is M2Container m2)
                {
                    if (!m2Instances.ContainsKey(m2.FileDataId))
                        m2Instances[m2.FileDataId] = [];

                    m2Instances[m2.FileDataId].Add(m2);
                }
            }
            RebuildM2InstancePackets();
        }

        private void RebuildM2InstancePackets()
        {
            m2InstancePackets.Clear();
            foreach (var (fileDataId, instances) in m2Instances)
                m2InstancePackets.Add(fileDataId, new M2InstancePacket(instances));
        }

        private void RegisterWmoInstance(WMOContainer container)
        {
            var key = (
                container.FileDataId,
                WMOContainer.CreateEnabledGroupSignature(container.EnabledGroups));
            if (!wmoInstances.TryGetValue(key, out var instances))
            {
                instances = [];
                wmoInstances.Add(key, instances);
            }

            instances.Add(container);
        }

        private void RegisterLoadedWmoInstance(WMOContainer container)
        {
            UnregisterWmoInstance(container);
            RegisterWmoInstance(container);
        }

        private void UnregisterWmoInstance(WMOContainer container)
        {
            foreach (var key in wmoInstances.Keys.ToArray())
            {
                var instances = wmoInstances[key];
                instances.Remove(container);
                if (instances.Count == 0)
                    wmoInstances.Remove(key);
            }
        }

        private void RegisterM2Instance(M2Container container)
        {
            if (!m2Instances.TryGetValue(container.FileDataId, out var instances))
            {
                instances = [];
                m2Instances.Add(container.FileDataId, instances);
                m2InstancePackets.Add(container.FileDataId, new M2InstancePacket(instances));
            }

            instances.Add(container);
            m2InstancePackets[container.FileDataId].Invalidate();
        }

        private void UnregisterM2Instance(M2Container container)
        {
            if (!m2Instances.TryGetValue(container.FileDataId, out var instances))
                return;

            instances.Remove(container);
            if (instances.Count == 0)
            {
                m2Instances.Remove(container.FileDataId);
                m2InstancePackets.Remove(container.FileDataId);
            }
            else if (m2InstancePackets.TryGetValue(container.FileDataId, out var packet))
            {
                packet.Invalidate();
            }
        }

        private void SpawnWMODoodads(WMOContainer wmoContainer)
        {
            var wmo = wmoContainer.GetWMO();
            var enabledSets = wmoContainer.EnabledDoodadSets;
            tileSceneBoundsByRoot.TryGetValue(wmoContainer.ParentFileDataId, out var owningTileBounds);

            wmoContainer.ActiveDoodads.Clear();

            for (var doodadIndex = 0; doodadIndex < wmo.doodads.Length; doodadIndex++)
            {
                var doodad = wmo.doodads[doodadIndex];
                if (!IsWmoDoodadSpawnable(doodad, enabledSets))
                    continue;

                var m2Container = new M2Container(_device, doodad.filedataid, wmoContainer.ParentFileDataId)
                {
                    ParentWMO = wmoContainer,
                    LocalPosition = doodad.position,
                    LocalRotation = doodad.rotation,
                    LocalScale = doodad.scale,
                    WmoDoodadIndex = doodadIndex,
                };

                lock (SceneObjectLock)
                    SceneObjects.Add(m2Container);

                wmoContainer.ActiveDoodads.Add(m2Container);
                owningTileBounds?.AddObject(m2Container);
            }
        }

        internal static bool IsWmoDoodadSpawnable(in WMODoodad doodad, IReadOnlyList<bool> enabledSets) =>
            doodad.filedataid != 0 &&
            doodad.doodadSet < enabledSets.Count &&
            enabledSets[(int)doodad.doodadSet];

        public void RefreshWMODoodads(WMOContainer wmoContainer)
        {
            if (!wmoContainer.IsLoaded)
                return;

            lock (SceneObjectLock)
            {
                tileSceneBoundsByRoot.TryGetValue(wmoContainer.ParentFileDataId, out var owningTileBounds);
                foreach (var doodad in wmoContainer.ActiveDoodads)
                {
                    owningTileBounds?.RemoveObject(doodad);
                    SceneObjects.Remove(doodad);
                    M2Cache.Release(doodad.FileDataId, doodad.ParentFileDataId);
                }

                SpawnWMODoodads(wmoContainer);

                UpdateInstanceList();
            }
        }

        public bool ProcessQueue(double synchronousBudgetMilliseconds = 10d)
        {
            if (!double.IsFinite(synchronousBudgetMilliseconds))
                synchronousBudgetMilliseconds = 10d;
            synchronousBudgetMilliseconds = Math.Clamp(synchronousBudgetMilliseconds, 0d, 10d);
            var queueTimer = Stopwatch.StartNew();

            // Filling the bounded parse window is intentionally independent of
            // the synchronous GPU budget. The channel workers keep parsing even
            // if presentation slows to a longer viewport interval.
            QueuePendingTileLoads();

            var unloadDeadline = AllocatePhaseDeadline(
                queueTimer.Elapsed.TotalMilliseconds,
                synchronousBudgetMilliseconds,
                0.2d);
            ProcessUnloadQueue(queueTimer, unloadDeadline);

            var firstWorkPhaseDeadline = AllocatePhaseDeadline(
                queueTimer.Elapsed.TotalMilliseconds,
                synchronousBudgetMilliseconds,
                0.5d);
            var uploaded = 0;
            if (nextStreamingPhase == 0)
            {
                uploaded += UploadQueuedResources(queueTimer, firstWorkPhaseDeadline);
                ProcessPopulationQueues(queueTimer, synchronousBudgetMilliseconds);
            }
            else
            {
                ProcessPopulationQueues(queueTimer, firstWorkPhaseDeadline);
                uploaded += UploadQueuedResources(queueTimer, synchronousBudgetMilliseconds);
            }
            nextStreamingPhase = (nextStreamingPhase + 1) % 2;
            UploadedResourcesLastFrame = uploaded;

            return GetPendingOperationCount() > 0;
        }

        private void ProcessPopulationQueues(Stopwatch queueTimer, double deadlineMilliseconds)
        {
            if (nextPopulationQueue == 0)
            {
                ProcessPendingAdtPopulations(queueTimer, deadlineMilliseconds);
                ProcessPendingWmoDoodads(queueTimer, deadlineMilliseconds);
            }
            else
            {
                ProcessPendingWmoDoodads(queueTimer, deadlineMilliseconds);
                ProcessPendingAdtPopulations(queueTimer, deadlineMilliseconds);
            }
            nextPopulationQueue = (nextPopulationQueue + 1) % 2;
        }

        private void ProcessPendingAdtPopulations(Stopwatch queueTimer, double budgetMilliseconds)
        {
            while (pendingAdtPopulations.Count > 0 &&
                   queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds)
            {
                var pending = pendingAdtPopulations.Dequeue();
                if (!adtContainers.Contains(pending.Container))
                    continue;

                var terrain = pending.Terrain;
                while (queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds)
                {
                    if (pending.NextWorldModel < terrain.worldModelBatches.Length)
                    {
                        AddWorldModelPlacement(
                            terrain.worldModelBatches[pending.NextWorldModel++],
                            terrain.rootADTFileDataID,
                            pending.Bounds);
                        continue;
                    }

                    if (pending.NextDoodad < terrain.doodads.Length)
                    {
                        AddDoodadPlacement(
                            terrain.doodads[pending.NextDoodad++],
                            terrain.rootADTFileDataID,
                            pending.Bounds);
                        continue;
                    }

                    break;
                }

                if (pending.NextWorldModel < terrain.worldModelBatches.Length ||
                    pending.NextDoodad < terrain.doodads.Length)
                {
                    pendingAdtPopulations.Enqueue(pending);
                }
            }
        }

        private void ProcessPendingWmoDoodads(Stopwatch queueTimer, double budgetMilliseconds)
        {
            var remaining = pendingWMODoodads.Count;
            for (var pendingIndex = 0;
                 pendingIndex < remaining &&
                 queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds;
                 pendingIndex++)
            {
                var pending = pendingWMODoodads.Dequeue();
                var container = pending.Container;
                if (!SceneObjects.Contains(container))
                    continue;
                if (!container.IsLoaded)
                {
                    pendingWMODoodads.Enqueue(pending);
                    continue;
                }

                if (!pending.RegisteredLoadedGroups)
                {
                    RegisterLoadedWmoInstance(container);
                    pending.RegisteredLoadedGroups = true;
                }

                var wmo = container.GetWMO();
                var enabledSets = container.EnabledDoodadSets;
                tileSceneBoundsByRoot.TryGetValue(container.ParentFileDataId, out var owningTileBounds);
                if (!pending.Initialized)
                {
                    container.ActiveDoodads.Clear();
                    pending.Initialized = true;
                }

                while (pending.NextDoodad < wmo.doodads.Length &&
                       queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds)
                {
                    var doodadIndex = pending.NextDoodad++;
                    var doodad = wmo.doodads[doodadIndex];
                    if (!IsWmoDoodadSpawnable(doodad, enabledSets))
                        continue;

                    var doodadContainer = new M2Container(
                        _device,
                        doodad.filedataid,
                        container.ParentFileDataId)
                    {
                        ParentWMO = container,
                        LocalPosition = doodad.position,
                        LocalRotation = doodad.rotation,
                        LocalScale = doodad.scale,
                        WmoDoodadIndex = doodadIndex,
                    };

                    lock (SceneObjectLock)
                        SceneObjects.Add(doodadContainer);
                    container.ActiveDoodads.Add(doodadContainer);
                    owningTileBounds?.AddObject(doodadContainer);
                    RegisterM2Instance(doodadContainer);
                }

                if (pending.NextDoodad < wmo.doodads.Length)
                    pendingWMODoodads.Enqueue(pending);
                else
                    container.DoodadsSpawned = true;
            }
        }

        private void QueuePendingTileLoads()
        {
            while (tilesToLoad.Count > 0 &&
                   tilesInFlight.Count < ADTCache.MaxRequestsInFlight)
            {
                var mapTile = tilesToLoad.Dequeue();
                tilesQueuedForLoad.Remove(mapTile);
                tilesInFlight.Add(mapTile);

                try
                {
                    var adtContainer = new ADTContainer(_device, mapTile);
                    adtContainer.LoadCallback += OnADTContainerLoaded;
                    adtContainer.LoadFailedCallback += OnADTContainerLoadFailed;

                    ADTCache.GetOrLoad(
                        mapTile,
                        mapTile.wdtFileDataID,
                        adtContainer.OnLoaded,
                        adtContainer.OnLoadFailed);
                    adtContainer.MarkCacheReferenceHeld();

                    lock (SceneObjectLock)
                    {
                        SceneObjects.Add(adtContainer);
                        adtContainers.Add(adtContainer);
                    }
                }
                catch (Exception ex)
                {
                    tilesInFlight.Remove(mapTile);
                    failedDesiredTiles.Add(mapTile);
                    ReportSceneLoadFailure(
                        $"Queuing ADT {mapTile.tileX}, {mapTile.tileY} for WDT {mapTile.wdtFileDataID}", ex);
                }
            }
        }

        private int UploadQueuedResources(Stopwatch queueTimer, double budgetMilliseconds)
        {
            var uploaded = 0;
            var firstQueue = nextUploadQueue;

            // Revisit every cache fairly until the deadline. Limiting each
            // cache to one item per round prevents starvation without limiting
            // texture/model throughput to one item per presented frame.
            while (queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds)
            {
                var pendingBefore = GetPendingResourceQueueCount();
                var uploadedThisRound = 0;
                for (var offset = 0;
                     offset < 4 && queueTimer.Elapsed.TotalMilliseconds < budgetMilliseconds;
                     offset++)
                {
                    var queueIndex = GetUploadQueueIndex(firstQueue, offset);
                    uploadedThisRound += queueIndex switch
                    {
                        0 => ADTCache.Upload(queueTimer, _device, budgetMilliseconds, maxItems: 1),
                        1 => WMOCache.Upload(queueTimer, budgetMilliseconds, maxItems: 1),
                        2 => M2Cache.Upload(queueTimer, budgetMilliseconds, maxItems: 1),
                        _ => BLPCache.Upload(queueTimer, budgetMilliseconds, maxItems: 1)
                    };
                }

                uploaded += uploadedThisRound;
                firstQueue = GetUploadQueueIndex(firstQueue, 1);

                // Counts also include workers currently parsing. If no upload
                // completed and no stale/error result was drained, there is no
                // render-thread work ready yet; try again on the next frame.
                if (uploadedThisRound == 0 &&
                    GetPendingResourceQueueCount() >= pendingBefore)
                {
                    break;
                }
            }

            nextUploadQueue = firstQueue;
            return uploaded;
        }

        private static int GetPendingResourceQueueCount() =>
            ADTCache.GetLoadQueueCount() +
            WMOCache.GetLoadQueueCount() +
            M2Cache.GetLoadQueueCount() +
            BLPCache.GetQueueCount();

        internal static int GetUploadQueueIndex(int firstQueue, int offset) =>
            (firstQueue + offset) % 4;

        internal static double AllocatePhaseDeadline(
            double elapsedMilliseconds,
            double budgetMilliseconds,
            double share)
        {
            var remaining = Math.Max(0d, budgetMilliseconds - elapsedMilliseconds);
            return Math.Min(
                budgetMilliseconds,
                elapsedMilliseconds + remaining * Math.Clamp(share, 0d, 1d));
        }

        public int GetPendingOperationCount() =>
            tilesToLoad.Count +
            tilesInFlight.Count +
            pendingAdtPopulations.Count +
            pendingTileUnloads.Count +
            pendingWMODoodads.Count +
            ADTCache.GetLoadQueueCount() +
            WMOCache.GetLoadQueueCount() +
            M2Cache.GetLoadQueueCount() +
            BLPCache.GetQueueCount();

        public static AssetStreamingMetrics GetAssetStreamingMetrics() => new(
            ADTCache.GetQueueMetrics(),
            BLPCache.GetQueueMetrics(),
            M2Cache.GetQueueMetrics(),
            WMOCache.GetQueueMetrics());

        public bool TryGetTerrainTileMaxHeight(uint wdtFileDataId, byte tileX, byte tileY, out float height)
        {
            var container = adtContainers.FirstOrDefault(candidate =>
                candidate.IsLoaded && candidate.mapTile.wdtFileDataID == wdtFileDataId &&
                candidate.mapTile.tileX == tileX && candidate.mapTile.tileY == tileY);
            if (container == null)
            {
                height = 0;
                return false;
            }

            height = container.Terrain.terrainBounds.Max.Z;
            return true;
        }

        private void OnADTContainerLoaded(ADTContainer adtContainer, Terrain terrain)
        {
            // unregister the callback, adts only load once, probably
            adtContainer.LoadCallback -= OnADTContainerLoaded;
            adtContainer.LoadFailedCallback -= OnADTContainerLoadFailed;

            var tileBoundsKey = GetTileBoundsKey(adtContainer.mapTile);
            if (!tileSceneBounds.TryGetValue(tileBoundsKey, out var owningTileBounds))
            {
                owningTileBounds = new TileSceneBounds(adtContainer.mapTile);
                tileSceneBounds.Add(tileBoundsKey, owningTileBounds);
            }
            owningTileBounds.SetTerrain(
                terrain.rootADTFileDataID,
                terrain.terrainBounds,
                terrain.worldLiquid.hasBounds ? terrain.worldLiquid.bounds : null);
            tileSceneBoundsByRoot[terrain.rootADTFileDataID] = owningTileBounds;
            TerrainTileHeightAvailable?.Invoke(adtContainer.mapTile, terrain.terrainBounds.Max.Z);
            pendingAdtPopulations.Enqueue(new PendingAdtPopulation(
                adtContainer,
                terrain,
                owningTileBounds));
            tilesInFlight.Remove(adtContainer.mapTile);
            loadedTiles.Add(adtContainer.mapTile);
            failedDesiredTiles.Remove(adtContainer.mapTile);
        }

        private void AddWorldModelPlacement(
            in WorldModelBatch worldModel,
            uint rootAdtFileDataId,
            TileSceneBounds owningTileBounds)
        {
            if (uuidUsers.ContainsKey(worldModel.uniqueID))
                return;

            var container = new WMOContainer(_device, worldModel.fileDataID, rootAdtFileDataId)
            {
                Position = worldModel.position,
                Rotation = worldModel.rotation,
                Scale = worldModel.scale == 0 ? 1 : worldModel.scale,
                UniqueID = worldModel.uniqueID,
                PlacementFlags = worldModel.flags,
                PlacementDoodadSet = worldModel.doodadSet,
                PlacementNameSet = worldModel.nameSet,
                OnDoodadSetsChanged = RefreshWMODoodads,
                OnGroupsChanged = _ => UpdateWMOInstanceList()
            };
            container.SetDoodadSetsToEnable(worldModel.doodadSetIDs);

            lock (SceneObjectLock)
                SceneObjects.Add(container);
            owningTileBounds.AddObject(container);
            uuidUsers[worldModel.uniqueID] = 1;
            pendingWMODoodads.Enqueue(new PendingWmoDoodadPopulation(container));
            RegisterWmoInstance(container);
        }

        private void AddDoodadPlacement(
            in Doodad doodad,
            uint rootAdtFileDataId,
            TileSceneBounds owningTileBounds)
        {
            var container = new M2Container(_device, doodad.fileDataID, rootAdtFileDataId)
            {
                Position = doodad.position,
                Rotation = doodad.rotation,
                Scale = doodad.scale,
                UniqueID = doodad.uniqueID,
                PlacementFlags = doodad.flags
            };

            lock (SceneObjectLock)
                SceneObjects.Add(container);
            owningTileBounds.AddObject(container);
            RegisterM2Instance(container);
        }

        private void OnADTContainerLoadFailed(ADTContainer adtContainer, Exception exception)
        {
            adtContainer.LoadCallback -= OnADTContainerLoaded;
            adtContainer.LoadFailedCallback -= OnADTContainerLoadFailed;
            tilesInFlight.Remove(adtContainer.mapTile);
            loadedTiles.Remove(adtContainer.mapTile);
            failedDesiredTiles.Add(adtContainer.mapTile);
            RemovePendingAdtPopulation(adtContainer.mapTile);

            lock (SceneObjectLock)
            {
                SceneObjects.Remove(adtContainer);
                adtContainers.Remove(adtContainer);
            }

            adtContainer.Unload();
            ReportSceneLoadFailure(
                $"Loading ADT {adtContainer.mapTile.tileX}, {adtContainer.mapTile.tileY} " +
                $"for WDT {adtContainer.mapTile.wdtFileDataID}",
                exception);
        }

        private void ReportSceneLoadFailure(string message, Exception exception)
        {
            LoadDiagnostics.Error(message, exception);
            SceneLoadFailed?.Invoke(message, exception);
        }

    }
}
