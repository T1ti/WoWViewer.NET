using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WoWFormatLib.Structs.WDT;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Managers
{
    public class SceneManager(ComPtr<ID3D11Device> device, ComPtr<IDXGISwapChain1> swapchain, ComPtr<ID3D11DeviceContext> deviceContext, ShaderManager shaderManager) : IDisposable
    {
        private readonly ShaderManager _shaderManager = shaderManager ?? throw new ArgumentNullException(nameof(shaderManager));
        public List<Container3D> SceneObjects { get; } = [];
        public Lock SceneObjectLock { get; } = new();

        private readonly Queue<MapTile> tilesToLoad = new();
        private int totalTilesToLoad = 0;
        private readonly Dictionary<uint, uint> uuidUsers = [];
        private readonly HashSet<MapTile> loadedTiles = [];

        private WDT? currentWDT;
        public uint CurrentWDTFileDataID { get; private set; } = 775971;

        private uint OpsPerFrame = 5;
        private uint CurrentOps = 0;

        public Container3D? SelectedObject { get; set; } = null;

        public bool ShowBoundingBoxes { get; set; } = false;
        public bool ShowBoundingSpheres { get; set; } = false;

        public bool RenderADT { get; set; } = true;
        public bool RenderWMO { get; set; } = true;
        public bool RenderM2 { get; set; } = true;

        public Vector3 LightDirection { get; set; } = new Vector3(0.5f, 1f, 0.5f);

        private const int MaxInstancesPerBatch = 1024;

        private CompiledShader adtShaderProgram;
        private CompiledShader wmoShaderProgram;
        private CompiledShader m2ShaderProgram;
        private CompiledShader debugShaderProgram;
        private CompiledShader bboxShaderProgram;

        public readonly Dictionary<(uint FileDataID, int EnabledGroupHash), List<WMOContainer>> wmoInstances = [];
        public readonly Dictionary<uint, List<M2Container>> m2Instances = [];

        private static RenderState lastRenderState;
        private struct RenderState
        {
            public byte lastWMOVertexShaderID;
            public byte lastWMOPixelShaderID;
        }

        private int m2AlphaRefLoc;
        private int wmoAlphaRefLoc;

        private readonly ComPtr<IDXGISwapChain1> _swapChain = swapchain;
        private ComPtr<ID3D11Buffer> adtPerObjectConstantBuffer = default;
        private ComPtr<ID3D11Buffer> layerDataConstantBuffer = default;
        private ComPtr<ID3D11Buffer> wmoPerObjectConstantBuffer = default;
        private ComPtr<ID3D11Buffer> m2PerObjectConstantBuffer = default;
        private ComPtr<ID3D11Buffer> instanceMatrixBuffer = default;
        private ComPtr<ID3D11DepthStencilView> depthStencilView = default;
        private ComPtr<ID3D11DepthStencilState> bboxDepthStencilState = default;
        private ComPtr<ID3D11Texture2D> depthTexture = default;
        private ComPtr<ID3D11SamplerState> textureSampler = default;
        private ComPtr<ID3D11SamplerState> clampSampler = default;
        private ComPtr<ID3D11RenderTargetView> renderTargetView = default;
        private ComPtr<ID3D11RasterizerState> rasterizerState = default;
        private ComPtr<ID3D11RasterizerState> wmoRasterizerState = default;
        private ComPtr<ID3D11RasterizerState> wireframeRasterizerState = default;
        private ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        private ComPtr<ID3D11ShaderResourceView> defaultTexture;
        private ComPtr<ID3D11Buffer> bboxConstantBuffer = default;
        private ComPtr<ID3D11Buffer> bboxVertexBuffer = default;

        private uint _renderWidth = 1920;
        private uint _renderHeight = 1080;

        public int visibleChunks { get; private set; } = 0;
        public int visibleWMOs { get; private set; } = 0;
        public int visibleM2s { get; private set; } = 0;

        public bool SceneLoaded => loadedTiles.Count > 0; // this won't work for WMO only maps
        public string StatusMessage { get; private set; } = "";

        public void Initialize(ShaderManager shaderManager, CompiledShader adtShader, CompiledShader wmoShader, CompiledShader m2Shader, CompiledShader bboxShader)
        {
            adtShaderProgram = adtShader;
            wmoShaderProgram = wmoShader;
            m2ShaderProgram = m2Shader;
            bboxShaderProgram = bboxShader;

            // debugRenderer = new DebugRenderer(_gl, debugShaderProgram);
            defaultTexture = BLPLoader.CreatePlaceholderTexture(device);

            // Create PerObject constant buffer (matches cbuffer PerObject in adt.hlsl)
            unsafe
            {
                // SAMPLERS
                var samplerDesc = new SamplerDesc
                {
                    Filter = Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Wrap,
                    AddressV = TextureAddressMode.Wrap,
                    AddressW = TextureAddressMode.Wrap,
                    MipLODBias = 0,
                    MaxAnisotropy = 1,
                    MinLOD = float.MinValue,
                    MaxLOD = float.MaxValue,
                };
                samplerDesc.BorderColor[0] = 0.0f;
                samplerDesc.BorderColor[1] = 0.0f;
                samplerDesc.BorderColor[2] = 0.0f;
                samplerDesc.BorderColor[3] = 1.0f;

                SilkMarshal.ThrowHResult(device.CreateSamplerState(in samplerDesc, ref textureSampler));

                var clampSamplerDesc = new SamplerDesc
                {
                    Filter = Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    MipLODBias = 0,
                    MaxAnisotropy = 1,
                    MinLOD = float.MinValue,
                    MaxLOD = float.MaxValue,
                };
                clampSamplerDesc.BorderColor[0] = 0.0f;
                clampSamplerDesc.BorderColor[1] = 0.0f;
                clampSamplerDesc.BorderColor[2] = 0.0f;
                clampSamplerDesc.BorderColor[3] = 1.0f;

                SilkMarshal.ThrowHResult(device.CreateSamplerState(in clampSamplerDesc, ref clampSampler));

                // PER OBJECT CONSTANT BUFFERS
                var bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)Marshal.SizeOf<ADTPerObjectCB>(),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, null, ref adtPerObjectConstantBuffer));

                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)sizeof(WMOPerObjectCB),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, null, ref wmoPerObjectConstantBuffer));

                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)sizeof(M2PerObjectCB),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, null, ref m2PerObjectConstantBuffer));

                // Instance buffer
                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)(MaxInstancesPerBatch * sizeof(Matrix4x4)),
                    Usage = Usage.Dynamic,
                    BindFlags = (uint)BindFlag.VertexBuffer,
                    CPUAccessFlags = (uint)CpuAccessFlag.Write
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, null, ref instanceMatrixBuffer));

                // ADT layer data
                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)Marshal.SizeOf<LayerData>(),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, null, ref layerDataConstantBuffer));

                // Bounding box 
                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)sizeof(BBoxCB),
                    Usage = Usage.Dynamic,
                    BindFlags = (uint)BindFlag.ConstantBuffer,
                    CPUAccessFlags = (uint)CpuAccessFlag.Write
                };
                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, null, ref bboxConstantBuffer));

                // Rasterizers, need to be merged once ADTs are fixed
                var rastDesc = new RasterizerDesc
                {
                    FillMode = FillMode.Solid,
                    CullMode = CullMode.Back, // TODO: Fix, then merge rasterizers
                    FrontCounterClockwise = false,
                    DepthClipEnable = true
                };

                SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rastDesc, ref rasterizerState));
                deviceContext.RSSetState(rasterizerState);

                var wmoRastDesc = new RasterizerDesc
                {
                    FillMode = FillMode.Solid,
                    CullMode = CullMode.Front,
                    FrontCounterClockwise = false,
                    DepthClipEnable = true
                };
                SilkMarshal.ThrowHResult(device.CreateRasterizerState(in wmoRastDesc, ref wmoRasterizerState));

                var wireframeDesc = new RasterizerDesc
                {
                    FillMode = FillMode.Wireframe,
                    CullMode = CullMode.None,
                    FrontCounterClockwise = false,
                    DepthClipEnable = true
                };
                SilkMarshal.ThrowHResult(device.CreateRasterizerState(in wireframeDesc, ref wireframeRasterizerState));

                var bboxStencilDesc = new DepthStencilDesc
                {
                    DepthEnable = false,
                    DepthWriteMask = DepthWriteMask.Zero,
                    DepthFunc = ComparisonFunc.Always,
                    StencilEnable = false
                };
                SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in bboxStencilDesc, ref bboxDepthStencilState));

                ComPtr<ID3D11RasterizerState> rastState = default;
                device.CreateRasterizerState(in rastDesc, ref rastState);
                deviceContext.RSSetState(rastState);

                CreateBBoxBuffers();
                CreateSizeDependentResources(1920, 1080);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BBoxCB
        {
            public Matrix4x4 projection_matrix;
            public Matrix4x4 view_matrix;
            public Matrix4x4 model_matrix;
            public Vector4 color;
        }

        private unsafe void CreateBBoxBuffers()
        {
            var vbDesc = new BufferDesc
            {
                ByteWidth = (uint)(24 * sizeof(Vector3)),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write
            };
            SilkMarshal.ThrowHResult(device.CreateBuffer(in vbDesc, null, ref bboxVertexBuffer));
        }

        private unsafe void CreateSizeDependentResources(uint width, uint height)
        {
            using var framebuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(framebuffer, null, ref renderTargetView));

            Texture2DDesc backbufferDesc = default;
            framebuffer.GetDesc(ref backbufferDesc);
            uint actualWidth = backbufferDesc.Width;
            uint actualHeight = backbufferDesc.Height;

            var depthDesc = new Texture2DDesc
            {
                Width = actualWidth,
                Height = actualHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.FormatD24UnormS8Uint,
                SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.DepthStencil,
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in depthDesc, null, ref depthTexture));
            SilkMarshal.ThrowHResult(device.CreateDepthStencilView(depthTexture, null, ref depthStencilView));

            var viewport = new Viewport
            {
                TopLeftX = 0,
                TopLeftY = 0,
                Width = actualWidth,
                Height = actualHeight,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };
            deviceContext.RSSetViewports(1, in viewport);

            _renderWidth = actualWidth;
            _renderHeight = actualHeight;
        }

        public unsafe void Resize(uint width, uint height)
        {
            deviceContext.OMSetRenderTargets(0, (ID3D11RenderTargetView**)null, (ID3D11DepthStencilView*)null);

            if (renderTargetView.Handle != null) { renderTargetView.Dispose(); renderTargetView = default; }
            if (depthStencilView.Handle != null) { depthStencilView.Dispose(); depthStencilView = default; }
            if (depthTexture.Handle != null) { depthTexture.Dispose(); depthTexture = default; }

            CreateSizeDependentResources(width, height);
        }

        public void LoadWDT(uint wdtFileDataID)
        {
            if (CurrentWDTFileDataID != wdtFileDataID)
            {
                loadedTiles.Clear();

                lock (SceneObjectLock)
                    SceneObjects.Clear();

                CurrentWDTFileDataID = wdtFileDataID;
                currentWDT = WDTCache.GetOrLoad(CurrentWDTFileDataID);
            }
        }

        public void PreloadTEX()
        {
            if (currentWDT == null)
                return;

            var texFileDataID = currentWDT.Value.mphd.texFDID;
            if (texFileDataID != 0)
                TEXCache.Preload(texFileDataID);
        }

        public WDT? GetCurrentWDT()
        {
            currentWDT ??= WDTCache.GetOrLoad(CurrentWDTFileDataID);
            return currentWDT;
        }

        public (byte x, byte y) GetFirstMapTile()
        {
            if (currentWDT == null || currentWDT.Value.tiles.Count == 0)
                return (0, 0);

            return currentWDT.Value.tiles[0];
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
                        tilesToLoad.Enqueue(mapTile);
                        totalTilesToLoad++;
                    }
                }
            }

            foreach (var tile in loadedTiles.ToList())
            {
                if (!usedTiles.Contains(tile))
                {
                    loadedTiles.Remove(tile);

                    lock (SceneObjectLock)
                    {
                        UpdateInstanceList();

                        var adtToRemove = SceneObjects.FirstOrDefault(x => x is ADTContainer adt && adt.mapTile.wdtFileDataID == tile.wdtFileDataID && adt.mapTile.tileX == tile.tileX && adt.mapTile.tileY == tile.tileY) as ADTContainer;
                        if (adtToRemove != null)
                        {
                            SceneObjects.Remove(adtToRemove);
                            ADTCache.Release(device, adtToRemove.mapTile, adtToRemove.mapTile.wdtFileDataID);

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
                                        foreach (var doodad in wmo.ActiveDoodads)
                                        {
                                            SceneObjects.Remove(doodad);
                                            M2Cache.Release(doodad.FileDataId, doodad.ParentFileDataId);
                                        }
                                        wmo.ActiveDoodads.Clear();

                                        SceneObjects.Remove(wmo);
                                        WMOCache.Release(wmo.FileDataId, wmo.ParentFileDataId);
                                        uuidUsers.Remove(wmo.UniqueID);
                                    }
                                }
                            }

                            List<M2Container> m2sToRemove = [.. SceneObjects.Where(x => x is M2Container m2 && m2.ParentFileDataId == adtToRemove.Terrain.rootADTFileDataID).Select(x => (M2Container)x)];
                            foreach (var m2 in m2sToRemove)
                            {
                                SceneObjects.Remove(m2);
                                M2Cache.Release(m2.FileDataId, m2.ParentFileDataId);
                            }
                        }
                    }
                }
            }

            if (loadedTiles.Count == 0)
            {
                if (WMOCache.GetCacheCount() > 0)
                    WMOCache.ReleaseAll();

                if (M2Cache.GetCacheCount() > 0)
                    M2Cache.ReleaseAll();

                if (BLPCache.GetCacheCount() > 0)
                    BLPCache.ReleaseAll();
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
        }

        public void UpdateWMOInstanceList()
        {
            wmoInstances.Clear();
            foreach (var sceneObject in SceneObjects)
            {
                if (sceneObject is WMOContainer wmo)
                {
                    var key = (wmo.FileDataId, wmo.EnabledGroups.GetHashCode());
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
                    var key = (wmo.FileDataId, wmo.EnabledGroups.GetHashCode());
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
        }

        private void SpawnWMODoodads(WMOContainer wmoContainer)
        {
            var wmo = WMOCache.GetOrLoad(device, wmoContainer.FileDataId, wmoShaderProgram, wmoContainer.ParentFileDataId, false);
            var enabledSets = wmoContainer.EnabledDoodadSets;

            wmoContainer.ActiveDoodads.Clear();

            foreach (var doodad in wmo.doodads)
            {
                if (!enabledSets[doodad.doodadSet])
                    continue;

                var m2Container = new M2Container(device, doodad.filedataid, m2ShaderProgram, wmoContainer.ParentFileDataId)
                {
                    ParentWMO = wmoContainer,
                    LocalPosition = doodad.position,
                    LocalRotation = doodad.rotation,
                    LocalScale = doodad.scale,
                };

                lock (SceneObjectLock)
                    SceneObjects.Add(m2Container);

                wmoContainer.ActiveDoodads.Add(m2Container);
            }
        }

        public void RefreshWMODoodads(WMOContainer wmoContainer)
        {
            if (!wmoContainer.IsLoaded)
                return;

            lock (SceneObjectLock)
            {
                foreach (var doodad in wmoContainer.ActiveDoodads)
                {
                    SceneObjects.Remove(doodad);
                    M2Cache.Release(doodad.FileDataId, doodad.ParentFileDataId);
                }

                SpawnWMODoodads(wmoContainer);

                UpdateInstanceList();
            }
        }

        public bool ProcessQueue()
        {
            // If no ADTs are queued, but other files still are, we return true (and not dequeue tiles) to keep calling this function over and over to handle the various uploads, because these need to be called from this thread, but this does block new ADTs from loading until these are done which isn't ideal.

            // WMO
            WMOCache.Upload(wmoShaderProgram);

            // BLP
            BLPCache.Upload();

            if (tilesToLoad.Count == 0)
            {
                var wmoRemaining = WMOCache.GetLoadQueueCount();
                var blpRemaining = BLPCache.GetQueueCount();

                if (wmoRemaining > 0)
                {
                    StatusMessage = $"Loading WMOs ({wmoRemaining} queued)...";
                    return true;
                }
                else if (blpRemaining > 0)
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

            // TODO: M2

            var mapTile = tilesToLoad.Dequeue();
            var tilesLoaded = totalTilesToLoad - tilesToLoad.Count;
            var wmoQueueCount = WMOCache.GetLoadQueueCount();
            var blpQueueCount = BLPCache.GetQueueCount();
            StatusMessage = $"Loading tile {mapTile.tileX},{mapTile.tileY} ({tilesLoaded}/{totalTilesToLoad})";

            if (wmoQueueCount > 0)
                StatusMessage += $" | (busy loading WMOs ({wmoQueueCount} queued)";

            if (blpQueueCount > 0)
                StatusMessage += $" | (busy loading textures ({blpQueueCount} queued)";

            var timer = new Stopwatch();
            timer.Start();

            Terrain adt;

            try
            {
                adt = ADTCache.GetOrLoad(device, mapTile, adtShaderProgram, mapTile.wdtFileDataID);
                CurrentOps++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading ADT: " + ex.ToString());
                return false;
            }

            timer.Stop();

            var adtContainer = new ADTContainer(device, adt, mapTile, adtShaderProgram);

            lock (SceneObjectLock)
                SceneObjects.Add(adtContainer);

            foreach (var worldModel in adt.worldModelBatches)
            {
                if (uuidUsers.ContainsKey(worldModel.uniqueID))
                    continue;

                WMOCache.GetOrLoad(device, worldModel.fileDataID, wmoShaderProgram, adt.rootADTFileDataID);

                var worldModelContainer = new WMOContainer(device, worldModel.fileDataID, wmoShaderProgram, adt.rootADTFileDataID)
                {
                    Position = worldModel.position,
                    Rotation = worldModel.rotation,
                    Scale = worldModel.scale == 0 ? 1 : worldModel.scale,
                    UniqueID = worldModel.uniqueID,
                    OnDoodadSetsChanged = RefreshWMODoodads
                };

                worldModelContainer.DoodadSetsToEnable.AddRange(worldModel.doodadSetIDs);

                lock (SceneObjectLock)
                    SceneObjects.Add(worldModelContainer);

                if (uuidUsers.TryGetValue(worldModel.uniqueID, out var count))
                    uuidUsers[worldModel.uniqueID] = count + 1;
                else
                    uuidUsers[worldModel.uniqueID] = 1;
            }

            var wmosToSpawn = SceneObjects.OfType<WMOContainer>().Where(w => w.IsLoaded && !w.DoodadsSpawned).ToList();
            foreach (var wmoContainer in wmosToSpawn)
            {
                SpawnWMODoodads(wmoContainer);
                wmoContainer.DoodadsSpawned = true;
            }

            foreach (var doodad in adt.doodads)
            {
                var doodadContainer = new M2Container(device, doodad.fileDataID, m2ShaderProgram, adt.rootADTFileDataID)
                {
                    Position = doodad.position,
                    Rotation = doodad.rotation,
                    Scale = doodad.scale
                };

                lock (SceneObjectLock)
                    SceneObjects.Add(doodadContainer);
            }

            UpdateInstanceList();

            loadedTiles.Add(mapTile);

            return true;
        }

        public void PerformRaycast(float mouseX, float mouseY, Camera camera, int windowWidth, int windowHeight)
        {
            // TODO: Untested with DX, bounding boxes likely need accurate transforming 
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

                    // Make doodads unselectable
                    if (sceneObject is M2Container m2container && m2container.ParentWMO != null)
                        continue;

                    if (sceneObject.IsSelected)
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

        public void RenderScene(Camera camera, out bool gizmoWasUsing, out bool gizmoWasOver)
        {
            deviceContext.RSSetState(rasterizerState);

#if DEBUG
            if (shaderManager.CheckForChanges())
            {
                adtShaderProgram = shaderManager.GetOrCompileShader("adt");
                wmoShaderProgram = shaderManager.GetOrCompileShader("wmo");
                m2ShaderProgram = shaderManager.GetOrCompileShader("m2");
            }
#endif

            var projectionMatrix = camera.GetProjectionMatrix();

            var cameraMatrix = camera.GetViewMatrix();

            camera.UpdateFrustum();

            var frustum = camera.GetFrustum();

            visibleM2s = 0;
            visibleWMOs = 0;
            visibleChunks = 0;

            var backgroundColour = new[] { 1.0f, 0.0f, 0.0f, 1.0f };

            deviceContext.ClearRenderTargetView(renderTargetView, ref backgroundColour[0]);
            deviceContext.OMSetRenderTargets(1, ref renderTargetView, depthStencilView);
            deviceContext.ClearDepthStencilView(depthStencilView, (uint)ClearFlag.Depth, 1.0f, 0);

            deviceContext.PSSetSamplers(0, 1, ref textureSampler);
            deviceContext.PSSetSamplers(1, 1, ref clampSampler);

            deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);

            var adtVertexStride = (uint)Marshal.SizeOf<ADTVertex>();
            var adtVertexOffset = 0U;

            uint wmoVertexStride = (uint)Marshal.SizeOf<WMOVertex>();
            uint wmoVertexOffset = 0;

            uint m2VertexStride = (uint)Marshal.SizeOf<M2Vertex>();
            uint m2VertexOffset = 0;

            uint instanceStride = 64; //matrix4x4 but marshal complained so hardcoded it 
            uint instanceOffset = 0;

            // Set up WMO stuff, we do this before the loop since they're all shared
            deviceContext.RSSetState(wmoRasterizerState);
            deviceContext.IASetInputLayout(wmoShaderProgram.InputLayout);
            deviceContext.VSSetShader(wmoShaderProgram.VertexShader, ref nullClassInstance, 0);
            deviceContext.PSSetShader(wmoShaderProgram.PixelShader, ref nullClassInstance, 0);
            deviceContext.PSSetSamplers(0, 1, ref textureSampler);
            deviceContext.VSSetConstantBuffers(0, 1, ref wmoPerObjectConstantBuffer);
            deviceContext.PSSetConstantBuffers(0, 1, ref wmoPerObjectConstantBuffer);

            foreach (var (key, instances) in wmoInstances)
            {
                if (!RenderWMO || instances.Count == 0)
                    continue;

                var firstInstance = instances[0];
                if (!firstInstance.IsLoaded)
                    continue;

                var visibleIndices = new List<int>();
                for (int i = 0; i < instances.Count; i++)
                {
                    var sphere = instances[i].GetBoundingSphere();
                    if (sphere.HasValue && frustum.IsSphereVisible(sphere.Value.Center, sphere.Value.Radius))
                    {
                        visibleWMOs++;
                        visibleIndices.Add(i);
                    }
                }

                if (visibleIndices.Count == 0)
                    continue;

                var wmo = WMOCache.GetOrLoad(device, firstInstance.FileDataId, wmoShaderProgram, firstInstance.ParentFileDataId, false);
                var enabledGroups = firstInstance.EnabledGroups;

                for (int batchStart = 0; batchStart < visibleIndices.Count; batchStart += MaxInstancesPerBatch)
                {
                    int batchSize = Math.Min(MaxInstancesPerBatch, visibleIndices.Count - batchStart);

                    // the normal approach to do updatesubresource doesn't work for dynamic buffers, so we have to do the below block instead
                    unsafe
                    {
                        MappedSubresource mapped = default;
                        SilkMarshal.ThrowHResult(deviceContext.Map(instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                        var dest = new Span<Matrix4x4>(mapped.PData, batchSize);
                        for (int i = 0; i < batchSize; i++)
                            dest[i] = instances[visibleIndices[batchStart + i]].GetModelMatrix();

                        deviceContext.Unmap(instanceMatrixBuffer, 0);
                    }

                    deviceContext.IASetVertexBuffers(1, 1, ref instanceMatrixBuffer, in instanceStride, in instanceOffset);

                    for (int j = 0; j < wmo.wmoRenderBatch.Length; j++)
                    {
                        var batch = wmo.wmoRenderBatch[j];
                        if (!enabledGroups[batch.groupID])
                            continue;

                        var group = wmo.groupBatches[batch.groupID];
                        var vertexBuffer = group.vertexBuffer;
                        var indiceBuffer = group.indiceBuffer;

                        deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in wmoVertexStride, in wmoVertexOffset);
                        deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR16Uint, 0);

                        var cb = new WMOPerObjectCB
                        {
                            projection_matrix = projectionMatrix,
                            view_matrix = cameraMatrix,
                            model_matrix = Matrix4x4.Identity,
                            vertexShader = (int)ShaderEnums.WMOShaders[(int)batch.shader].VertexShader,
                            pixelShader = (int)ShaderEnums.WMOShaders[(int)batch.shader].PixelShader,
                            _pad0 = Vector2.Zero,
                            lightDirection = LightDirection,
                            alphaRef = 1.0f,
                        };

                        deviceContext.UpdateSubresource(wmoPerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref cb, 0, 0);

                        var srvs = batch.materialFDIDs.Select(id => id != 0 ? BLPCache.GetCurrent(id, defaultTexture) : defaultTexture).ToArray();
                        if (srvs.Length > 0)
                            deviceContext.PSSetShaderResources(0, (uint)srvs.Length, ref srvs[0]);

                        deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchSize, batch.firstFace, 0, 0);
                    }
                }
            }

            // Set up M2 stuff (unchanged per M2 so we do it before we loop)
            deviceContext.RSSetState(wmoRasterizerState);
            deviceContext.IASetInputLayout(m2ShaderProgram.InputLayout);
            deviceContext.VSSetShader(m2ShaderProgram.VertexShader, ref nullClassInstance, 0);
            deviceContext.PSSetShader(m2ShaderProgram.PixelShader, ref nullClassInstance, 0);
            deviceContext.PSSetSamplers(0, 1, ref textureSampler);
            deviceContext.VSSetConstantBuffers(0, 1, ref m2PerObjectConstantBuffer);
            deviceContext.PSSetConstantBuffers(0, 1, ref m2PerObjectConstantBuffer);

            foreach (var (fileDataId, instances) in m2Instances)
            {
                if (!RenderM2 || instances.Count == 0)
                    continue;

                var visibleIndices = new List<int>();
                for (int i = 0; i < instances.Count; i++)
                {
                    var sphere = instances[i].GetBoundingSphere();
                    if (sphere.HasValue && frustum.IsSphereVisible(sphere.Value.Center, sphere.Value.Radius))
                    {
                        visibleWMOs++;
                        visibleIndices.Add(i);
                    }
                }

                if (visibleIndices.Count == 0)
                    continue;

                var m2 = M2Cache.GetOrLoad(device, fileDataId, m2ShaderProgram, instances[0].ParentFileDataId, false);

                var vertexBuffer = m2.vertexBuffer;
                var indiceBuffer = m2.indiceBuffer;

                deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in m2VertexStride, in m2VertexOffset);
                deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR16Uint, 0);

                visibleM2s++;
                for (int batchStart = 0; batchStart < visibleIndices.Count; batchStart += MaxInstancesPerBatch)
                {
                    int batchCount = Math.Min(MaxInstancesPerBatch, visibleIndices.Count - batchStart);

                    unsafe
                    {
                        MappedSubresource mapped = default;
                        SilkMarshal.ThrowHResult(deviceContext.Map(instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                        var dest = new Span<Matrix4x4>(mapped.PData, batchCount);
                        for (int i = 0; i < batchCount; i++)
                            dest[i] = instances[visibleIndices[batchStart + i]].GetModelMatrix();

                        deviceContext.Unmap(instanceMatrixBuffer, 0);
                    }

                    deviceContext.IASetVertexBuffers(1, 1, ref instanceMatrixBuffer, in instanceStride, in instanceOffset);

                    for (int j = 0; j < m2.submeshes.Length; j++)
                    {
                        var batch = m2.submeshes[j];

                        var cb = new M2PerObjectCB
                        {
                            projection_matrix = projectionMatrix,
                            view_matrix = cameraMatrix,
                            model_matrix = Matrix4x4.Identity, // now comes from instance buffer
                            vertexShader = (int)batch.vertexShaderID,
                            pixelShader = (int)batch.pixelShaderID,
                            texMatrix1 = Matrix4x4.Identity,
                            texMatrix2 = Matrix4x4.Identity,
                            hasTexMatrix1 = 0,
                            hasTexMatrix2 = 0,
                            lightDirection = LightDirection,
                            alphaRef = 1.0f,
                            blendMode = batch.blendType,
                            _pad = Vector3.Zero
                        };

                        deviceContext.UpdateSubresource(m2PerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref cb, 0, 0);

                        var srvs = batch.material.Select(id => id != 0 ? BLPCache.GetCurrent(id, defaultTexture) : defaultTexture).ToArray();
                        if (srvs.Length > 0)
                            deviceContext.PSSetShaderResources(0, (uint)srvs.Length, ref srvs[0]);

                        deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchCount, batch.firstFace, 0, 0);
                    }
                }
            }

            foreach (var sceneObject in SceneObjects)
            {
                if (sceneObject is ADTContainer adt)
                {
                    if (!RenderADT)
                        continue;

                    deviceContext.RSSetState(rasterizerState);

                    var vertexBuffer = adt.Terrain.vertexBuffer;
                    var indiceBuffer = adt.Terrain.indiceBuffer;

                    var cb = new ADTPerObjectCB
                    {
                        model_matrix = adt.GetModelMatrix(),
                        projection_matrix = projectionMatrix,
                        rotation_matrix = cameraMatrix,
                        firstPos = adt.Terrain.startPos,
                        _pad0 = 0f
                    };

                    deviceContext.UpdateSubresource(adtPerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref cb, 0, 0);
                    deviceContext.VSSetConstantBuffers(0, 1, ref adtPerObjectConstantBuffer);
                    deviceContext.PSSetConstantBuffers(0, 1, ref adtPerObjectConstantBuffer);

                    deviceContext.IASetInputLayout(adtShaderProgram.InputLayout);
                    deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in adtVertexStride, in adtVertexOffset);
                    deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR32Uint, 0);

                    deviceContext.VSSetShader(adtShaderProgram.VertexShader, ref nullClassInstance, 0);
                    deviceContext.PSSetShader(adtShaderProgram.PixelShader, ref nullClassInstance, 0);

                    deviceContext.VSSetConstantBuffers(1, 1, ref layerDataConstantBuffer);
                    deviceContext.PSSetConstantBuffers(1, 1, ref layerDataConstantBuffer);

                    var layerCB = new LayerData
                    {
                        layerCount = 0,
                        lightDirection = LightDirection,
                        heightScales0 = Vector4.One,
                        heightScales1 = Vector4.One,
                        heightOffsets0 = Vector4.Zero,
                        heightOffsets1 = Vector4.Zero,
                        layerScales0 = Vector4.One,
                        layerScales1 = Vector4.One,
                    };

                    for (uint c = 0; c < 256; c++)
                    {
                        var bounds = adt.Terrain.chunkBounds[c];
                        if (!frustum.IsBoxVisible(bounds.Min, bounds.Max))
                            continue;
                        else
                            visibleChunks++;

                        var batch = adt.Terrain.renderBatches[c];

                        layerCB.layerCount = batch.materialFDIDs.Length;
                        layerCB.heightScales0 = new Vector4(batch.heightScales[0], batch.heightScales[1], batch.heightScales[2], batch.heightScales[3]);
                        layerCB.heightScales1 = new Vector4(batch.heightScales[4], batch.heightScales[5], batch.heightScales[6], batch.heightScales[7]);
                        layerCB.heightOffsets0 = new Vector4(batch.heightOffsets[0], batch.heightOffsets[1], batch.heightOffsets[2], batch.heightOffsets[3]);
                        layerCB.heightOffsets1 = new Vector4(batch.heightOffsets[4], batch.heightOffsets[5], batch.heightOffsets[6], batch.heightOffsets[7]);
                        layerCB.layerScales0 = new Vector4(batch.scales[0], batch.scales[1], batch.scales[2], batch.scales[3]);
                        layerCB.layerScales1 = new Vector4(batch.scales[4], batch.scales[5], batch.scales[6], batch.scales[7]);

                        deviceContext.UpdateSubresource(layerDataConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref layerCB, 0, 0);


                        var materialIDsrvs = batch.materialFDIDs.Select(id => id != 0 ? BLPCache.GetCurrent((uint)id, defaultTexture) : defaultTexture).ToArray();
                        if (materialIDsrvs.Length > 0)
                            deviceContext.PSSetShaderResources(0, 8, ref materialIDsrvs[0]);

                        var heightMaterialIDsrvs = batch.heightMaterialFDIDs.Select(id => id != 0 ? BLPCache.GetCurrent((uint)id, defaultTexture) : defaultTexture).ToArray();
                        if (heightMaterialIDsrvs.Length > 0)
                            deviceContext.PSSetShaderResources(8, 8, ref heightMaterialIDsrvs[0]);

                        deviceContext.PSSetShaderResources(16, 2, ref batch.alphaMaterialID[0]);

                        deviceContext.DrawIndexed(768, c * 768, 0);
                    }
                }
            }

            // Bounding box rendering
            if (ShowBoundingBoxes || ShowBoundingSpheres || SelectedObject != null)
            {
                deviceContext.RSSetState(wireframeRasterizerState);
                deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
                deviceContext.IASetInputLayout(bboxShaderProgram.InputLayout);
                deviceContext.VSSetShader(bboxShaderProgram.VertexShader, ref nullClassInstance, 0);
                deviceContext.PSSetShader(bboxShaderProgram.PixelShader, ref nullClassInstance, 0);
                deviceContext.OMSetDepthStencilState(bboxDepthStencilState, 0);

                lock (SceneObjectLock)
                {
                    foreach (var sceneObject in SceneObjects)
                    {
                        if (sceneObject is ADTContainer) continue;
                        if (!ShowBoundingBoxes && !ShowBoundingSpheres && !sceneObject.IsSelected) continue;

                        var color = sceneObject.IsSelected ? new Vector4(0, 1, 0, 1) : new Vector4(1, 1, 0, 1);

                        if (ShowBoundingBoxes || sceneObject.IsSelected)
                        {
                            var box = sceneObject.GetBoundingBox();
                            if (box.HasValue && float.IsFinite(box.Value.Min.X) && float.IsFinite(box.Value.Max.X))
                            {
                                BoundingBox localBox;
                                Matrix4x4 modelMatrix;

                                if (sceneObject is WMOContainer wmo)
                                {
                                    localBox = wmo.GetLocalBoundingBox();
                                    modelMatrix = wmo.GetModelMatrix();
                                }
                                else if (sceneObject is M2Container m2)
                                {
                                    localBox = m2.GetLocalBoundingBox();
                                    modelMatrix = m2.GetModelMatrix();
                                }
                                else continue;

                                DrawBoundingBox(localBox, modelMatrix, color, projectionMatrix, cameraMatrix);
                            }
                        }

                        if (ShowBoundingSpheres || sceneObject.IsSelected)
                        {
                            var sphere = sceneObject.GetBoundingSphere();
                            if (sphere.HasValue)
                                DrawBoundingSphere(sphere.Value, new Vector4(0, 0.5f, 1, 1), projectionMatrix, cameraMatrix);
                        }
                    }
                }

                deviceContext.RSSetState(rasterizerState);
                deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
                ComPtr<ID3D11DepthStencilState> nullDSS = default;
                deviceContext.OMSetDepthStencilState(nullDSS, 0);
            }

            swapchain.Present(1, 0);

            gizmoWasUsing = false;
            gizmoWasOver = false;
        }

        private unsafe void DrawBoundingSphere(BoundingSphere sphere, Vector4 color, Matrix4x4 projection, Matrix4x4 view)
        {
            const int segments = 32;
            var verts = new List<Vector3>();

            for (int pass = 0; pass < 3; pass++)
            {
                for (int s = 0; s < segments; s++)
                {
                    float a0 = (MathF.PI * 2f / segments) * s;
                    float a1 = (MathF.PI * 2f / segments) * (s + 1);

                    Vector3 p0, p1;
                    switch (pass)
                    {
                        case 0: // XY
                            p0 = new Vector3(MathF.Cos(a0), MathF.Sin(a0), 0);
                            p1 = new Vector3(MathF.Cos(a1), MathF.Sin(a1), 0);
                            break;
                        case 1: // XZ
                            p0 = new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0));
                            p1 = new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1));
                            break;
                        default: // YZ
                            p0 = new Vector3(0, MathF.Cos(a0), MathF.Sin(a0));
                            p1 = new Vector3(0, MathF.Cos(a1), MathF.Sin(a1));
                            break;
                    }

                    verts.Add(sphere.Center + p0 * sphere.Radius);
                    verts.Add(sphere.Center + p1 * sphere.Radius);
                }
            }

            int vertCount = verts.Count; // 3 * segments * 2 = 192 for segments=32
            var sphereVerts = verts.ToArray();

            var vbDesc = new BufferDesc
            {
                ByteWidth = (uint)(vertCount * sizeof(Vector3)),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write
            };

            ComPtr<ID3D11Buffer> sphereVB = default;
            SilkMarshal.ThrowHResult(device.CreateBuffer(in vbDesc, null, ref sphereVB));

            MappedSubresource mappedVB = default;
            SilkMarshal.ThrowHResult(deviceContext.Map(sphereVB, 0, Map.WriteDiscard, 0, ref mappedVB));
            var dest = new Span<Vector3>(mappedVB.PData, vertCount);
            sphereVerts.CopyTo(dest);
            deviceContext.Unmap(sphereVB, 0);

            var cb = new BBoxCB
            {
                projection_matrix = projection,
                view_matrix = view,
                model_matrix = Matrix4x4.Identity,
                color = color
            };

            MappedSubresource mappedCB = default;
            SilkMarshal.ThrowHResult(deviceContext.Map(bboxConstantBuffer, 0, Map.WriteDiscard, 0, ref mappedCB));
            *(BBoxCB*)mappedCB.PData = cb;
            deviceContext.Unmap(bboxConstantBuffer, 0);

            uint stride = (uint)sizeof(Vector3);
            uint offset = 0;
            deviceContext.IASetVertexBuffers(0, 1, ref sphereVB, in stride, in offset);
            deviceContext.VSSetConstantBuffers(0, 1, ref bboxConstantBuffer);
            deviceContext.PSSetConstantBuffers(0, 1, ref bboxConstantBuffer);

            ComPtr<ID3D11Buffer> nullBuffer = default;
            uint nullStride = 0, nullOffset = 0;
            deviceContext.IASetVertexBuffers(1, 1, ref nullBuffer, in nullStride, in nullOffset);

            deviceContext.Draw((uint)vertCount, 0);
            sphereVB.Dispose();
        }

        private unsafe void DrawBoundingBox(BoundingBox localBox, Matrix4x4 modelMatrix, Vector4 color, Matrix4x4 projection, Matrix4x4 view)
        {
            var min = localBox.Min;
            var max = localBox.Max;

            var verts = new Vector3[24];
            int i = 0;

            verts[i++] = new(min.X, min.Y, min.Z); verts[i++] = new(max.X, min.Y, min.Z);
            verts[i++] = new(max.X, min.Y, min.Z); verts[i++] = new(max.X, min.Y, max.Z);
            verts[i++] = new(max.X, min.Y, max.Z); verts[i++] = new(min.X, min.Y, max.Z);
            verts[i++] = new(min.X, min.Y, max.Z); verts[i++] = new(min.X, min.Y, min.Z);
            verts[i++] = new(min.X, max.Y, min.Z); verts[i++] = new(max.X, max.Y, min.Z);
            verts[i++] = new(max.X, max.Y, min.Z); verts[i++] = new(max.X, max.Y, max.Z);
            verts[i++] = new(max.X, max.Y, max.Z); verts[i++] = new(min.X, max.Y, max.Z);
            verts[i++] = new(min.X, max.Y, max.Z); verts[i++] = new(min.X, max.Y, min.Z);
            verts[i++] = new(min.X, min.Y, min.Z); verts[i++] = new(min.X, max.Y, min.Z);
            verts[i++] = new(max.X, min.Y, min.Z); verts[i++] = new(max.X, max.Y, min.Z);
            verts[i++] = new(max.X, min.Y, max.Z); verts[i++] = new(max.X, max.Y, max.Z);
            verts[i++] = new(min.X, min.Y, max.Z); verts[i++] = new(min.X, max.Y, max.Z);

            MappedSubresource mappedVB = default;
            SilkMarshal.ThrowHResult(deviceContext.Map(bboxVertexBuffer, 0, Map.WriteDiscard, 0, ref mappedVB));
            var dest = new Span<Vector3>(mappedVB.PData, 24);
            verts.CopyTo(dest);
            deviceContext.Unmap(bboxVertexBuffer, 0);

            var cb = new BBoxCB
            {
                projection_matrix = projection,
                view_matrix = view,
                model_matrix = modelMatrix,
                color = color
            };

            MappedSubresource mappedCB = default;
            SilkMarshal.ThrowHResult(deviceContext.Map(bboxConstantBuffer, 0, Map.WriteDiscard, 0, ref mappedCB));
            *(BBoxCB*)mappedCB.PData = cb;
            deviceContext.Unmap(bboxConstantBuffer, 0);

            uint stride = (uint)sizeof(Vector3);
            uint offset = 0;
            deviceContext.IASetVertexBuffers(0, 1, ref bboxVertexBuffer, in stride, in offset);
            deviceContext.VSSetConstantBuffers(0, 1, ref bboxConstantBuffer);
            deviceContext.PSSetConstantBuffers(0, 1, ref bboxConstantBuffer);

            ComPtr<ID3D11Buffer> nullBuffer = default;
            uint nullStride = 0, nullOffset = 0;
            deviceContext.IASetVertexBuffers(1, 1, ref nullBuffer, in nullStride, in nullOffset);

            deviceContext.Draw(24, 0);
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

        public static Vector3 GetTileCenterPosition(byte tileX, byte tileY)
        {
            const float tileSize = 533.33333f;
            const int mapCenter = 32;
            var posX = (mapCenter - tileX) * tileSize - (tileSize / 2);
            var posY = (mapCenter - tileY) * tileSize - (tileSize / 2);
            return new Vector3(posY, posX, 0);
        }

        //private unsafe uint MakeDefaultTexture()
        //{
        //    var defaultTexture = _gl.GenTexture();
        //    _gl.BindTexture(TextureTarget.Texture2D, defaultTexture);
        //    byte[] fill = [0, 0, 0, 0];
        //    fixed (byte* fillPtr = fill)
        //    {
        //        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, fillPtr);
        //    }

        //    _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        //    _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        //    _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        //    _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        //    return defaultTexture;
        //}

        //private static void SwitchBlendMode(int blendType, GL gl, int alphaRefLoc)
        //{
        //    switch (blendType)
        //    {
        //        case 0:
        //            gl.Disable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            break;
        //        case 1:
        //            gl.Disable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, 0.90393700787f);
        //            break;
        //        case 2:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        //            break;
        //        case 3:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);
        //            break;
        //        case 4:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.Zero, BlendingFactor.DstAlpha, BlendingFactor.Zero);
        //            break;
        //        case 5:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.SrcColor, BlendingFactor.DstAlpha, BlendingFactor.SrcAlpha);
        //            break;
        //        case 6:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.One, BlendingFactor.DstAlpha, BlendingFactor.One);
        //            break;
        //        case 7:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One);
        //            break;
        //        case 8:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero);
        //            break;
        //        case 9:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.Zero, BlendingFactor.SrcAlpha, BlendingFactor.Zero);
        //            break;
        //        case 10:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);
        //            break;
        //        case 11:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.ConstantAlpha, BlendingFactor.OneMinusConstantAlpha, BlendingFactor.ConstantAlpha, BlendingFactor.OneMinusConstantAlpha);
        //            break;
        //        case 12:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.OneMinusDstColor, BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero);
        //            break;
        //        case 13:
        //            gl.Enable(EnableCap.Blend);
        //            gl.Uniform1(alphaRefLoc, -1.0f);
        //            gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        //            break;
        //        default:
        //            throw new Exception("Unsupport blend mode: " + blendType);
        //    }
        //}

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                //M2Cache.StopWorker();
                WMOCache.StopWorker();
                BLPCache.StopWorker();

                // TODO: Release all cached resources

                textureSampler.Dispose();
                clampSampler.Dispose();
                renderTargetView.Dispose();
                depthStencilView.Dispose();
                depthTexture.Dispose();
                adtPerObjectConstantBuffer.Dispose();
                layerDataConstantBuffer.Dispose();
                m2PerObjectConstantBuffer.Dispose();
                wmoPerObjectConstantBuffer.Dispose();
                instanceMatrixBuffer.Dispose();
                defaultTexture.Dispose();
                bboxDepthStencilState.Dispose();
            }
        }
    }
}
