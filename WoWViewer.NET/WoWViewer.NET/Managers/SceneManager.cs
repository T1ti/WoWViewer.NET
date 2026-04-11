using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Numerics;
using WoWFormatLib.Structs.WDT;
using WoWViewer.NET.Objects;
using WoWViewer.NET.Raycasting;
using WoWViewer.NET.Renderer;
using static WoWViewer.NET.Structs;

namespace WoWViewer.NET.Managers
{
    public class SceneManager(GL gl, ShaderManager shaderManager) : IDisposable
    {
        private readonly GL _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        private readonly ShaderManager _shaderManager = shaderManager ?? throw new ArgumentNullException(nameof(shaderManager));

        public List<Container3D> SceneObjects { get; } = [];
        public Lock SceneObjectLock { get; } = new();

        private readonly Queue<MapTile> tilesToLoad = new();
        private int totalTilesToLoad = 0;
        private readonly Dictionary<uint, uint> uuidUsers = [];
        private readonly HashSet<MapTile> loadedTiles = [];

        private WDT? currentWDT;
        public uint CurrentWDTFileDataID { get; private set; } = 775971;

        public Container3D? SelectedObject { get; set; } = null;

        private DebugRenderer? debugRenderer;
        public bool ShowBoundingBoxes { get; set; } = false;
        public bool ShowBoundingSpheres { get; set; } = false;

        public bool RenderADT { get; set; } = true;
        public bool RenderWMO { get; set; } = true;
        public bool RenderM2 { get; set; } = false;

        public Vector3 LightDirection { get; set; } = new Vector3(0.5f, 1f, 0.5f);

        private uint defaultTextureID;

        // Instance rendering
        private uint instanceMatrixVBO;
        private const int MaxInstancesPerBatch = 1024;

        // Shader programs
        private uint adtShaderProgram;
        private uint wmoShaderProgram;
        private uint m2ShaderProgram;
        private uint debugShaderProgram;

        // Shader uniforms
        private readonly int[] heightScaleUniforms = new int[8];
        private readonly int[] heightOffsetUniforms = new int[8];
        private readonly int[] layerScaleUniforms = new int[8];
        private readonly int[] alphaLayerUniforms = new int[2];
        private readonly int[] diffuseLayerUniforms = new int[8];
        private readonly int[] heightLayerUniforms = new int[8];

        public readonly Dictionary<uint, List<WMOContainer>> wmoInstances = [];
        public readonly Dictionary<uint, List<M2Container>> m2Instances = [];

        private static RenderState lastRenderState;
        private struct RenderState
        {
            public byte lastWMOVertexShaderID;
            public byte lastWMOPixelShaderID;
        }

        private int m2AlphaRefLoc;
        private int wmoAlphaRefLoc;

        public bool SceneLoaded => loadedTiles.Count > 0;
        public string StatusMessage { get; private set; } = "";

        public void Initialize(ShaderManager shaderManager, uint adtShader, uint wmoShader, uint m2Shader, uint debugShader)
        {
            adtShaderProgram = adtShader;
            wmoShaderProgram = wmoShader;
            m2ShaderProgram = m2Shader;
            debugShaderProgram = debugShader;

            RefreshUniforms();

            debugRenderer = new DebugRenderer(_gl, debugShaderProgram);
            defaultTextureID = MakeDefaultTexture();
            SetupInstanceBuffer();
        }

        public void RefreshUniforms()
        {
            for (int i = 0; i < 8; i++)
            {
                heightScaleUniforms[i] = _shaderManager.GetUniformLocation(adtShaderProgram, $"heightScales[{i}]");
                heightOffsetUniforms[i] = _shaderManager.GetUniformLocation(adtShaderProgram, $"heightOffsets[{i}]");
                layerScaleUniforms[i] = _shaderManager.GetUniformLocation(adtShaderProgram, $"layerScales[{i}]");
                diffuseLayerUniforms[i] = _shaderManager.GetUniformLocation(adtShaderProgram, $"diffuseLayers[{i}]");
                heightLayerUniforms[i] = _shaderManager.GetUniformLocation(adtShaderProgram, $"heightLayers[{i}]");
            }

            for (int i = 0; i < 2; i++)
                alphaLayerUniforms[i] = _shaderManager.GetUniformLocation(adtShaderProgram, $"alphaLayers[{i}]");

            m2AlphaRefLoc = _shaderManager.GetUniformLocation(m2ShaderProgram, "alphaRef");
            wmoAlphaRefLoc = _shaderManager.GetUniformLocation(wmoShaderProgram, "alphaRef");
        }

