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

namespace WoWRenderLib.DX11.Managers
{
    public partial class SceneManager
    {
        public void MoveSelectedObject(Vector3 delta)
        {
            if (SelectedObject == null)
                return;

            SelectedObject.Position += delta;
            MarkTileBoundsDirty(SelectedObject.ParentFileDataId);

            if (SelectedObject is M2Container selectedM2)
            {
                if (m2InstancePackets.TryGetValue(selectedM2.FileDataId, out var packet))
                    packet.Invalidate();
            }
            else if (SelectedObject is WMOContainer)
            {
                // Parent WMO movement changes every active doodad matrix.
                foreach (var packet in m2InstancePackets.Values)
                    packet.Invalidate();
            }
        }

        public void UpdateSelectedObjectTransform(
            Vector3 position,
            Vector3 rotationDegrees,
            float scale,
            bool lockWorldModelScale)
        {
            if (SelectedObject == null)
                return;

            SelectedObject.Position = position;
            SelectedObject.Rotation = rotationDegrees;
            SelectedObject.Scale = lockWorldModelScale && SelectedObject is WMOContainer
                ? 1f
                : Math.Max(0.001f, scale);
            MarkTileBoundsDirty(SelectedObject.ParentFileDataId);

            if (SelectedObject is M2Container selectedM2 &&
                m2InstancePackets.TryGetValue(selectedM2.FileDataId, out var packet))
                packet.Invalidate();
            else if (SelectedObject is WMOContainer)
                foreach (var m2Packet in m2InstancePackets.Values)
                    m2Packet.Invalidate();
        }

        public void UpdateSelectedWmoPlacement(ushort doodadSet, ushort nameSet)
        {
            if (SelectedObject is not WMOContainer worldModel)
                return;

            worldModel.PlacementDoodadSet = doodadSet;
            worldModel.PlacementNameSet = nameSet;
            if ((worldModel.PlacementFlags & (uint)MapObjDefFlags.use_sets_from_mwds) == 0)
                worldModel.SetDoodadSetsToEnable([doodadSet]);
        }

        /// <summary>
        /// Invalidates the conservative scene aggregate for an ADT. Object
        /// editing and future terrain/liquid mutation paths must call this
        /// after changing world-space bounds.
        /// </summary>
        public void MarkTileBoundsDirty(uint rootAdtFileDataId)
        {
            if (tileSceneBoundsByRoot.TryGetValue(rootAdtFileDataId, out var bounds))
                bounds.MarkDirty();
        }

        public bool IsTerrainTileModified(MapTile tile)
        {
            var container = adtContainers.FirstOrDefault(adt => adt.mapTile == tile);
            return container?.IsModified == true;
        }

        public bool HasUnsavedTerrainChanges =>
            adtContainers.Any(adt => adt.IsModified);

        public IReadOnlyList<ModifiedTerrainTile> GetModifiedTerrainTiles()
        {
            return adtContainers
                .Where(adt => adt.IsModified && adt.Terrain.vertices is { Length: > 0 })
                .Select(adt => new ModifiedTerrainTile(
                    TerrainTileId.From(adt.mapTile),
                    adt.Terrain.rootADTFileDataID,
                    adt.Terrain.vertices.ToArray()))
                .ToArray();
        }

        public void MarkTerrainChangesSaved()
        {
            foreach (var adt in adtContainers.Where(adt => adt.IsModified))
                adt.MarkSaved();
        }

        public void BeginTerrainStroke()
        {
            lock (SceneObjectLock)
            {
                _flattenStrokeHeight = null;
                _activeTerrainStrokeBefore = [];
            }
        }

        public TerrainStrokeDelta? EndTerrainStroke()
        {
            lock (SceneObjectLock)
            {
                if (_activeTerrainStrokeBefore == null)
                    return null;

                var edits = new List<TerrainTileEdit>(_activeTerrainStrokeBefore.Count);
                foreach (var (tile, before) in _activeTerrainStrokeBefore)
                {
                    var adt = adtContainers.FirstOrDefault(candidate =>
                        TerrainTileId.From(candidate.mapTile) == tile);
                    if (adt?.Terrain.vertices is not { Length: > 0 } after ||
                        before.SequenceEqual(after))
                    {
                        continue;
                    }

                    edits.Add(new TerrainTileEdit(
                        tile,
                        adt.Terrain.rootADTFileDataID,
                        before,
                        after.ToArray()));
                }

                _activeTerrainStrokeBefore = null;
                return edits.Count == 0 ? null : new TerrainStrokeDelta(edits);
            }
        }

