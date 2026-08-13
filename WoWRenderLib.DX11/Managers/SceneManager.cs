using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WoWFormatLib.Structs.WDT;
using WoWRenderLib.Cache;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Managers
{
    public class SceneManager(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> deviceContext, ShaderManager shaderManager) : IDisposable
    {
        private readonly ShaderManager _shaderManager = shaderManager ?? throw new ArgumentNullException(nameof(shaderManager));
        public List<Container3D> SceneObjects { get; } = [];
        public Lock SceneObjectLock { get; } = new();

        private Queue<MapTile> tilesToLoad = new();
        private readonly Queue<MapTile> tilesToUnload = new();
        private readonly HashSet<MapTile> tilesInFlight = [];

        private int totalTilesToLoad = 0;
        private readonly Dictionary<uint, uint> uuidUsers = [];
        private readonly HashSet<MapTile> loadedTiles = [];

        private WDT? currentWDT;
        public uint CurrentWDTFileDataID { get; private set; } = 775971;
        public Container3D? SelectedObject { get; set; } = null;
        public bool ShowBoundingBoxes { get; set; } = false;
        public bool ShowBoundingSpheres { get; set; } = false;

        public bool RenderADT { get; set; } = true;
        public bool RenderWMO { get; set; } = true;
        public bool RenderM2 { get; set; } = true;
        public int TileLoadingDistance { get; set; } = 4;
        public float TerrainRenderDistance { get; set; } = 20_000f;
        public float ModelRenderDistance { get; set; } = 20_000f;

        // World-space light from north-west at a 45° elevation. WoW's world
        // axes map north/west to the negative X/Y directions in this renderer.
        public Vector3 LightDirection { get; set; } = new(-0.5f, -0.5f, 0.70710678f);
        public Vector3 AmbientColor { get; set; } = new(104f / 255f, 130f / 255f, 154f / 255f);
        public Vector3 DiffuseColor { get; set; } = new(1f, 136f / 255f, 0f);

        private const int MaxInstancesPerBatch = 1024;

        private CompiledShader adtShaderProgram;
        private CompiledShader wmoShaderProgram;
        private CompiledShader m2ShaderProgram;
        private CompiledShader debugShaderProgram;
        private CompiledShader bboxShaderProgram;

        private readonly Queue<WMOContainer> pendingWMODoodads = [];
        public readonly Dictionary<(uint FileDataID, int EnabledGroupHash), List<WMOContainer>> wmoInstances = [];
        public readonly Dictionary<uint, List<M2Container>> m2Instances = [];

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
        private readonly ComPtr<ID3D11BlendState>[] _blendStates = new ComPtr<ID3D11BlendState>[14];

        private readonly ComPtr<ID3D11ShaderResourceView>[] _srvScratch = new ComPtr<ID3D11ShaderResourceView>[16];
        private readonly List<int> _visibleIndices = new(64);

        private uint _renderWidth = 1920;
        private uint _renderHeight = 1080;

        public int visibleChunks { get; private set; } = 0;
        public int visibleWMOs { get; private set; } = 0;
        public int visibleM2s { get; private set; } = 0;
        public int candidateChunks { get; private set; }
        public int candidateWMOs { get; private set; }
        public int candidateM2s { get; private set; }
        public double CullingTimeMs { get; private set; }
        public int UploadedResourcesLastFrame { get; private set; }

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
                    // Wisp follows the client convention: CCW triangles are
                    // front-facing and back faces are culled. The previous
                    // Front/clockwise state inverted this and hid valid M2/WMO
                    // surfaces.
                    CullMode = CullMode.Back,
                    FrontCounterClockwise = true,
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
                CreateBlendStates();
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

        private unsafe void CreateBlendStates()
        {
            static RenderTargetBlendDesc MakeRTBlend(bool enable, Blend src, Blend dst, Blend srcA, Blend dstA) => new()
            {
                BlendEnable = enable ? (Silk.NET.Core.Bool32)1 : (Silk.NET.Core.Bool32)0,
                SrcBlend = src,
                DestBlend = dst,
                BlendOp = BlendOp.Add,
                SrcBlendAlpha = srcA,
                DestBlendAlpha = dstA,
                BlendOpAlpha = BlendOp.Add,
                RenderTargetWriteMask = (byte)ColorWriteEnable.All
            };

            (bool enabled, Blend src, Blend dst, Blend srcA, Blend dstA)[] configs =
            [
                (false, Blend.One,         Blend.Zero,          Blend.One,         Blend.Zero),
                (false, Blend.One,         Blend.Zero,          Blend.One,         Blend.Zero),
                (true,  Blend.SrcAlpha,    Blend.InvSrcAlpha,   Blend.SrcAlpha,    Blend.InvSrcAlpha),
                (true,  Blend.SrcAlpha,    Blend.One,           Blend.Zero,        Blend.One),
                (true,  Blend.DestColor,   Blend.Zero,          Blend.DestAlpha,   Blend.Zero),
                (true,  Blend.DestColor,   Blend.SrcColor,      Blend.DestAlpha,   Blend.SrcAlpha),
                (true,  Blend.DestColor,   Blend.One,           Blend.DestAlpha,   Blend.One),
                (true,  Blend.InvSrcAlpha, Blend.One,           Blend.InvSrcAlpha, Blend.One),
                (true,  Blend.InvSrcAlpha, Blend.Zero,          Blend.InvSrcAlpha, Blend.Zero),
                (true,  Blend.SrcAlpha,    Blend.Zero,          Blend.SrcAlpha,    Blend.Zero),
                (true,  Blend.One,         Blend.One,           Blend.Zero,        Blend.One),
                (true,  Blend.BlendFactor, Blend.InvBlendFactor,Blend.BlendFactor, Blend.InvBlendFactor),
                (true,  Blend.InvDestColor,Blend.One,           Blend.One,         Blend.Zero),
                (true,  Blend.One,         Blend.InvSrcAlpha,   Blend.One,         Blend.InvSrcAlpha),
            ];

            for (int i = 0; i < configs.Length; i++)
            {
                var (enabled, src, dst, srcA, dstA) = configs[i];
                var blendDesc = new BlendDesc { AlphaToCoverageEnable = 0, IndependentBlendEnable = 0 };
                blendDesc.RenderTarget[0] = MakeRTBlend(enabled, src, dst, srcA, dstA);
                SilkMarshal.ThrowHResult(device.CreateBlendState(in blendDesc, ref _blendStates[i]));
            }
        }

        private unsafe float ApplyBlendMode(int blendType)
        {
            if ((uint)blendType >= (uint)_blendStates.Length)
                blendType = 0;

            float blendFactor = 1f;
            deviceContext.OMSetBlendState(_blendStates[blendType], ref blendFactor, 0xFFFFFFFF);

            return blendType == 1 ? 0.90393700787f : -1.0f;
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

        private unsafe void CreateSizeDependentResources(uint width, uint height, ComPtr<ID3D11RenderTargetView> rtv)
        {
            if (width == 0 || height == 0)
                return;

            renderTargetView = rtv;

            var depthDesc = new Texture2DDesc
            {
                Width = width,
                Height = height,
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
                Width = width,
                Height = height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };
            deviceContext.RSSetViewports(1, in viewport);

            _renderWidth = width;
            _renderHeight = height;
        }

        public unsafe void Resize(uint width, uint height, ComPtr<ID3D11RenderTargetView> rtv)
        {
            if (width == 0 || height == 0)
                return;

            deviceContext.OMSetRenderTargets(0, (ID3D11RenderTargetView**)null, (ID3D11DepthStencilView*)null);
            deviceContext.ClearState();

            renderTargetView = default; // don't dispose, we dont own it!
            if (depthStencilView.Handle != null) { depthStencilView.Dispose(); depthStencilView = default; }
            if (depthTexture.Handle != null) { depthTexture.Dispose(); depthTexture = default; }

            CreateSizeDependentResources(width, height, rtv);
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

            var viewDistance = Math.Clamp(TileLoadingDistance, 0, 32);
            for (int xOffset = -viewDistance; xOffset <= viewDistance; xOffset++)
            {
                for (int yOffset = -viewDistance; yOffset <= viewDistance; yOffset++)
                {
                    int tileX = x + xOffset;
                    int tileY = y + yOffset;

                    if (tileX < 0 || tileX > 63 || tileY < 0 || tileY > 63)
                        continue;

                    if (!currentWDT.Value.tiles.Contains(((byte)tileX, (byte)tileY)))
                        continue;

                    var mapTile = new MapTile
                    {
                        tileX = (byte)tileX,
                        tileY = (byte)tileY,
                        wdtFileDataID = CurrentWDTFileDataID
                    };

                    usedTiles.Add(mapTile);

                    if (!loadedTiles.Contains(mapTile) && !tilesToLoad.Contains(mapTile) && !tilesInFlight.Contains(mapTile))
                    {
                        tilesToLoad.Enqueue(mapTile);
                        totalTilesToLoad++;
                    }
                }
            }

            foreach (var tile in loadedTiles)
            {
                bool inRange = false;

                // same logic as above
                    for (int xOffset = -viewDistance; xOffset <= viewDistance && !inRange; xOffset++)
                    for (int yOffset = -viewDistance; yOffset <= viewDistance && !inRange; yOffset++)
                        if (x + xOffset >= 0 && x + xOffset <= 63 &&
                            y + yOffset >= 0 && y + yOffset <= 63 &&
                            tile.tileX == x + xOffset && tile.tileY == y + yOffset)
                            inRange = true;

                if (!inRange && !tilesToUnload.Contains(tile))
                    tilesToUnload.Enqueue(tile);
            }
        }

        public void ProcessUnloadQueue()
        {
            var unloadTimer = Stopwatch.StartNew();

            bool instanceListDirty = false;
            bool cachesDirty = false;

            while (tilesToUnload.Count > 0 && unloadTimer.ElapsedMilliseconds < 10)
            {
                var tile = tilesToUnload.Dequeue();
                // TODO: this is rough... tilesToLoad should be readonly and not entirely redefined every unload...
                var queuedTiles = tilesToLoad.ToList();
                if (queuedTiles.Contains(tile))
                {
                    tilesToLoad = new Queue<MapTile>(queuedTiles.Where(t => t != tile));
                    continue;
                }

                tilesInFlight.Remove(tile);
                loadedTiles.Remove(tile);

                Console.WriteLine("Unloading tile " + tile.tileX + ", " + tile.tileY);
                lock (SceneObjectLock)
                {
                    var adtToRemove = SceneObjects.OfType<ADTContainer>().FirstOrDefault(a => a.mapTile.wdtFileDataID == tile.wdtFileDataID && a.mapTile.tileX == tile.tileX && a.mapTile.tileY == tile.tileY);

                    if (adtToRemove == null)
                        continue;

                    // remove callback so it cant finish loading, nyehehe
                    adtToRemove.LoadCallback -= OnADTContainerLoaded;

                    SceneObjects.Remove(adtToRemove);

                    // if its not actually loaded yet, dont bother with the rest
                    if (!adtToRemove.IsLoaded)
                    {
                        adtToRemove.Unload();
                        continue;
                    }

                    var rootId = adtToRemove.Terrain.rootADTFileDataID;

                    foreach (var wmo in SceneObjects.OfType<WMOContainer>().Where(w => w.ParentFileDataId == rootId).ToList())
                    {
                        if (uuidUsers.TryGetValue(wmo.UniqueID, out var count) && count > 1)
                        {
                            uuidUsers[wmo.UniqueID] = count - 1;
                        }
                        else
                        {
                            foreach (var doodad in wmo.ActiveDoodads)
                                SceneObjects.Remove(doodad);

                            wmo.ActiveDoodads.Clear();
                            SceneObjects.Remove(wmo);
                            uuidUsers.Remove(wmo.UniqueID);
                        }
                    }

                    foreach (var m2 in SceneObjects.OfType<M2Container>().Where(m => m.ParentFileDataId == rootId).ToList())
                        SceneObjects.Remove(m2);

                    adtToRemove.Unload();
                }

                instanceListDirty = true;
                cachesDirty = true;
            }

            if (instanceListDirty)
                UpdateInstanceList();

            if (cachesDirty)
            {
                WMOCache.CheckUsers();
                M2Cache.CheckUsers();
                BLPCache.CheckUsers();
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
            var wmo = wmoContainer.GetWMO();
            var enabledSets = wmoContainer.EnabledDoodadSets;

            wmoContainer.ActiveDoodads.Clear();

            foreach (var doodad in wmo.doodads)
            {
                if (!enabledSets[doodad.doodadSet])
                    continue;

                var m2Container = new M2Container(device, doodad.filedataid, wmoContainer.ParentFileDataId)
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
            var queueTimer = new Stopwatch();
            queueTimer.Start();

            UploadedResourcesLastFrame = ADTCache.Upload(queueTimer, device);
            UploadedResourcesLastFrame += WMOCache.Upload(queueTimer);
            UploadedResourcesLastFrame += M2Cache.Upload(queueTimer);
            UploadedResourcesLastFrame += BLPCache.Upload(queueTimer);

            if (queueTimer.ElapsedMilliseconds > 10)
                return true;

            ProcessUnloadQueue();

            var remaining = pendingWMODoodads.Count;
            for (var i = 0; i < remaining; i++)
            {
                var wmoContainer = pendingWMODoodads.Dequeue();

                if (!wmoContainer.IsLoaded)
                {
                    pendingWMODoodads.Enqueue(wmoContainer);
                    continue;
                }

                SpawnWMODoodads(wmoContainer);
                wmoContainer.DoodadsSpawned = true;
            }

            if (tilesToLoad.Count == 0)
                return ADTCache.GetLoadQueueCount() > 0 || WMOCache.GetLoadQueueCount() > 0 || M2Cache.GetLoadQueueCount() > 0 || BLPCache.GetQueueCount() > 0 || pendingWMODoodads.Count > 0;

            while (tilesToLoad.Count > 0 && queueTimer.ElapsedMilliseconds < 10)
            {
                var mapTile = tilesToLoad.Dequeue();
                tilesInFlight.Add(mapTile);

                try
                {
                    var adtContainer = new ADTContainer(device, mapTile);
                    adtContainer.LoadCallback += OnADTContainerLoaded;

                    ADTCache.GetOrLoad(mapTile, mapTile.wdtFileDataID, adtContainer.OnLoaded);

                    lock (SceneObjectLock)
                        SceneObjects.Add(adtContainer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error queuing ADT: " + ex.ToString());
                }
            }

            Console.WriteLine("Spent " + queueTimer.ElapsedMilliseconds + "ms processing queue, " + tilesToLoad.Count + " tiles left to queue for load.");

            return true;
        }

        public int GetPendingOperationCount() =>
            tilesToLoad.Count +
            tilesInFlight.Count +
            pendingWMODoodads.Count +
            ADTCache.GetLoadQueueCount() +
            WMOCache.GetLoadQueueCount() +
            M2Cache.GetLoadQueueCount() +
            BLPCache.GetQueueCount();

        private void OnADTContainerLoaded(ADTContainer adtContainer, Terrain terrain)
        {
            // unregister the callback, adts only load once, probably
            adtContainer.LoadCallback -= OnADTContainerLoaded;

            foreach (var worldModel in terrain.worldModelBatches)
            {
                if (uuidUsers.ContainsKey(worldModel.uniqueID))
                    continue;

                var worldModelContainer = new WMOContainer(device, worldModel.fileDataID, terrain.rootADTFileDataID)
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

                pendingWMODoodads.Enqueue(worldModelContainer);
            }

            foreach (var doodad in terrain.doodads)
            {
                var doodadContainer = new M2Container(device, doodad.fileDataID, terrain.rootADTFileDataID)
                {
                    Position = doodad.position,
                    Rotation = doodad.rotation,
                    Scale = doodad.scale
                };

                lock (SceneObjectLock)
                    SceneObjects.Add(doodadContainer);
            }

            UpdateInstanceList();
            tilesInFlight.Remove(adtContainer.mapTile);
            loadedTiles.Add(adtContainer.mapTile);
        }

        public void MoveSelectedObject(Vector3 delta)
        {
            SelectedObject?.Position += delta;
            SelectedObject?.ModelMatrix = null;
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

        public (uint drawCalls, uint vertices) RenderScene(Camera camera, out bool gizmoWasUsing, out bool gizmoWasOver)
        {
            uint drawCalls = 0;
            uint verticeCount = 0;

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

            var cullingStarted = Stopwatch.GetTimestamp();
            camera.UpdateFrustum();

            var frustum = camera.GetFrustum();

            visibleM2s = 0;
            visibleWMOs = 0;
            visibleChunks = 0;
            candidateM2s = 0;
            candidateWMOs = 0;
            candidateChunks = 0;
            CullingTimeMs = Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;

            var backgroundColour = new[] { 0f, 0f, 0f, 1.0f };

            ComPtr<ID3D11ShaderResourceView> nullSRV = default;
            deviceContext.PSSetShaderResources(0, 1, ref nullSRV);

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

            var wmoConstantBuffer = new WMOPerObjectCB
            {
                projection_matrix = projectionMatrix,
                view_matrix = cameraMatrix,
                model_matrix = Matrix4x4.Identity,
                vertexShader = 0,
                pixelShader = 0,
                _pad0 = Vector2.Zero,
                lightDirection = LightDirection,
                ambientColor = AmbientColor,
                diffuseColor = DiffuseColor,
                alphaRef = 1.0f,
            };

            foreach (var (key, instances) in wmoInstances)
            {
                if (!RenderWMO || instances.Count == 0)
                    continue;

                var firstInstance = instances[0];
                if (!firstInstance.IsLoaded)
                    continue;

                candidateWMOs += instances.Count;
                cullingStarted = Stopwatch.GetTimestamp();
                _visibleIndices.Clear();
                for (int i = 0; i < instances.Count; i++)
                {
                    var sphere = instances[i].GetBoundingSphere();
                    if (sphere.HasValue &&
                        IsWithinRenderDistance(camera.Position, sphere.Value.Center, sphere.Value.Radius, ModelRenderDistance) &&
                        frustum.IsSphereVisible(sphere.Value.Center, sphere.Value.Radius))
                    {
                        visibleWMOs++;
                        _visibleIndices.Add(i);
                    }
                }
                CullingTimeMs += Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;

                if (_visibleIndices.Count == 0)
                    continue;

                var wmo = firstInstance.GetWMO();
                var enabledGroups = firstInstance.EnabledGroups;

                for (int batchStart = 0; batchStart < _visibleIndices.Count; batchStart += MaxInstancesPerBatch)
                {
                    int batchSize = Math.Min(MaxInstancesPerBatch, _visibleIndices.Count - batchStart);

                    // the normal approach to do updatesubresource doesn't work for dynamic buffers, so we have to do the below block instead
                    unsafe
                    {
                        MappedSubresource mapped = default;
                        SilkMarshal.ThrowHResult(deviceContext.Map(instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                        var dest = new Span<Matrix4x4>(mapped.PData, batchSize);
                        for (int i = 0; i < batchSize; i++)
                            dest[i] = instances[_visibleIndices[batchStart + i]].GetModelMatrix();

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

                        wmoConstantBuffer.vertexShader = (int)ShaderEnums.WMOShaders[(int)batch.shader].VertexShader;
                        wmoConstantBuffer.pixelShader = (int)ShaderEnums.WMOShaders[(int)batch.shader].PixelShader;
                        // Match the OpenGL WMO path: -1 means opaque/no alpha
                        // test, while blend mode 1 supplies the alpha-test ref.
                        wmoConstantBuffer.alphaRef = ApplyBlendMode((int)batch.blendType);

                        deviceContext.UpdateSubresource(wmoPerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref wmoConstantBuffer, 0, 0);

                        for (int s = 0; s < batch.materialFDIDs.Length; s++)
                            _srvScratch[s] = batch.materialFDIDs[s] != 0 ? BLPCache.GetCurrent(batch.materialFDIDs[s], defaultTexture) : defaultTexture;
                        if (batch.materialFDIDs.Length > 0)
                            deviceContext.PSSetShaderResources(0, (uint)batch.materialFDIDs.Length, ref _srvScratch[0]);

                        deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchSize, batch.firstFace, 0, 0);

                        drawCalls++;
                        verticeCount += batch.numFaces;
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

            var m2ConstantBuffer = new M2PerObjectCB
            {
                projection_matrix = projectionMatrix,
                view_matrix = cameraMatrix,
                model_matrix = Matrix4x4.Identity, // now comes from instance buffer
                vertexShader = 0,
                pixelShader = 0,
                texMatrix1 = Matrix4x4.Identity,
                texMatrix2 = Matrix4x4.Identity,
                hasTexMatrix1 = 0,
                hasTexMatrix2 = 0,
                lightDirection = LightDirection,
                ambientColor = AmbientColor,
                diffuseColor = DiffuseColor,
                alphaRef = 1.0f,
                blendMode = 0,
                _pad = Vector3.Zero
            };

            foreach (var (fileDataId, instances) in m2Instances)
            {
                if (!RenderM2 || instances.Count == 0)
                    continue;

                candidateM2s += instances.Count;
                cullingStarted = Stopwatch.GetTimestamp();
                _visibleIndices.Clear();
                for (int i = 0; i < instances.Count; i++)
                {
                    var sphere = instances[i].GetBoundingSphere();
                    if (sphere.HasValue &&
                        IsWithinRenderDistance(camera.Position, sphere.Value.Center, sphere.Value.Radius, ModelRenderDistance) &&
                        frustum.IsSphereVisible(sphere.Value.Center, sphere.Value.Radius))
                    {
                        visibleM2s++;
                        _visibleIndices.Add(i);
                    }
                }
                CullingTimeMs += Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;

                if (_visibleIndices.Count == 0)
                    continue;

                var m2 = instances[0].GetM2();

                var vertexBuffer = m2.vertexBuffer;
                var indiceBuffer = m2.indiceBuffer;

                deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in m2VertexStride, in m2VertexOffset);
                deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR16Uint, 0);

                for (int batchStart = 0; batchStart < _visibleIndices.Count; batchStart += MaxInstancesPerBatch)
                {
                    int batchCount = Math.Min(MaxInstancesPerBatch, _visibleIndices.Count - batchStart);

                    unsafe
                    {
                        MappedSubresource mapped = default;
                        SilkMarshal.ThrowHResult(deviceContext.Map(instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                        var dest = new Span<Matrix4x4>(mapped.PData, batchCount);
                        for (int i = 0; i < batchCount; i++)
                            dest[i] = instances[_visibleIndices[batchStart + i]].GetModelMatrix();

                        deviceContext.Unmap(instanceMatrixBuffer, 0);
                    }

                    deviceContext.IASetVertexBuffers(1, 1, ref instanceMatrixBuffer, in instanceStride, in instanceOffset);

                    for (int j = 0; j < m2.submeshes.Length; j++)
                    {
                        var batch = m2.submeshes[j];

                        m2ConstantBuffer.blendMode = batch.blendType;
                        m2ConstantBuffer.alphaRef = ApplyBlendMode((int)batch.blendType);
                        m2ConstantBuffer.vertexShader = (int)batch.vertexShaderID;
                        m2ConstantBuffer.pixelShader = (int)batch.pixelShaderID;

                        deviceContext.UpdateSubresource(m2PerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref m2ConstantBuffer, 0, 0);

                        for (int s = 0; s < batch.material.Length; s++)
                            _srvScratch[s] = batch.material[s] != 0 ? BLPCache.GetCurrent(batch.material[s], defaultTexture) : defaultTexture;
                        if (batch.material.Length > 0)
                            deviceContext.PSSetShaderResources(0, (uint)batch.material.Length, ref _srvScratch[0]);

                        deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchCount, batch.firstFace, 0, 0);
                        drawCalls++;
                        verticeCount += batch.numFaces;
                    }
                }
            }

            ApplyBlendMode(0);

            deviceContext.RSSetState(rasterizerState);
            deviceContext.IASetInputLayout(adtShaderProgram.InputLayout);
            deviceContext.VSSetShader(adtShaderProgram.VertexShader, ref nullClassInstance, 0);
            deviceContext.PSSetShader(adtShaderProgram.PixelShader, ref nullClassInstance, 0);
            deviceContext.VSSetConstantBuffers(0, 1, ref adtPerObjectConstantBuffer);
            deviceContext.PSSetConstantBuffers(0, 1, ref adtPerObjectConstantBuffer);
            deviceContext.VSSetConstantBuffers(1, 1, ref layerDataConstantBuffer);
            deviceContext.PSSetConstantBuffers(1, 1, ref layerDataConstantBuffer);

            var layerCB = new LayerData
            {
                layerCount = 0,
                lightDirection = LightDirection,
                ambientColor = AmbientColor,
                diffuseColor = DiffuseColor,
                heightScales0 = Vector4.One,
                heightScales1 = Vector4.One,
                heightOffsets0 = Vector4.Zero,
                heightOffsets1 = Vector4.Zero,
                layerScales0 = Vector4.One,
                layerScales1 = Vector4.One,
            };

            foreach (var sceneObject in SceneObjects)
            {
                if (sceneObject is ADTContainer adt)
                {
                    if (!RenderADT || !adt.IsLoaded)
                        continue;

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
                    deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in adtVertexStride, in adtVertexOffset);
                    deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR32Uint, 0);

                    candidateChunks += 256;
                    cullingStarted = Stopwatch.GetTimestamp();
                    _visibleIndices.Clear();
                    for (var c = 0; c < 256; c++)
                    {
                        var bounds = adt.Terrain.chunkBounds[c];
                        var boundsCenter = (bounds.Min + bounds.Max) * 0.5f;
                        var boundsRadius = Vector3.Distance(boundsCenter, bounds.Max);
                        if (IsWithinRenderDistance(camera.Position, boundsCenter, boundsRadius, TerrainRenderDistance) &&
                            frustum.IsBoxVisible(bounds.Min, bounds.Max))
                        {
                            visibleChunks++;
                            _visibleIndices.Add(c);
                        }
                    }
                    CullingTimeMs += Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;

                    foreach (var c in _visibleIndices)
                    {
                        var batch = adt.Terrain.renderBatches[c];

                        layerCB.layerCount = batch.materialFDIDs.Length;
                        layerCB.heightScales0 = new Vector4(batch.heightScales[0], batch.heightScales[1], batch.heightScales[2], batch.heightScales[3]);
                        layerCB.heightScales1 = new Vector4(batch.heightScales[4], batch.heightScales[5], batch.heightScales[6], batch.heightScales[7]);
                        layerCB.heightOffsets0 = new Vector4(batch.heightOffsets[0], batch.heightOffsets[1], batch.heightOffsets[2], batch.heightOffsets[3]);
                        layerCB.heightOffsets1 = new Vector4(batch.heightOffsets[4], batch.heightOffsets[5], batch.heightOffsets[6], batch.heightOffsets[7]);
                        layerCB.layerScales0 = new Vector4(batch.scales[0], batch.scales[1], batch.scales[2], batch.scales[3]);
                        layerCB.layerScales1 = new Vector4(batch.scales[4], batch.scales[5], batch.scales[6], batch.scales[7]);

                        deviceContext.UpdateSubresource(layerDataConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref layerCB, 0, 0);

                        for (int s = 0; s < 8; s++)
                            _srvScratch[s] = s < batch.materialFDIDs.Length && batch.materialFDIDs[s] != 0 ? BLPCache.GetCurrent((uint)batch.materialFDIDs[s], defaultTexture) : defaultTexture;
                        if (batch.materialFDIDs.Length > 0)
                            deviceContext.PSSetShaderResources(0, 8, ref _srvScratch[0]);

                        for (int s = 0; s < 8; s++)
                            _srvScratch[s] = s < batch.heightMaterialFDIDs.Length && batch.heightMaterialFDIDs[s] != 0 ? BLPCache.GetCurrent((uint)batch.heightMaterialFDIDs[s], defaultTexture) : defaultTexture;
                        if (batch.heightMaterialFDIDs.Length > 0)
                            deviceContext.PSSetShaderResources(8, 8, ref _srvScratch[0]);

                        deviceContext.PSSetShaderResources(16, 2, ref batch.alphaMaterialID[0]);

                        deviceContext.DrawIndexed(768, (uint)c * 768, 0);
                        drawCalls++;
                        verticeCount += 768;
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

                                (var boxDrawCalls, var boxVerticeCount) = DrawBoundingBox(localBox, modelMatrix, color, projectionMatrix, cameraMatrix);
                                drawCalls += boxDrawCalls;
                                verticeCount += boxVerticeCount;
                            }
                        }

                        if (ShowBoundingSpheres || sceneObject.IsSelected)
                        {
                            var sphere = sceneObject.GetBoundingSphere();
                            if (sphere.HasValue)
                            {
                                (var sphereDrawCalls, var sphereVerticeCount) = DrawBoundingSphere(sphere.Value, new Vector4(0, 0.5f, 1, 1), projectionMatrix, cameraMatrix);
                                drawCalls += sphereDrawCalls;
                                verticeCount += sphereVerticeCount;
                            }
                        }
                    }
                }

                deviceContext.RSSetState(rasterizerState);
                deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
                ComPtr<ID3D11DepthStencilState> nullDSS = default;
                deviceContext.OMSetDepthStencilState(nullDSS, 0);
            }

            //swapchain.Present(1, 0);

            gizmoWasUsing = false;
            gizmoWasOver = false;

            return (drawCalls, verticeCount);
        }

        private static bool IsWithinRenderDistance(Vector3 cameraPosition, Vector3 center, float radius, float distance)
        {
            var maxDistance = Math.Max(0f, distance) + Math.Max(0f, radius);
            return Vector3.DistanceSquared(cameraPosition, center) <= maxDistance * maxDistance;
        }

        private unsafe (uint drawCalls, uint verticeCount) DrawBoundingSphere(BoundingSphere sphere, Vector4 color, Matrix4x4 projection, Matrix4x4 view)
        {
            uint drawCalls = 0;
            uint verticeCount = 0;

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
            verticeCount += (uint)vertCount;
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
            drawCalls++;
            sphereVB.Dispose();

            return (drawCalls, verticeCount);
        }

        private unsafe (uint drawCalls, uint verticeCount) DrawBoundingBox(BoundingBox localBox, Matrix4x4 modelMatrix, Vector4 color, Matrix4x4 projection, Matrix4x4 view)
        {
            uint drawCalls = 0;
            uint verticeCount = 0;

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
            drawCalls++;
            verticeCount += 24;

            return (drawCalls, verticeCount);
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                textureSampler.Dispose();
                clampSampler.Dispose();
                depthStencilView.Dispose();
                depthTexture.Dispose();
                adtPerObjectConstantBuffer.Dispose();
                layerDataConstantBuffer.Dispose();
                m2PerObjectConstantBuffer.Dispose();
                wmoPerObjectConstantBuffer.Dispose();
                instanceMatrixBuffer.Dispose();
                defaultTexture.Dispose();
                bboxDepthStencilState.Dispose();
                bboxConstantBuffer.Dispose();
                bboxVertexBuffer.Dispose();
                rasterizerState.Dispose();
                wmoRasterizerState.Dispose();
                wireframeRasterizerState.Dispose();
                foreach (var bs in _blendStates)
                    bs.Dispose();
            }
        }
    }
}