        public void LoadWDT(uint wdtFileDataID)
        {
            if (CurrentWDTFileDataID != wdtFileDataID)
            {
                loadedTiles.Clear();

                lock (SceneObjectLock)
                    SceneObjects.Clear();

                CurrentWDTFileDataID = wdtFileDataID;
                currentWDT = Cache.GetOrLoadWDT(CurrentWDTFileDataID);
            }
        }

        public WDT? GetCurrentWDT()
        {
            currentWDT ??= Cache.GetOrLoadWDT(CurrentWDTFileDataID);
            return currentWDT;
        }

        public void UpdateTilesByCameraPos(Vector3 cameraPosition)
        {
            if (currentWDT == null)
                return;

            var (x, y) = GetTileFromPosition(cameraPosition);

            var usedTiles = new List<MapTile>();

            var viewDistance = 2;
            for (int xOffset = -viewDistance; xOffset <= viewDistance; xOffset++)
            {
                for (int yOffset = -viewDistance; yOffset <= viewDistance; yOffset++)
                {
                    byte tileX = (byte)(x + xOffset);
                    byte tileY = (byte)(y + yOffset);

                    if (tileX < 0 || tileX > 63 || tileY < 0 || tileY > 63)
                        continue;

                    if (!currentWDT.Value.tiles.Contains((tileX, tileY)))
                        continue;

                    var mapTile = new MapTile
                    {
                        tileX = tileX,
                        tileY = tileY,
                        wdtFileDataID = CurrentWDTFileDataID
                    };

                    usedTiles.Add(mapTile);

                    if (!loadedTiles.Contains(mapTile) && !tilesToLoad.Contains(mapTile))
                    {
                        //Console.WriteLine($"Queuing tile {mapTile.tileX},{mapTile.tileY} for load (3x3 around camera, which is in tile {x},{y})");
                        tilesToLoad.Enqueue(mapTile);
                        totalTilesToLoad++;
                    }
                }
            }

            foreach (var tile in loadedTiles.ToList())
            {
                if (!usedTiles.Contains(tile))
                {
                    //Console.WriteLine($"Releasing tile {tile.tileX},{tile.tileY} as it's no longer in the 3x3 around the camera");
                    loadedTiles.Remove(tile);

                    lock (SceneObjectLock)
                    {
                        UpdateInstanceList();

                        var adtToRemove = SceneObjects.FirstOrDefault(x => x is ADTContainer adt && adt.mapTile.wdtFileDataID == tile.wdtFileDataID && adt.mapTile.tileX == tile.tileX && adt.mapTile.tileY == tile.tileY) as ADTContainer;
                        if (adtToRemove != null)
                        {
                            SceneObjects.Remove(adtToRemove);
                            Cache.ReleaseADT(_gl, adtToRemove.mapTile, adtToRemove.mapTile.wdtFileDataID);

                            List<WMOContainer> wmosToRemove = [.. SceneObjects.Where(x => x is WMOContainer wmo && wmo.ParentFileDataId == adtToRemove.Terrain.rootADTFileDataID).Select(x => (WMOContainer)x)];
                            foreach (var wmo in wmosToRemove)
                            {
                                if (uuidUsers.TryGetValue(wmo.UniqueID, out var count))
                                {
                                    if (count > 1)
                                    {
                                        uuidUsers[wmo.UniqueID] = count - 1;
                                    }
                                    else
                                    {
                                        SceneObjects.Remove(wmo);
                                        Cache.ReleaseWMO(_gl, wmo.FileDataId, wmo.ParentFileDataId);
                                        uuidUsers.Remove(wmo.UniqueID);
                                    }
                                }
                            }

                            List<M2Container> m2sToRemove = [.. SceneObjects.Where(x => x is M2Container m2 && m2.ParentFileDataId == adtToRemove.Terrain.rootADTFileDataID).Select(x => (M2Container)x)];
                            foreach (var m2 in m2sToRemove)
                            {
                                SceneObjects.Remove(m2);
                                Cache.ReleaseM2(_gl, m2.FileDataId, m2.ParentFileDataId);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateInstanceList()
        {
            wmoInstances.Clear();
            m2Instances.Clear();

            foreach (var sceneObject in SceneObjects)
            {
                if (sceneObject is WMOContainer wmo)
                {
                    if (!wmoInstances.ContainsKey(wmo.FileDataId))
                        wmoInstances[wmo.FileDataId] = [];

                    wmoInstances[wmo.FileDataId].Add(wmo);
                }
                else if (sceneObject is M2Container m2)
                {
                    if (!m2Instances.ContainsKey(m2.FileDataId))
                        m2Instances[m2.FileDataId] = [];

                    m2Instances[m2.FileDataId].Add(m2);
                }
            }
        }

        public bool ProcessQueue()
        {
            // If no ADTs are queued, but other files still are, we return true (and not dequeue tiles) to keep calling this function over and over to handle the various uploads, because these need to be called from this thread, but this does block new ADTs from loading until these are done which isn't ideal.

            // BLP
            Cache.UploadDecodedBLPs();

            if (tilesToLoad.Count == 0)
            {
                var blpRemaining = Cache.GetBLPLoadQueueCount();
                if (blpRemaining > 0)
                {
                    StatusMessage = $"Loading textures ({blpRemaining} queued)...";
                    return true;
                }
                else
                {
                    // Nothing to do, clear status and return
                    StatusMessage = "";
                    return false;
                }
            }

            // TODO: WMO
            // TODO: M2

            var mapTile = tilesToLoad.Dequeue();
            var tilesLoaded = totalTilesToLoad - tilesToLoad.Count;
            var blpQueueCount = Cache.GetBLPLoadQueueCount();
            StatusMessage = $"Loading tile {mapTile.tileX},{mapTile.tileY} ({tilesLoaded}/{totalTilesToLoad})";

            if (blpQueueCount > 0)
                StatusMessage += $" | (busy loading textures ({blpQueueCount} queued)";

            var timer = new Stopwatch();
            timer.Start();

            Terrain adt;

            try
            {
                adt = Cache.GetOrLoadADT(_gl, mapTile, adtShaderProgram, mapTile.wdtFileDataID);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading ADT: " + ex.ToString());
                return false;
            }

            timer.Stop();

            lock (SceneObjectLock)
            {
                var adtContainer = new ADTContainer(_gl, adt, mapTile, adtShaderProgram);
                SceneObjects.Add(adtContainer);

                foreach (var worldModel in adt.worldModelBatches)
                {
                    if (uuidUsers.ContainsKey(worldModel.uniqueID))
                        continue;

                    var worldModelContainer = new WMOContainer(_gl, worldModel.fileDataID, wmoShaderProgram, adt.rootADTFileDataID)
                    {
                        Position = worldModel.position,
                        Rotation = worldModel.rotation,
                        Scale = worldModel.scale == 0 ? 1 : worldModel.scale,
                        UniqueID = worldModel.uniqueID
                    };

                    SceneObjects.Add(worldModelContainer);

                    if (uuidUsers.TryGetValue(worldModel.uniqueID, out var count))
                        uuidUsers[worldModel.uniqueID] = count + 1;
                    else
                        uuidUsers[worldModel.uniqueID] = 1;
                }

                foreach (var doodad in adt.doodads)
                {
                    var doodadContainer = new M2Container(_gl, doodad.fileDataID, m2ShaderProgram, adt.rootADTFileDataID)
                    {
                        Position = doodad.position,
                        Rotation = doodad.rotation,
                        Scale = doodad.scale
                    };

                    SceneObjects.Add(doodadContainer);
                }

                UpdateInstanceList();
            }

            loadedTiles.Add(mapTile);

            return true;
        }

        public void PerformRaycast(float mouseX, float mouseY, Camera camera, int windowWidth, int windowHeight)
        {
            var ray = camera.GetRayFromScreen(mouseX, mouseY, windowWidth, windowHeight);

            Container3D? closestObject = null;
            float closestDistance = float.MaxValue;

            lock (SceneObjectLock)
            {
                foreach (var sceneObject in SceneObjects)
                {
                    if (sceneObject is ADTContainer)
                        continue;

                    if (!RenderWMO && sceneObject is WMOContainer)
                        continue;

                    if (!RenderM2 && sceneObject is M2Container)
                        continue;

                    if(sceneObject.IsSelected)
                        continue;

                    var sphere = sceneObject.GetBoundingSphere();
                    if (sphere.HasValue)
                    {
                        if (IntersectionTests.RayIntersectsSphere(ray, sphere.Value, out float sphereDistance))
                        {
                            if (sphereDistance < closestDistance)
                            {
                                var box = sceneObject.GetBoundingBox();
                                if (box.HasValue && IntersectionTests.RayIntersectsBox(ray, box.Value, out float boxDistance))
                                {
                                    if (boxDistance < closestDistance)
                                    {
                                        closestDistance = boxDistance;
                                        closestObject = sceneObject;
                                    }
                                }
                                else if (!box.HasValue)
                                {
                                    closestDistance = sphereDistance;
                                    closestObject = sceneObject;
                                }
                            }
                        }
                    }
                }
            }

            SelectedObject?.IsSelected = false;
            SelectedObject = closestObject;
            SelectedObject?.IsSelected = true;
        }

        public unsafe void RenderScene(Camera camera, out bool gizmoWasUsing, out bool gizmoWasOver)
        {
            var projectionMatrix = camera.GetProjectionMatrix();
            var viewMatrix = camera.GetViewMatrix();
            viewMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);

            camera.UpdateFrustum();
            var frustum = camera.GetFrustum();

            foreach (var instance in m2Instances)
            {
                if (!RenderM2)
                    break;

                var instances = instance.Value;
                if (instances.Count == 0) continue;

                var fileDataId = instance.Key;

                var firstInstance = instances[0];
                var m2 = Cache.GetOrLoadM2(_gl, fileDataId, m2ShaderProgram, firstInstance.ParentFileDataId);
                _gl.UseProgram(m2ShaderProgram);
                _gl.Uniform3(5, LightDirection.X, LightDirection.Y, LightDirection.Z);

                _gl.UniformMatrix4(0, 1, false, (float*)&projectionMatrix);
                _gl.UniformMatrix4(1, 1, false, (float*)&viewMatrix);

                var matrices = new Matrix4x4[Math.Min(instances.Count, MaxInstancesPerBatch)];

                var identityMatrix = Matrix4x4.Identity;
                _gl.UniformMatrix4(2, 1, false, (float*)&identityMatrix);

                SetupInstanceAttributes(m2.vao);
                _gl.BindVertexArray(m2.vao);

                for (int batchStart = 0; batchStart < instances.Count; batchStart += MaxInstancesPerBatch)
                {
                    int batchSize = Math.Min(MaxInstancesPerBatch, instances.Count - batchStart);

                    for (int i = 0; i < batchSize; i++)
                    {
                        matrices[i] = BuildModelMatrix(instances[batchStart + i]);
                    }

                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, instanceMatrixVBO);
                    fixed (Matrix4x4* ptr = matrices)
                    {
                        _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(batchSize * sizeof(float) * 16), ptr);
                    }
                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

                    for (var i = 0; i < m2.submeshes.Length; i++)
                    {
                        var submesh = m2.submeshes[i];
                        // i am ignoring that geosets can be enabled/disabled in theory here, hopefully not relevant for doodads...

                        SwitchBlendMode((int)submesh.blendType, _gl, m2AlphaRefLoc);

                        _gl.ActiveTexture(TextureUnit.Texture0);
                        _gl.BindTexture(TextureTarget.Texture2D, submesh.material);
                        _gl.DrawElementsInstanced(PrimitiveType.Triangles, submesh.numFaces, DrawElementsType.UnsignedInt, (void*)(submesh.firstFace * 4), (uint)batchSize);
                        _gl.BindTexture(TextureTarget.Texture2D, 0);
                    }
                }
            }

            foreach (var instance in wmoInstances)
            {
                if (!RenderWMO)
                    break;

                var instances = instance.Value;
                if (instances.Count == 0) continue;

                var fileDataId = instance.Key;

                var firstInstance = instances[0];
                var wmo = Cache.GetOrLoadWMO(_gl, fileDataId, wmoShaderProgram, firstInstance.ParentFileDataId);

                _gl.UseProgram(wmoShaderProgram);
                _gl.Uniform3(5, LightDirection.X, LightDirection.Y, LightDirection.Z);

                _gl.UniformMatrix4(0, 1, false, (float*)&projectionMatrix);
                _gl.UniformMatrix4(1, 1, false, (float*)&viewMatrix);

                var matrices = new Matrix4x4[Math.Min(instances.Count, MaxInstancesPerBatch)];

                for (var j = 0; j < wmo.wmoRenderBatch.Length; j++)
                {
                    var batch = wmo.wmoRenderBatch[j];
                    if (wmo.groupBatches[batch.groupID].vao != 0)
                    {
                        SetupInstanceAttributes(wmo.groupBatches[batch.groupID].vao);
                    }
                }

                var visibleIndices = new List<int>();
                for (int i = 0; i < instances.Count; i++)
                {
                    var sphere = instances[i].GetBoundingSphere();
                    if (sphere.HasValue && frustum.IsSphereVisible(sphere.Value.Center, sphere.Value.Radius))
                    {
                        visibleIndices.Add(i);
                    }
                }

                if (visibleIndices.Count == 0)
                    continue;

                for (int batchStart = 0; batchStart < visibleIndices.Count; batchStart += MaxInstancesPerBatch)
                {
                    int batchSize = Math.Min(MaxInstancesPerBatch, visibleIndices.Count - batchStart);

                    for (int i = 0; i < batchSize; i++)
                    {
                        matrices[i] = BuildModelMatrix(instances[visibleIndices[batchStart + i]]);
                    }

                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, instanceMatrixVBO);
                    fixed (Matrix4x4* ptr = matrices)
                    {
                        _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(batchSize * sizeof(float) * 16), ptr);
                    }
                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

                    for (var j = 0; j < wmo.wmoRenderBatch.Length; j++)
                    {
                        var batch = wmo.wmoRenderBatch[j];

                        if (wmo.groupBatches[batch.groupID].vao == 0)
                            continue;

                        _gl.BindVertexArray(wmo.groupBatches[batch.groupID].vao);

                        var vertexShaderID = (byte)ShaderEnums.WMOShaders[(int)batch.shader].VertexShader;
                        var pixelShaderID = (byte)ShaderEnums.WMOShaders[(int)batch.shader].PixelShader;

                        if (lastRenderState.lastWMOVertexShaderID != vertexShaderID)
                        {
                            _gl.Uniform1(3, (float)vertexShaderID);
                            lastRenderState.lastWMOVertexShaderID = vertexShaderID;
                        }

                        if (lastRenderState.lastWMOPixelShaderID != pixelShaderID)
                        {
                            _gl.Uniform1(4, (float)pixelShaderID);
                            lastRenderState.lastWMOPixelShaderID = pixelShaderID;
                        }

                        SwitchBlendMode((int)batch.blendType, _gl, wmoAlphaRefLoc);

                        for (var m = 0; m < batch.materialID.Length; m++)
                        {
                            _gl.ActiveTexture(TextureUnit.Texture0 + m);
                            if (batch.materialID[m] == -1)
                                _gl.BindTexture(TextureTarget.Texture2D, defaultTextureID);
                            else
                                _gl.BindTexture(TextureTarget.Texture2D, (uint)batch.materialID[m]);
                        }

                        _gl.DrawElementsInstanced(PrimitiveType.Triangles, batch.numFaces, DrawElementsType.UnsignedShort, (void*)(batch.firstFace * 2), (uint)batchSize);

                        for (var m = 0; m < batch.materialID.Length; m++)
                        {
                            _gl.ActiveTexture(TextureUnit.Texture0 + m);
                            _gl.BindTexture(TextureTarget.Texture2D, 0);
                        }
                    }
                }
            }

            // Render ADTs
            foreach (var sceneObject in SceneObjects)
            {
                if (sceneObject is ADTContainer adt)
                {
                    if (!RenderADT)
                        continue;

                    _gl.UseProgram(adtShaderProgram);
                    _gl.Uniform3(5, LightDirection.X, LightDirection.Y, LightDirection.Z);

                    var adtModelviewMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);
                    _gl.UniformMatrix4(0, 1, false, (float*)&adtModelviewMatrix);

                    _gl.UniformMatrix4(1, 1, false, (float*)&projectionMatrix);

                    var adtViewMatrix = camera.GetViewMatrix();
                    _gl.UniformMatrix4(2, 1, false, (float*)&adtViewMatrix);

                    _gl.BindVertexArray(adt.Terrain.vao);
                    _gl.Disable(EnableCap.Blend);

                    for (int c = 0; c < 256; c++)
                    {
                        var bounds = adt.Terrain.chunkBounds[c];
                        if (!frustum.IsBoxVisible(bounds.min, bounds.max))
                            continue;

                        var batch = adt.Terrain.renderBatches[c];

                        for (int j = 0; j < 2; j++)
                        {
                            _gl.Uniform1(alphaLayerUniforms[j], j);
                            _gl.ActiveTexture(TextureUnit.Texture0 + j);
                            _gl.BindTexture(TextureTarget.Texture2D, (batch.alphaMaterialID[j]) == -1 ? defaultTextureID : (uint)batch.alphaMaterialID[j]);
                        }

                        for (int j = 0; j < 8; j++)
                        {
                            _gl.Uniform1(heightScaleUniforms[j], batch.heightScales[j]);
                            _gl.Uniform1(heightOffsetUniforms[j], batch.heightOffsets[j]);
                            _gl.Uniform1(layerScaleUniforms[j], batch.scales[j]);

                            _gl.Uniform1(diffuseLayerUniforms[j], j + 7);
                            _gl.ActiveTexture(TextureUnit.Texture7 + j);
                            _gl.BindTexture(TextureTarget.Texture2D, (batch.materialID[j]) == -1 ? defaultTextureID : (uint)batch.materialID[j]);
                            _gl.Uniform1(heightLayerUniforms[j], j + 15);
                            _gl.ActiveTexture(TextureUnit.Texture15 + j);
                            _gl.BindTexture(TextureTarget.Texture2D, (batch.heightMaterialIDs[j]) == -1 ? defaultTextureID : (uint)batch.heightMaterialIDs[j]);
                        }

                        _gl.DrawElements(PrimitiveType.Triangles, (uint)((c + 1) * 768) - (uint)c * 768, DrawElementsType.UnsignedInt, (void*)((c * 768) * 4));
                    }
                }
            }

            _gl.BindVertexArray(0);

            gizmoWasUsing = false;
            gizmoWasOver = false;
        }