        public void ApplyTerrainStroke(TerrainStrokeDelta delta, bool useAfter)
        {
            ArgumentNullException.ThrowIfNull(delta);
            lock (SceneObjectLock)
            {
                foreach (var edit in delta.Tiles)
                {
                    var adt = adtContainers.FirstOrDefault(candidate =>
                        TerrainTileId.From(candidate.mapTile) == edit.Tile);
                    if (adt?.Terrain.vertices is not { Length: > 0 })
                        continue;

                    var terrain = adt.Terrain;
                    adt.EnsureOriginalVerticesCaptured();
                    terrain.vertices = (useAfter ? edit.After : edit.Before).ToArray();
                    RebuildTerrainBounds(ref terrain);
                    UploadTerrainVertices(terrain);
                    adt.UpdateTerrain(terrain);
                    adt.RefreshModifiedState();
                    MarkTileBoundsDirty(terrain.rootADTFileDataID);
                }
            }
        }

        public void PerformRaycast(float mouseX, float mouseY, Camera camera, int windowWidth, int windowHeight)
        {
            var ray = camera.GetRayFromScreen(mouseX, mouseY, windowWidth, windowHeight);

            Container3D? closestObject = null;
            float closestDistance = float.MaxValue;

            lock (SceneObjectLock)
            {
                // Terrain is opaque for selection: an object's bounds may only win
                // when their first intersection is closer than the terrain surface.
                if (RenderADT && TryRaycastTerrainLocked(ray, out var terrainHit))
                    closestDistance = Vector3.Distance(ray.Origin, terrainHit.WorldPosition);

                foreach (var sceneObject in SceneObjects)
                {
                    if (sceneObject is ADTContainer)
                        continue;

                    if (!RenderWMO && sceneObject is WMOContainer)
                        continue;

                    if (!RenderM2 && sceneObject is M2Container)
                        continue;

                    // Make doodads unselectable
                    if (sceneObject is M2Container m2container && m2container.ParentWMO != null)
                        continue;

                    var sphere = sceneObject.GetBoundingSphere();
                    if (!sphere.HasValue ||
                        !ScreenSpaceCulling.IntersectsRenderDistance(
                            ray.Origin,
                            sphere.Value.Center,
                            sphere.Value.Radius,
                            ModelRenderDistance) ||
                        !IntersectionTests.RayIntersectsSphere(
                            ray,
                            sphere.Value,
                            out var sphereDistance) ||
                        sphereDistance >= closestDistance)
                    {
                        continue;
                    }

                    var box = sceneObject.GetBoundingBox();
                    if (box.HasValue &&
                        (!IntersectionTests.RayIntersectsBox(ray, box.Value, out var boxDistance) ||
                         boxDistance >= closestDistance))
                    {
                        continue;
                    }

                    if (!sceneObject.TryRaycastTriangles(
                            ray,
                            closestDistance,
                            out var triangleDistance))
                    {
                        continue;
                    }

                    closestDistance = triangleDistance;
                    closestObject = sceneObject;
                }
            }

            SelectedObject?.IsSelected = false;
            SelectedObject = closestObject;
            SelectedObject?.IsSelected = true;
        }

        public void ClearBrushPreview()
        {
            BrushWorldPosition = null;
            BrushRadius = 0f;
            BrushFalloff = 0f;
            BrushHasFalloff = false;
        }

        public bool UpdateBrushPreview(
            Vector2 mousePosition,
            Camera camera,
            int windowWidth,
            int windowHeight,
            in BrushInput brush,
            Vector4 color)
        {
            if (!TryUpdateBrushPreview(mousePosition, camera, windowWidth, windowHeight, brush, color, out _))
            {
                ClearBrushPreview();
                return false;
            }

            return true;
        }

        public void UpdateTerrainBrush(
            Vector2 mousePosition,
            Camera camera,
            int windowWidth,
            int windowHeight,
            in BrushInput brush,
            TerrainBrushInput input,
            bool apply,
            float deltaTime)
        {
            var previewColor = TerrainBrushTools.Get(input.ToolMode).PreviewColor;
            if (!TryUpdateBrushPreview(
                    mousePosition,
                    camera,
                    windowWidth,
                    windowHeight,
                    brush,
                    previewColor,
                    out var hit))
            {
                ClearBrushPreview();
                return;
            }

            if (apply)
                ApplyTerrainBrush(hit, brush, input, Math.Clamp(deltaTime, 0f, 0.1f));
        }

        private bool TryUpdateBrushPreview(
            Vector2 mousePosition,
            Camera camera,
            int windowWidth,
            int windowHeight,
            in BrushInput brush,
            Vector4 color,
            out TerrainRayHit hit)
        {
            var ray = camera.GetRayFromScreen(
                mousePosition.X,
                mousePosition.Y,
                windowWidth,
                windowHeight);

            if (!TryRaycastTerrain(ray, out hit))
                return false;

            BrushWorldPosition = hit.WorldPosition;
            BrushRadius = Math.Clamp(brush.Radius, 1f, 1000f);
            BrushFalloff = Math.Clamp(brush.Falloff, 0f, 1f);
            BrushHasFalloff = brush.HasFalloff;
            BrushShape = brush.Shape;
            BrushFalloffProfile = brush.HasFalloff
                ? brush.FalloffProfile
                : BrushFalloffProfile.Hard;
            BrushColor = color;
            return true;
        }

        private bool TryRaycastTerrain(Ray ray, out TerrainRayHit closestHit)
        {
            lock (SceneObjectLock)
                return TryRaycastTerrainLocked(ray, out closestHit);
        }

        /// <summary>
        /// Raycasts only loaded terrain and returns the hit chunk's active textures
        /// in material-layer order. Scene objects intentionally do not participate.
        /// </summary>
        public IReadOnlyList<TerrainChunkTextureLayer> GetTerrainChunkTextures(
            Vector2 mousePosition,
            Camera camera,
            int windowWidth,
            int windowHeight)
        {
            if (windowWidth <= 0 || windowHeight <= 0)
                return Array.Empty<TerrainChunkTextureLayer>();

            var ray = camera.GetRayFromScreen(
                mousePosition.X,
                mousePosition.Y,
                windowWidth,
                windowHeight);

            lock (SceneObjectLock)
            {
                if (!TryRaycastTerrainLocked(ray, out var hit))
                    return Array.Empty<TerrainChunkTextureLayer>();

                var batches = hit.Container.Terrain.renderBatches;
                if (batches == null || (uint)hit.ChunkIndex >= (uint)batches.Length)
                    return Array.Empty<TerrainChunkTextureLayer>();

                var materials = batches[hit.ChunkIndex].materialFDIDs;
                if (materials == null || materials.Length == 0)
                    return Array.Empty<TerrainChunkTextureLayer>();

                return materials
                    .Select((fileDataId, layerIndex) => (fileDataId, layerIndex))
                    .Where(layer => layer.fileDataId > 0)
                    .Select(layer => new TerrainChunkTextureLayer(
                        layer.layerIndex,
                        checked((uint)layer.fileDataId)))
                    .ToArray();
            }
        }

        public TerrainChunkTextureLayer? GetDominantTerrainTexture(
            Vector2 mousePosition,
            Camera camera,
            int windowWidth,
            int windowHeight)
        {
            if (windowWidth <= 0 || windowHeight <= 0)
                return null;

            var ray = camera.GetRayFromScreen(
                mousePosition.X,
                mousePosition.Y,
                windowWidth,
                windowHeight);

            lock (SceneObjectLock)
            {
                if (!TryRaycastTerrainLocked(ray, out var hit))
                    return null;

                var batches = hit.Container.Terrain.renderBatches;
                if (batches == null || (uint)hit.ChunkIndex >= (uint)batches.Length)
                    return null;

                var batch = batches[hit.ChunkIndex];
                var weights = TerrainAlphaMapSampler.SampleWeights(
                    batch.alphaMaterials,
                    batch.layerCount,
                    hit.TextureCoordinate);
                var dominantLayer = TerrainAlphaMapSampler.FindDominantLayer(
                    weights,
                    batch.materialFDIDs);

                return dominantLayer >= 0
                    ? new TerrainChunkTextureLayer(
                        dominantLayer,
                        checked((uint)batch.materialFDIDs[dominantLayer]))
                    : null;
            }
        }