        public void RenderDebug(Camera camera, out bool gizmoWasUsing, out bool gizmoWasOver)
        {
            if (debugRenderer == null)
            {
                gizmoWasUsing = false;
                gizmoWasOver = false;
                return;
            }

            debugRenderer.Clear();

            gizmoWasUsing = false;
            gizmoWasOver = false;

            lock (SceneObjectLock)
            {
                foreach (var sceneObject in SceneObjects)
                {
                    if (sceneObject is ADTContainer)
                        continue;

                    if (!RenderWMO && sceneObject is WMOContainer && !sceneObject.IsSelected)
                        continue;

                    if (!RenderM2 && sceneObject is M2Container && !sceneObject.IsSelected)
                        continue;

                    var color = sceneObject.IsSelected ? new Vector4(0, 1, 0, 1) : new Vector4(1, 1, 0, 1);

                    if (ShowBoundingBoxes || sceneObject.IsSelected)
                    {
                        var box = sceneObject.GetBoundingBox();
                        if (box.HasValue)
                        {
                            debugRenderer.DrawBox(box.Value.Min, box.Value.Max, color);
                        }
                    }

                    if (ShowBoundingSpheres)
                    {
                        var sphere = sceneObject.GetBoundingSphere();
                        if (sphere.HasValue)
                        {
                            debugRenderer.DrawSphere(sphere.Value.Center, sphere.Value.Radius, color);
                        }
                    }
                }
            }

            var debugViewMatrix = camera.GetViewMatrix();
            debugViewMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);
            var projectionMatrix = camera.GetProjectionMatrix();
            debugRenderer.Render(projectionMatrix, debugViewMatrix);
        }