        public IReadOnlyList<TerrainChunkTextureLayer> GetTerrainTileTextures(Vector3 worldPosition)
        {
            var (tileX, tileY) = GetTileFromPosition(worldPosition);
            lock (SceneObjectLock)
            {
                var adt = adtContainers.FirstOrDefault(candidate =>
                    candidate.IsLoaded &&
                    candidate.mapTile.wdtFileDataID == CurrentWDTFileDataID &&
                    candidate.mapTile.tileX == tileX &&
                    candidate.mapTile.tileY == tileY);
                if (adt == null)
                    return Array.Empty<TerrainChunkTextureLayer>();

                var seen = new HashSet<uint>();
                var textures = new List<TerrainChunkTextureLayer>();
                foreach (var batch in adt.Terrain.renderBatches ?? [])
                {
                    var materials = batch.materialFDIDs;
                    if (materials == null)
                        continue;
                    var layerCount = Math.Min(batch.layerCount, materials.Length);
                    for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
                    {
                        var fileDataId = materials[layerIndex];
                        if (fileDataId <= 0 || !seen.Add(checked((uint)fileDataId)))
                            continue;
                        textures.Add(new TerrainChunkTextureLayer(
                            layerIndex,
                            checked((uint)fileDataId)));
                    }
                }

                return textures;
            }
        }

        private bool TryRaycastTerrainLocked(Ray ray, out TerrainRayHit closestHit)
        {
            closestHit = default;
            if (!RenderADT)
                return false;

            var closestDistance = float.MaxValue;

            foreach (var adt in adtContainers)
            {
                if (!adt.IsLoaded || adt.Terrain.vertices == null || adt.Terrain.indices == null)
                    continue;

                if (!Matrix4x4.Invert(adt.GetModelMatrix(), out var inverseModel))
                    continue;

                var modelMatrix = adt.GetModelMatrix();
                var localRay = new Ray(
                    Vector3.Transform(ray.Origin, inverseModel),
                    Vector3.Normalize(Vector3.TransformNormal(ray.Direction, inverseModel)));
                var terrain = adt.Terrain;
                if (!ScreenSpaceCulling.IntersectsRenderDistance(
                        ray.Origin,
                        terrain.terrainBoundingSphere.Center,
                        terrain.terrainBoundingSphere.Radius,
                        TerrainRenderDistance))
                    continue;
                if (!IntersectionTests.RayIntersectsBox(localRay, terrain.terrainBounds, out _))
                    continue;
                var candidate = new TerrainRaycastChunk(adt, modelMatrix, inverseModel, -1);

                for (var chunkIndex = 0; chunkIndex < terrain.chunkBounds.Length; chunkIndex++)
                {
                    var chunkSphere = terrain.chunkBoundingSpheres[chunkIndex];
                    if (!ScreenSpaceCulling.IntersectsRenderDistance(
                            ray.Origin,
                            chunkSphere.Center,
                            chunkSphere.Radius,
                            TerrainRenderDistance))
                        continue;
                    TryRaycastTerrainChunk(
                        candidate,
                        ray,
                        chunkIndex,
                        ref closestDistance,
                        ref closestHit);
                }
            }

            return closestDistance < float.MaxValue;
        }

        private static void TryRaycastTerrainChunk(
            TerrainRaycastChunk candidate,
            Ray worldRay,
            int chunkIndex,
            ref float closestDistance,
            ref TerrainRayHit closestHit)
        {
            var terrain = candidate.Container.Terrain;
            var localRay = new Ray(
                Vector3.Transform(worldRay.Origin, candidate.InverseModel),
                Vector3.Normalize(Vector3.TransformNormal(worldRay.Direction, candidate.InverseModel)));
            if (!IntersectionTests.RayIntersectsBox(
                    localRay,
                    terrain.chunkBounds[chunkIndex],
                    out _))
            {
                return;
            }

            var indexStart = chunkIndex * (int)TerrainIndicesPerChunk;
            var indexEnd = Math.Min(indexStart + (int)TerrainIndicesPerChunk, terrain.indices.Length);
            for (var index = indexStart; index < indexEnd; index += 3)
            {
                var i0 = terrain.indices[index];
                var i1 = terrain.indices[index + 1];
                var i2 = terrain.indices[index + 2];
                if (i0 == i1 || i1 == i2 || i0 == i2 ||
                    i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= terrain.vertices.Length ||
                    i1 >= terrain.vertices.Length ||
                    i2 >= terrain.vertices.Length)
                {
                    continue;
                }

                if (!RayIntersectsTriangle(
                        localRay,
                        terrain.vertices[i0].Position,
                        terrain.vertices[i1].Position,
                        terrain.vertices[i2].Position,
                        out var distance,
                        out var localPosition,
                        out var triangleU,
                        out var triangleV) ||
                    distance >= closestDistance)
                {
                    continue;
                }

                var worldPosition = Vector3.Transform(localPosition, candidate.ModelMatrix);
                var localNormal = Vector3.Normalize(Vector3.Cross(
                    terrain.vertices[i1].Position - terrain.vertices[i0].Position,
                    terrain.vertices[i2].Position - terrain.vertices[i0].Position));
                var worldNormal = Vector3.Normalize(Vector3.TransformNormal(
                    localNormal,
                    Matrix4x4.Transpose(candidate.InverseModel)));
                if (worldNormal.Z < 0f)
                    worldNormal = -worldNormal;

                closestDistance = Vector3.Distance(worldRay.Origin, worldPosition);
                closestHit = new TerrainRayHit(
                    candidate.Container,
                    chunkIndex,
                    localPosition,
                    terrain.vertices[i0].TexCoord * (1f - triangleU - triangleV) +
                    terrain.vertices[i1].TexCoord * triangleU +
                    terrain.vertices[i2].TexCoord * triangleV,
                    worldPosition,
                    worldNormal);
            }
        }

        private void ApplyTerrainBrush(
            TerrainRayHit hit, BrushInput brush, TerrainBrushInput input, float deltaTime)
        {
            brush = brush with { Radius = Math.Clamp(brush.Radius, 1f, 1000f) };
            lock (SceneObjectLock)
            {
                // Include a halo for smoothing neighbours and seam-normal triangles.
                var tiles = new HashSet<ADTContainer>();
                WorldChunkRange.ForEachChunkInRange(
                    adtContainers, hit.WorldPosition, brush.Radius * MathF.Sqrt(2f) + 16f,
                    static adt => adt.IsLoaded && adt.Terrain.vertices is { Length: > 0 },
                    static adt => adt.GetModelMatrix(),
                    static adt => adt.Terrain.terrainBounds,
                    static adt => adt.Terrain.chunkBounds,
                    static bounds => bounds,
                    context => { tiles.Add(context.Tile); return false; },
                    _ => { });
                if (input.ToolMode == TerrainBrushMode.Flatten &&
                    input.FlattenTarget == TerrainFlattenTarget.BrushCenter)
                {
                    _flattenStrokeHeight ??= hit.WorldPosition.Z;
                    input.FlattenHeight = _flattenStrokeHeight.Value;
                }
                foreach (var tile in tiles)
                {
                    tile.EnsureOriginalVerticesCaptured();
                    var id = TerrainTileId.From(tile.mapTile);
                    if (_activeTerrainStrokeBefore is { } before && !before.ContainsKey(id))
                        before[id] = tile.Terrain.vertices.ToArray();
                }
                var surfaces = tiles.Select(tile => new TerrainSurfaceEditor.Surface(
                    tile.Terrain.vertices, tile.Terrain.indices, tile.GetModelMatrix())).ToArray();
                if (!TerrainSurfaceEditor.Apply(surfaces, hit.WorldPosition, brush, input, deltaTime))
                    return;
                foreach (var tile in tiles)
                {
                    var terrain = tile.Terrain;
                    RebuildTerrainBounds(ref terrain);
                    UploadTerrainVertices(terrain);
                    tile.UpdateTerrain(terrain);
                    tile.RefreshModifiedState();
                    MarkTileBoundsDirty(terrain.rootADTFileDataID);
                }
            }
        }