        public static (byte x, byte y) GetTileFromPosition(Vector3 position)
        {
            const float tileSize = 533.33333f;
            const int mapCenter = 32;

            var posX = position.Y / tileSize;
            var posY = position.X / tileSize;

            int tileX = mapCenter - (int)Math.Ceiling(posX);
            int tileY = mapCenter - (int)Math.Ceiling(posY);

            tileX = Math.Clamp(tileX, 0, 63);
            tileY = Math.Clamp(tileY, 0, 63);

            return ((byte)tileX, (byte)tileY);
        }

        private unsafe void SetupInstanceBuffer()
        {
            instanceMatrixVBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, instanceMatrixVBO);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxInstancesPerBatch * sizeof(float) * 16), null, BufferUsageARB.DynamicDraw);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        }

        private Matrix4x4 BuildModelMatrix(Container3D container)
        {
            var modelMatrix = Matrix4x4.CreateScale(container.Scale);
            modelMatrix *= Matrix4x4.CreateRotationX(MathF.PI / 180f * container.Rotation.Z);
            modelMatrix *= Matrix4x4.CreateRotationY(MathF.PI / 180f * container.Rotation.X);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (container.Rotation.Y + 90f));
            modelMatrix *= Matrix4x4.CreateTranslation(container.Position.X, container.Position.Z * -1, container.Position.Y);
            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);
            return modelMatrix;
        }

        private unsafe void SetupInstanceAttributes(uint vao)
        {
            _gl.BindVertexArray(vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, instanceMatrixVBO);

            for (uint i = 0; i < 4; i++)
            {
                uint location = 10 + i;
                _gl.EnableVertexAttribArray(location);
                _gl.VertexAttribPointer(location, 4, VertexAttribPointerType.Float, false, sizeof(float) * 16, (void*)(sizeof(float) * 4 * i));
                _gl.VertexAttribDivisor(location, 1);
            }

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
        }

        private unsafe uint MakeDefaultTexture()
        {
            var defaultTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, defaultTexture);
            byte[] fill = [0, 0, 0, 0];
            fixed (byte* fillPtr = fill)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, fillPtr);
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            return defaultTexture;
        }

        private static void SwitchBlendMode(int blendType, GL gl, int alphaRefLoc)
        {
            switch (blendType)
            {
                case 0:
                    gl.Disable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    break;
                case 1:
                    gl.Disable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, 0.90393700787f);
                    break;
                case 2:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    break;
                case 3:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);
                    break;
                case 4:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.Zero, BlendingFactor.DstAlpha, BlendingFactor.Zero);
                    break;
                case 5:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.SrcColor, BlendingFactor.DstAlpha, BlendingFactor.SrcAlpha);
                    break;
                case 6:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.One, BlendingFactor.DstAlpha, BlendingFactor.One);
                    break;
                case 7:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One);
                    break;
                case 8:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero);
                    break;
                case 9:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.Zero, BlendingFactor.SrcAlpha, BlendingFactor.Zero);
                    break;
                case 10:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);
                    break;
                case 11:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.ConstantAlpha, BlendingFactor.OneMinusConstantAlpha, BlendingFactor.ConstantAlpha, BlendingFactor.OneMinusConstantAlpha);
                    break;
                case 12:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.OneMinusDstColor, BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero);
                    break;
                case 13:
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                    break;
                default:
                    throw new Exception("Unsupport blend mode: " + blendType);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Cache.StopBLPLoader();

                debugRenderer?.Dispose();

                if (instanceMatrixVBO != 0)
                {
                    _gl.DeleteBuffer(instanceMatrixVBO);
                    instanceMatrixVBO = 0;
                }
            }
        }
    }
}