        private unsafe void UploadTerrainVertices(Terrain terrain)
        {
            if (terrain.vertexBuffer.Handle == null || terrain.vertices == null)
                return;

            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(_deviceContext.Map(
                terrain.vertexBuffer,
                0,
                Map.WriteDiscard,
                0,
                ref mapped));
            var destination = new Span<ADTGpuVertex>(mapped.PData, terrain.vertices.Length);
            for (var index = 0; index < terrain.vertices.Length; index++)
                destination[index] = ADTGpuVertex.FromCpu(terrain.vertices[index]);
            _deviceContext.Unmap(terrain.vertexBuffer, 0);
        }

        private static void RebuildTerrainBounds(ref Terrain terrain)
        {
            for (var chunkIndex = 0; chunkIndex < terrain.chunkBounds.Length; chunkIndex++)
                RebuildTerrainChunkBounds(ref terrain, chunkIndex);

            RebuildTerrainAggregateBounds(ref terrain);
        }

        private static void RebuildTerrainChunkBounds(ref Terrain terrain, int chunkIndex)
        {
            var start = chunkIndex * TerrainVerticesPerChunk;
            var end = Math.Min(start + TerrainVerticesPerChunk, terrain.vertices.Length);
            if (start >= end || chunkIndex >= terrain.chunkBounds.Length)
                return;

            var min = terrain.vertices[start].Position;
            var max = min;
            for (var index = start + 1; index < end; index++)
            {
                min = Vector3.Min(min, terrain.vertices[index].Position);
                max = Vector3.Max(max, terrain.vertices[index].Position);
            }

            terrain.chunkBounds[chunkIndex] = new BoundingBox { Min = min, Max = max };
            var center = (min + max) * 0.5f;
            terrain.chunkBoundingSpheres[chunkIndex] = new BoundingSphere(
                center,
                Vector3.Distance(center, max));
        }

        private static void RebuildTerrainAggregateBounds(ref Terrain terrain)
        {
            if (terrain.chunkBounds.Length == 0)
                return;

            var terrainMin = terrain.chunkBounds[0].Min;
            var terrainMax = terrain.chunkBounds[0].Max;
            for (var index = 1; index < terrain.chunkBounds.Length; index++)
            {
                terrainMin = Vector3.Min(terrainMin, terrain.chunkBounds[index].Min);
                terrainMax = Vector3.Max(terrainMax, terrain.chunkBounds[index].Max);
            }
            terrain.terrainBounds = new BoundingBox { Min = terrainMin, Max = terrainMax };
            var terrainCenter = (terrainMin + terrainMax) * 0.5f;
            terrain.terrainBoundingSphere = new BoundingSphere(
                terrainCenter,
                Vector3.Distance(terrainCenter, terrainMax));
        }

        private static bool RayIntersectsTriangle(
            Ray ray,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            out float distance,
            out Vector3 hit,
            out float barycentricU,
            out float barycentricV)
        {
            const float epsilon = 0.000001f;
            distance = 0f;
            hit = default;
            barycentricU = 0f;
            barycentricV = 0f;
            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var p = Vector3.Cross(ray.Direction, edge2);
            var determinant = Vector3.Dot(edge1, p);
            if (MathF.Abs(determinant) < epsilon)
                return false;

            var inverse = 1f / determinant;
            var t = ray.Origin - v0;
            var u = Vector3.Dot(t, p) * inverse;
            if (u < 0f || u > 1f)
                return false;

            var q = Vector3.Cross(t, edge1);
            var v = Vector3.Dot(ray.Direction, q) * inverse;
            if (v < 0f || u + v > 1f)
                return false;

            distance = Vector3.Dot(edge2, q) * inverse;
            if (distance < 0f)
                return false;

            hit = ray.GetPoint(distance);
            barycentricU = u;
            barycentricV = v;
            return true;
        }

        private readonly record struct TerrainRayHit(
            ADTContainer Container,
            int ChunkIndex,
            Vector3 LocalPosition,
            Vector2 TextureCoordinate,
            Vector3 WorldPosition,
            Vector3 WorldNormal);

        private readonly record struct TerrainRaycastChunk(
            ADTContainer Container,
            Matrix4x4 ModelMatrix,
            Matrix4x4 InverseModel,
            int ChunkIndex);

    }
}
