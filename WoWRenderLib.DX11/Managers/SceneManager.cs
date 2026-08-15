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
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Profiling;
using WoWRenderLib.DX11.Renderer;
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
        private readonly HashSet<MapTile> tilesQueuedForLoad = [];
        private readonly HashSet<MapTile> tilesQueuedForUnload = [];
        private readonly HashSet<MapTile> tilesInFlight = [];

        private int totalTilesToLoad = 0;
        private readonly Dictionary<uint, uint> uuidUsers = [];
        private readonly HashSet<MapTile> loadedTiles = [];
        private readonly List<ADTContainer> adtContainers = [];
        private readonly HashSet<(byte X, byte Y)> availableWdtTiles = [];
        private readonly Dictionary<MapTile, TileSceneBounds> tileSceneBounds = [];
        private readonly Dictionary<uint, TileSceneBounds> tileSceneBoundsByRoot = [];
        private readonly HashSet<uint> coarseCulledTileRoots = [];

        private WDT? currentWDT;
        public uint CurrentWDTFileDataID { get; private set; } = 775971;
        public Container3D? SelectedObject { get; set; } = null;
        public bool SelectionVisualsEnabled { get; set; } = true;
        public bool ShowBoundingBoxes { get; set; } = false;
        public bool ShowBoundingSpheres { get; set; } = false;

        public bool RenderADT { get; set; } = true;
        public bool RenderWMO { get; set; } = true;
        public bool RenderM2 { get; set; } = true;
        public bool EnableWmoPortalCulling { get; set; }
        public int TileLoadingDistance { get; set; } = 4;
        public float TerrainRenderDistance { get; set; } = 20_000f;
        public float ModelRenderDistance { get; set; } = 20_000f;
        public float MinimumModelScreenSizePixels { get; set; } = 1f;
        public float TerrainLodTransitionPixels { get; set; } = 32f;
        public Vector3? TerrainBrushWorldPosition { get; private set; }
        public float TerrainBrushRadius { get; private set; }
        public float TerrainBrushInnerRadius { get; private set; }
        public Vector4 TerrainBrushColor { get; private set; } = new(0.2f, 0.85f, 1f, 1f);
        private Vector3[] _terrainBrushOuterPoints = [];
        private Vector3[] _terrainBrushInnerPoints = [];
        private Vector3 _lastTerrainBrushCenter;
        private float _lastTerrainBrushRadius;
        private float _lastTerrainBrushInnerRadius;
        private bool _terrainBrushProjectionDirty = true;
        private ComPtr<ID3D11Buffer> _terrainBrushOuterVertexBuffer = default;
        private ComPtr<ID3D11Buffer> _terrainBrushInnerVertexBuffer = default;
        private Dictionary<TerrainTileId, ADTVertex[]>? _activeTerrainStrokeBefore;

        // World-space light from north-west at a 45° elevation. WoW's world
        // axes map north/west to the negative X/Y directions in this renderer.
        public Vector3 LightDirection { get; set; } = new(-0.5f, -0.5f, 0.70710678f);
        public Vector3 AmbientColor { get; set; } = new(104f / 255f, 130f / 255f, 154f / 255f);
        public Vector3 DiffuseColor { get; set; } = new(1f, 136f / 255f, 0f);

        private const int MaxInstancesPerBatch = 1024;

        private CompiledShader adtShaderProgram;
        private readonly CompiledShader[] adtLayerShaderPrograms = new CompiledShader[8];
        private CompiledShader wmoShaderProgram;
        private CompiledShader m2ShaderProgram;
        private CompiledShader debugShaderProgram;
        private CompiledShader bboxShaderProgram;

        private readonly Queue<WMOContainer> pendingWMODoodads = [];
        public readonly Dictionary<(uint FileDataID, string EnabledGroupSignature), List<WMOContainer>> wmoInstances = [];
        public readonly Dictionary<uint, List<M2Container>> m2Instances = [];
        private readonly Dictionary<uint, M2InstancePacket> m2InstancePackets = [];

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
        private readonly Dictionary<uint, ComPtr<ID3D11ShaderResourceView>> _frameTextureSrvs = [];
        private readonly List<int> _visibleIndices = new(64);
        private readonly List<bool> _visibleTerrainFarLod = new(256);
        private readonly List<WmoVisibilityBatch> _wmoVisibilityBatches = [];
        private readonly string? _wmoGroupTraceFilter =
            Environment.GetEnvironmentVariable("WTEDITOR_TRACE_WMO_GROUP");
        private readonly HashSet<string> _tracedWmoGroups = [];
        private int _activeWmoVisibilityBatchCount;
        private long _renderFrameNumber;

        private uint _renderWidth = 1920;
        private uint _renderHeight = 1080;

        public int visibleChunks { get; private set; } = 0;
        public int visibleWMOs { get; private set; } = 0;
        public int visibleM2s { get; private set; } = 0;
        public int candidateChunks { get; private set; }
        public int candidateWMOs { get; private set; }
        public int candidateM2s { get; private set; }
        public int sizeCulledWMOs { get; private set; }
        public int sizeCulledM2s { get; private set; }
        public int farLodTerrainChunks { get; private set; }
        public int candidateTiles { get; private set; }
        public int coarseCulledTiles { get; private set; }
        public int portalCulledWmoGroups { get; private set; }
        public int portalCulledM2s { get; private set; }
        public int traversedWmoPortalReferences { get; private set; }
        public double CullingTimeMs { get; private set; }
        public int UploadedResourcesLastFrame { get; private set; }
        public double SceneSetupTimeMs { get; private set; }
        public double WmoCullingTimeMs { get; private set; }
        public double WmoSubmissionTimeMs { get; private set; }
        public double M2CullingTimeMs { get; private set; }
        public double M2SubmissionTimeMs { get; private set; }
        public double TerrainCullingTimeMs { get; private set; }
        public double TerrainSubmissionTimeMs { get; private set; }
        public double TileHierarchyCullingTimeMs { get; private set; }
        public double DebugSubmissionTimeMs { get; private set; }
        public uint WmoDrawCalls { get; private set; }
        public uint M2DrawCalls { get; private set; }
        public uint TerrainDrawCalls { get; private set; }
        public uint DebugDrawCalls { get; private set; }
        public uint WmoSubmittedInstances { get; private set; }
        public uint M2SubmittedInstances { get; private set; }
        public uint TerrainSubmittedChunks { get; private set; }
        public ulong WmoSubmittedIndices { get; private set; }
        public ulong M2SubmittedIndices { get; private set; }
        public ulong TerrainSubmittedIndices { get; private set; }
        public uint InstanceBufferMapCalls { get; private set; }
        public uint ConstantBufferUpdates { get; private set; }
        public uint TextureBindingCalls { get; private set; }
        public uint BlendStateBindings { get; private set; }
        public uint VertexBufferBindings { get; private set; }
        public uint IndexBufferBindings { get; private set; }

        public bool SceneLoaded => loadedTiles.Count > 0; // this won't work for WMO only maps
        public string StatusMessage { get; private set; } = "";

        public void Initialize(ShaderManager shaderManager, CompiledShader adtShader, CompiledShader wmoShader, CompiledShader m2Shader, CompiledShader bboxShader)
        {
            adtShaderProgram = adtShader;
            LoadAdtLayerShaders();
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

        private unsafe float ApplyBlendMode(int blendType, ref int currentBlendType)
        {
            if ((uint)blendType >= (uint)_blendStates.Length)
                blendType = 0;

            if (currentBlendType != blendType)
            {
                float blendFactor = 1f;
                deviceContext.OMSetBlendState(_blendStates[blendType], ref blendFactor, 0xFFFFFFFF);
                currentBlendType = blendType;
                BlendStateBindings++;
            }

            return blendType == 1 ? 0.90393700787f : -1.0f;
        }

        private ComPtr<ID3D11ShaderResourceView> ResolveFrameTexture(uint fileDataId)
        {
            if (fileDataId == 0)
                return defaultTexture;

            if (_frameTextureSrvs.TryGetValue(fileDataId, out var texture))
                return texture;

            texture = BLPCache.GetCurrent(fileDataId, defaultTexture);
            _frameTextureSrvs.Add(fileDataId, texture);
            return texture;
        }

        private void ResetWmoVisibilityBatches()
        {
            for (var index = 0; index < _activeWmoVisibilityBatchCount; index++)
                _wmoVisibilityBatches[index].InstanceIndices.Clear();
            _activeWmoVisibilityBatchCount = 0;
        }

        private WmoVisibilityBatch GetWmoVisibilityBatch(ReadOnlySpan<bool> groupMask)
        {
            for (var index = 0; index < _activeWmoVisibilityBatchCount; index++)
            {
                if (_wmoVisibilityBatches[index].Matches(groupMask))
                    return _wmoVisibilityBatches[index];
            }

            WmoVisibilityBatch batch;
            if (_activeWmoVisibilityBatchCount == _wmoVisibilityBatches.Count)
            {
                batch = new WmoVisibilityBatch();
                _wmoVisibilityBatches.Add(batch);
            }
            else
            {
                batch = _wmoVisibilityBatches[_activeWmoVisibilityBatchCount];
            }
            _activeWmoVisibilityBatchCount++;
            batch.Begin(groupMask);
            return batch;
        }

        private void TraceWmoGroupVisibility(
            in WorldModel wmo,
            WMOContainer instance,
            ReadOnlySpan<bool> enabledGroups,
            ReadOnlySpan<bool> visibleGroups,
            bool portalApplied,
            Vector3 cameraPosition)
        {
            if (string.IsNullOrWhiteSpace(_wmoGroupTraceFilter))
                return;

            var tokens = _wmoGroupTraceFilter.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var placementMatches = tokens.Contains("*") || tokens.Any(token =>
                uint.TryParse(token, out var id) && id == instance.UniqueID);

            for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
            {
                var group = wmo.groupBatches[groupIndex];
                var groupMatches = tokens.Any(token =>
                    group.groupName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    group.mogiGroupName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    (uint.TryParse(token, out var id) && id == group.groupID));
                if (!placementMatches && !groupMatches)
                    continue;

                var traceKey = $"{wmo.rootWMOFileDataID}:{instance.UniqueID}:{groupIndex}";
                if (!_tracedWmoGroups.Add(traceKey))
                    continue;

                Console.WriteLine(
                    "WTEDITOR_WMO_TRACE " +
                    $"root={wmo.rootWMOFileDataID} placement={instance.UniqueID} " +
                    $"renderIndex={groupIndex} sourceIndex={group.sourceGroupIndex} " +
                    $"groupID={group.groupID} mogpName={group.groupName} mogiName={group.mogiGroupName} " +
                    $"mogpFlags=0x{group.flags:X8} mogiFlags=0x{group.mogiFlags:X8} " +
                    $"enabled={enabledGroups[groupIndex]} portalApplied={portalApplied} " +
                    $"visible={visibleGroups[groupIndex]} portalLinks={group.portalLinks.Length} " +
                    $"modr={group.doodadReferences.Length} camera={cameraPosition}");
            }
        }

        private void LoadAdtLayerShaders()
        {
            var layerCounts = new[] { 1, 2, 4, 8 };
            for (var index = 0; index < layerCounts.Length; index++)
            {
                adtLayerShaderPrograms[index] =
                    shaderManager.GetOrCompileAdtShader(layerCounts[index], false);
                adtLayerShaderPrograms[index + 4] =
                    shaderManager.GetOrCompileAdtShader(layerCounts[index], true);
            }
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
                tilesToLoad.Clear();
                tilesToUnload.Clear();
                tilesQueuedForLoad.Clear();
                tilesQueuedForUnload.Clear();
                tilesInFlight.Clear();
                availableWdtTiles.Clear();

                foreach (var bounds in tileSceneBounds.Values)
                    bounds.Dispose();
                tileSceneBounds.Clear();
                tileSceneBoundsByRoot.Clear();

                lock (SceneObjectLock)
                {
                    SceneObjects.Clear();
                    adtContainers.Clear();
                }

                CurrentWDTFileDataID = wdtFileDataID;
                currentWDT = WDTCache.GetOrLoad(CurrentWDTFileDataID);
                RebuildAvailableTileIndex();
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
            if (currentWDT == null)
            {
                currentWDT = WDTCache.GetOrLoad(CurrentWDTFileDataID);
                RebuildAvailableTileIndex();
            }
            return currentWDT;
        }

        private void RebuildAvailableTileIndex()
        {
            availableWdtTiles.Clear();
            if (currentWDT == null)
                return;

            foreach (var tile in currentWDT.Value.tiles)
                availableWdtTiles.Add(tile);
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

            var desiredTiles = new HashSet<MapTile>();

            var viewDistance = Math.Clamp(TileLoadingDistance, 0, 32);
            for (int xOffset = -viewDistance; xOffset <= viewDistance; xOffset++)
            {
                for (int yOffset = -viewDistance; yOffset <= viewDistance; yOffset++)
                {
                    int tileX = x + xOffset;
                    int tileY = y + yOffset;

                    if (tileX < 0 || tileX > 63 || tileY < 0 || tileY > 63)
                        continue;

                    if (!availableWdtTiles.Contains(((byte)tileX, (byte)tileY)))
                        continue;

                    var mapTile = new MapTile
                    {
                        tileX = (byte)tileX,
                        tileY = (byte)tileY,
                        wdtFileDataID = CurrentWDTFileDataID
                    };

                    desiredTiles.Add(mapTile);

                    if (!loadedTiles.Contains(mapTile) &&
                        !tilesQueuedForLoad.Contains(mapTile) &&
                        !tilesInFlight.Contains(mapTile))
                    {
                        tilesToLoad.Enqueue(mapTile);
                        tilesQueuedForLoad.Add(mapTile);
                        totalTilesToLoad++;
                    }
                }
            }

            foreach (var tile in loadedTiles)
            {
                    if (!desiredTiles.Contains(tile) &&
                        !IsTerrainTileModified(tile) &&
                        tilesQueuedForUnload.Add(tile))
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
                tilesQueuedForUnload.Remove(tile);
                // TODO: this is rough... tilesToLoad should be readonly and not entirely redefined every unload...
                if (tilesQueuedForLoad.Remove(tile))
                {
                    tilesToLoad = new Queue<MapTile>(tilesToLoad.Where(t => t != tile));
                    continue;
                }

                tilesInFlight.Remove(tile);
                loadedTiles.Remove(tile);

                Console.WriteLine("Unloading tile " + tile.tileX + ", " + tile.tileY);
                lock (SceneObjectLock)
                {
                    var adtToRemove = adtContainers.FirstOrDefault(a =>
                        a.mapTile.wdtFileDataID == tile.wdtFileDataID &&
                        a.mapTile.tileX == tile.tileX &&
                        a.mapTile.tileY == tile.tileY);

                    if (adtToRemove == null)
                        continue;

                    // Keep edited tiles resident until a save operation clears
                    // their dirty state; unloading would otherwise discard the
                    // in-memory edit before a writer can consume it.
                    if (adtToRemove.IsModified)
                    {
                        loadedTiles.Add(tile);
                        continue;
                    }

                    // remove callback so it cant finish loading, nyehehe
                    adtToRemove.LoadCallback -= OnADTContainerLoaded;

                    SceneObjects.Remove(adtToRemove);
                    adtContainers.Remove(adtToRemove);

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

                    if (tileSceneBounds.Remove(tile, out var bounds))
                    {
                        tileSceneBoundsByRoot.Remove(rootId);
                        bounds.Dispose();
                    }
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

                var m2Container = new M2Container(device, doodad.filedataid, wmoContainer.ParentFileDataId)
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
            var spawnedWmoDoodads = false;
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
                spawnedWmoDoodads = true;
            }

            if (spawnedWmoDoodads)
                UpdateInstanceList();

            if (tilesToLoad.Count == 0)
                return ADTCache.GetLoadQueueCount() > 0 || WMOCache.GetLoadQueueCount() > 0 || M2Cache.GetLoadQueueCount() > 0 || BLPCache.GetQueueCount() > 0 || pendingWMODoodads.Count > 0;

            while (tilesToLoad.Count > 0 && queueTimer.ElapsedMilliseconds < 10)
            {
                var mapTile = tilesToLoad.Dequeue();
                tilesQueuedForLoad.Remove(mapTile);
                tilesInFlight.Add(mapTile);

                try
                {
                    var adtContainer = new ADTContainer(device, mapTile);
                    adtContainer.LoadCallback += OnADTContainerLoaded;

                    ADTCache.GetOrLoad(mapTile, mapTile.wdtFileDataID, adtContainer.OnLoaded);

                    lock (SceneObjectLock)
                    {
                        SceneObjects.Add(adtContainer);
                        adtContainers.Add(adtContainer);
                    }
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

            if (!tileSceneBounds.TryGetValue(adtContainer.mapTile, out var owningTileBounds))
            {
                owningTileBounds = new TileSceneBounds(adtContainer.mapTile);
                tileSceneBounds.Add(adtContainer.mapTile, owningTileBounds);
            }
            owningTileBounds.SetTerrain(terrain.rootADTFileDataID, terrain.terrainBounds);
            tileSceneBoundsByRoot[terrain.rootADTFileDataID] = owningTileBounds;

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
                    PlacementFlags = worldModel.flags,
                    PlacementDoodadSet = worldModel.doodadSet,
                    PlacementNameSet = worldModel.nameSet,
                    OnDoodadSetsChanged = RefreshWMODoodads,
                    OnGroupsChanged = _ => UpdateWMOInstanceList()
                };

                worldModelContainer.SetDoodadSetsToEnable(worldModel.doodadSetIDs);

                lock (SceneObjectLock)
                    SceneObjects.Add(worldModelContainer);
                owningTileBounds.AddObject(worldModelContainer);

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
                    Scale = doodad.scale,
                    UniqueID = doodad.uniqueID,
                    PlacementFlags = doodad.flags
                };

                lock (SceneObjectLock)
                    SceneObjects.Add(doodadContainer);
                owningTileBounds.AddObject(doodadContainer);
            }

            UpdateInstanceList();
            tilesInFlight.Remove(adtContainer.mapTile);
            loadedTiles.Add(adtContainer.mapTile);
        }

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
            if ((worldModel.PlacementFlags & 0x80) == 0)
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
                _activeTerrainStrokeBefore = [];
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
                        HaveSamePositions(before, after))
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
                    terrain.vertices = (useAfter ? edit.After : edit.Before).ToArray();
                    RebuildTerrainBounds(ref terrain);
                    UploadTerrainVertices(terrain);
                    adt.UpdateTerrain(terrain);
                    adt.RefreshModifiedState();
                    MarkTileBoundsDirty(terrain.rootADTFileDataID);
                }
            }
        }

        private static bool HaveSamePositions(ADTVertex[] left, ADTVertex[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index].Position != right[index].Position)
                    return false;
            }

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

                    // Make doodads unselectable
                    if (sceneObject is M2Container m2container && m2container.ParentWMO != null)
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

        public void ClearTerrainBrush()
        {
            TerrainBrushWorldPosition = null;
            TerrainBrushRadius = 0f;
            TerrainBrushInnerRadius = 0f;
            _terrainBrushOuterPoints = [];
            _terrainBrushInnerPoints = [];
            _terrainBrushProjectionDirty = true;
        }

        public void UpdateTerrainBrush(
            Vector2 mousePosition,
            Camera camera,
            int windowWidth,
            int windowHeight,
            TerrainBrushInput input,
            bool apply,
            float deltaTime)
        {
            var ray = camera.GetRayFromScreen(
                mousePosition.X,
                mousePosition.Y,
                windowWidth,
                windowHeight);

            if (!TryRaycastTerrain(ray, out var hit))
            {
                ClearTerrainBrush();
                return;
            }

            TerrainBrushWorldPosition = hit.WorldPosition;
            TerrainBrushRadius = Math.Clamp(input.Radius, 1f, 1000f);
            TerrainBrushInnerRadius = Math.Clamp(input.InnerRadius, 0f, 1f);
            TerrainBrushColor = TerrainBrushTools.Get(input.ToolMode).PreviewColor;

            if (apply)
                ApplyTerrainBrush(hit, input, Math.Clamp(deltaTime, 0f, 0.1f));

            var deltaX = _lastTerrainBrushCenter.X - hit.WorldPosition.X;
            var deltaY = _lastTerrainBrushCenter.Y - hit.WorldPosition.Y;
            var horizontalMovementSquared = (deltaX * deltaX) + (deltaY * deltaY);
            var centerMoved = horizontalMovementSquared > 0.25f;
            var accumulatedHeightChange =
                MathF.Abs(_lastTerrainBrushCenter.Z - hit.WorldPosition.Z) > 0.25f;
            var radiusChanged = MathF.Abs(_lastTerrainBrushRadius - TerrainBrushRadius) > 0.0001f;
            var innerRadiusChanged = MathF.Abs(_lastTerrainBrushInnerRadius - TerrainBrushInnerRadius) > 0.0001f;
            if (_terrainBrushProjectionDirty ||
                centerMoved ||
                accumulatedHeightChange ||
                radiusChanged ||
                innerRadiusChanged)
            {
                var projection = ProjectTerrainBrush(
                    hit.WorldPosition,
                    TerrainBrushRadius,
                    TerrainBrushRadius * TerrainBrushInnerRadius);
                _terrainBrushOuterPoints = projection.Outer;
                _terrainBrushInnerPoints = projection.Inner;
                _lastTerrainBrushCenter = hit.WorldPosition;
                _lastTerrainBrushRadius = TerrainBrushRadius;
                _lastTerrainBrushInnerRadius = TerrainBrushInnerRadius;
                _terrainBrushProjectionDirty = false;
            }
        }

        private bool TryRaycastTerrain(Ray ray, out TerrainRayHit closestHit)
        {
            lock (SceneObjectLock)
                return TryRaycastTerrainLocked(ray, out closestHit);
        }

        private bool TryRaycastTerrainLocked(Ray ray, out TerrainRayHit closestHit)
        {
            closestHit = default;
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
                if (!IntersectionTests.RayIntersectsBox(localRay, terrain.terrainBounds, out _))
                    continue;
                var candidate = new TerrainRaycastChunk(adt, modelMatrix, inverseModel, -1);

                for (var chunkIndex = 0; chunkIndex < terrain.chunkBounds.Length; chunkIndex++)
                    TryRaycastTerrainChunk(
                        candidate,
                        ray,
                        chunkIndex,
                        ref closestDistance,
                        ref closestHit);
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

            var indexStart = chunkIndex * 768;
            var indexEnd = Math.Min(indexStart + 768, terrain.indices.Length);
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
                        out var localPosition) ||
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
                    localPosition,
                    worldPosition,
                    worldNormal);
            }
        }

        private List<TerrainRaycastChunk> BuildTerrainRaycastCandidatesLocked(
            Vector3 center,
            float radius)
        {
            var candidates = new List<TerrainRaycastChunk>();

            WorldChunkRange.ForEachChunkInRange(
                adtContainers,
                center,
                radius,
                static adt => adt.IsLoaded &&
                              adt.Terrain.vertices is { Length: > 0 } &&
                              adt.Terrain.indices is { Length: > 0 },
                static adt => adt.GetModelMatrix(),
                static adt => adt.Terrain.terrainBounds,
                static adt => adt.Terrain.chunkBounds,
                static bounds => bounds,
                context =>
                {
                    candidates.Add(new TerrainRaycastChunk(
                        context.Tile,
                        context.ModelMatrix,
                        context.InverseModelMatrix,
                        context.ChunkIndex));
                    return false;
                });

            return candidates;
        }

        private void ApplyTerrainBrush(TerrainRayHit hit, TerrainBrushInput input, float deltaTime)
        {
            var radius = Math.Clamp(input.Radius, 1f, 1000f);
            var speed = Math.Clamp(input.Speed, 0.1f, 50f);
            var tool = TerrainBrushTools.Get(input.ToolMode);
            lock (SceneObjectLock)
            {
                ADTContainer? activeAdt = null;
                Terrain activeTerrain = default;
                var activeChanged = false;

                WorldChunkRange.ForEachChunkInRange(
                    adtContainers,
                    hit.WorldPosition,
                    radius,
                    static adt => adt.IsLoaded && adt.Terrain.vertices is { Length: > 0 },
                    static adt => adt.GetModelMatrix(),
                    static adt => adt.Terrain.terrainBounds,
                    static adt => adt.Terrain.chunkBounds,
                    static bounds => bounds,
                    context =>
                    {
                        if (!ReferenceEquals(activeAdt, context.Tile))
                        {
                            activeAdt = context.Tile;
                            activeTerrain = context.Tile.Terrain;
                            activeChanged = false;
                        }

                        if (_activeTerrainStrokeBefore is { } strokeBefore)
                        {
                            var tileId = TerrainTileId.From(context.Tile.mapTile);
                            if (!strokeBefore.ContainsKey(tileId))
                            {
                                strokeBefore[tileId] = activeTerrain.vertices?.ToArray() ?? [];
                            }
                        }

                        var changed = ApplyTerrainChunk(
                            ref activeTerrain,
                            context.ChunkIndex,
                            context.LocalCenter,
                            context.LocalRadius,
                            Math.Clamp(input.InnerRadius, 0f, 1f),
                            speed,
                            tool,
                            input,
                            deltaTime);
                        activeChanged |= changed;
                        return changed;
                    },
                    tile =>
                    {
                        if (!ReferenceEquals(activeAdt, tile))
                            return;

                        if (activeChanged)
                        {
                            RebuildTerrainAggregateBounds(ref activeTerrain);
                            UploadTerrainVertices(activeTerrain);
                            tile.UpdateTerrain(activeTerrain);
                            tile.RefreshModifiedState();
                            MarkTileBoundsDirty(activeTerrain.rootADTFileDataID);
                        }

                        activeAdt = null;
                        activeChanged = false;
                    });
            }
        }

        private bool ApplyTerrainChunk(
            ref Terrain terrain,
            int chunkIndex,
            Vector3 localCenter,
            float radius,
            float innerRadiusRatio,
            float speed,
            TerrainBrushTool tool,
            TerrainBrushInput input,
            float deltaTime)
        {
            var passes = tool.GetPassCount(input.SmoothIterations);
            var radiusSquared = radius * radius;
            var innerRadius = radius * innerRadiusRatio;
            var changed = false;

            for (var pass = 0; pass < passes; pass++)
            {
                var chunkChanged = false;
                var start = chunkIndex * 145;
                var end = Math.Min(start + 145, terrain.vertices.Length);
                for (var vertexIndex = start; vertexIndex < end; vertexIndex++)
                {
                    var vertex = terrain.vertices[vertexIndex];
                    var deltaX = vertex.Position.X - localCenter.X;
                    var deltaY = vertex.Position.Y - localCenter.Y;
                    var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                    if (distanceSquared > radiusSquared)
                        continue;

                    var distance = MathF.Sqrt(distanceSquared);
                    var falloff = BrushMath.CalculateFalloff(distance, radius, innerRadius);
                    var amount = speed * deltaTime * falloff;
                    var height = vertex.Position.Z;
                    var nextHeight = tool.Apply(new TerrainBrushSample(
                        terrain.vertices,
                        vertexIndex,
                        height,
                        amount,
                        radius,
                        input.FlattenHeight,
                        input.Action));

                    if (MathF.Abs(nextHeight - height) < 0.0001f)
                        continue;

                    vertex.Position.Z = nextHeight;
                    terrain.vertices[vertexIndex] = vertex;
                    changed = true;
                    chunkChanged = true;
                }

                if (chunkChanged)
                    RebuildTerrainChunkBounds(ref terrain, chunkIndex);
            }

            return changed;
        }

        private unsafe void UploadTerrainVertices(Terrain terrain)
        {
            if (terrain.vertexBuffer.Handle == null || terrain.vertices == null)
                return;

            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(deviceContext.Map(
                terrain.vertexBuffer,
                0,
                Map.WriteDiscard,
                0,
                ref mapped));
            var destination = new Span<ADTVertex>(mapped.PData, terrain.vertices.Length);
            terrain.vertices.AsSpan().CopyTo(destination);
            deviceContext.Unmap(terrain.vertexBuffer, 0);
        }

        private static void RebuildTerrainBounds(ref Terrain terrain)
        {
            for (var chunkIndex = 0; chunkIndex < terrain.chunkBounds.Length; chunkIndex++)
                RebuildTerrainChunkBounds(ref terrain, chunkIndex);

            RebuildTerrainAggregateBounds(ref terrain);
        }

        private static void RebuildTerrainChunkBounds(ref Terrain terrain, int chunkIndex)
        {
            var start = chunkIndex * 145;
            var end = Math.Min(start + 145, terrain.vertices.Length);
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
            out Vector3 hit)
        {
            const float epsilon = 0.000001f;
            distance = 0f;
            hit = default;
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
            return true;
        }

        private readonly record struct TerrainRayHit(
            ADTContainer Container,
            Vector3 LocalPosition,
            Vector3 WorldPosition,
            Vector3 WorldNormal);

        private readonly record struct TerrainRaycastChunk(
            ADTContainer Container,
            Matrix4x4 ModelMatrix,
            Matrix4x4 InverseModel,
            int ChunkIndex);

        public (uint drawCalls, ulong submittedIndices) RenderScene(Camera camera, out bool gizmoWasUsing, out bool gizmoWasOver) =>
            RenderScene(camera, out gizmoWasUsing, out gizmoWasOver, null);

        internal (uint drawCalls, ulong submittedIndices) RenderScene(
            Camera camera,
            out bool gizmoWasUsing,
            out bool gizmoWasOver,
            GpuFrameTimer? gpuTimer)
        {
            var sceneSetupStarted = Stopwatch.GetTimestamp();
            _renderFrameNumber++;
            uint drawCalls = 0;
            ulong submittedIndexCount = 0;

            CullingTimeMs = 0;
            SceneSetupTimeMs = 0;
            WmoCullingTimeMs = 0;
            WmoSubmissionTimeMs = 0;
            M2CullingTimeMs = 0;
            M2SubmissionTimeMs = 0;
            TerrainCullingTimeMs = 0;
            TerrainSubmissionTimeMs = 0;
            TileHierarchyCullingTimeMs = 0;
            DebugSubmissionTimeMs = 0;
            WmoDrawCalls = 0;
            M2DrawCalls = 0;
            TerrainDrawCalls = 0;
            DebugDrawCalls = 0;
            WmoSubmittedInstances = 0;
            M2SubmittedInstances = 0;
            TerrainSubmittedChunks = 0;
            WmoSubmittedIndices = 0;
            M2SubmittedIndices = 0;
            TerrainSubmittedIndices = 0;
            InstanceBufferMapCalls = 0;
            ConstantBufferUpdates = 0;
            TextureBindingCalls = 0;
            BlendStateBindings = 0;
            VertexBufferBindings = 0;
            IndexBufferBindings = 0;
            _frameTextureSrvs.Clear();
            var currentBlendType = -1;

            deviceContext.RSSetState(rasterizerState);

#if DEBUG
            if (shaderManager.CheckForChanges())
            {
                adtShaderProgram = shaderManager.GetOrCompileShader("adt");
                LoadAdtLayerShaders();
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
            candidateM2s = 0;
            candidateWMOs = 0;
            candidateChunks = 0;
            sizeCulledWMOs = 0;
            sizeCulledM2s = 0;
            farLodTerrainChunks = 0;
            candidateTiles = tileSceneBounds.Count;
            coarseCulledTiles = 0;
            portalCulledWmoGroups = 0;
            portalCulledM2s = 0;
            traversedWmoPortalReferences = 0;

            var verticalProjectionScale = projectionMatrix.M22;
            var normalizedCameraForward = camera.Front.LengthSquared() > float.Epsilon
                ? Vector3.Normalize(camera.Front)
                : Vector3.UnitX;

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

            SceneSetupTimeMs = Stopwatch.GetElapsedTime(sceneSetupStarted).TotalMilliseconds;

            var tileCullingStarted = Stopwatch.GetTimestamp();
            coarseCulledTileRoots.Clear();
            foreach (var bounds in tileSceneBounds.Values)
            {
                bounds.IsCoarseCulledThisFrame = false;
                if (bounds.RootAdtFileDataId == 0 ||
                    !bounds.TryGetCombinedBounds(out var combinedBounds))
                {
                    continue;
                }

                if (frustum.ClassifyBox(combinedBounds.Min, combinedBounds.Max) !=
                    Frustum.BoxIntersection.Outside)
                {
                    continue;
                }

                bounds.IsCoarseCulledThisFrame = true;
                coarseCulledTileRoots.Add(bounds.RootAdtFileDataId);
                coarseCulledTiles++;
            }
            TileHierarchyCullingTimeMs = Stopwatch.GetElapsedTime(tileCullingStarted).TotalMilliseconds;
            CullingTimeMs += TileHierarchyCullingTimeMs;

            // Set up WMO stuff, we do this before the loop since they're all shared
            var passStarted = Stopwatch.GetTimestamp();
            gpuTimer?.BeginWorldModels();
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
            var lastWmoVertexShader = int.MinValue;
            var lastWmoPixelShader = int.MinValue;
            var lastWmoAlphaRef = float.NaN;

            var viewProjection = cameraMatrix * projectionMatrix;
            foreach (var (_, instances) in wmoInstances)
            {
                if (!RenderWMO || instances.Count == 0)
                    continue;

                var firstInstance = instances[0];
                if (!firstInstance.IsLoaded)
                    continue;

                var wmo = firstInstance.GetWMO();
                candidateWMOs += instances.Count;
                var cullingStarted = Stopwatch.GetTimestamp();
                ResetWmoVisibilityBatches();
                for (int i = 0; i < instances.Count; i++)
                {
                    var instance = instances[i];
                    var sphere = instance.CachedBoundingSphere ?? instance.GetBoundingSphere();
                    if (sphere.HasValue &&
                        IsWithinRenderDistance(camera.Position, sphere.Value.Center, sphere.Value.Radius, ModelRenderDistance) &&
                        frustum.IsSphereVisible(sphere.Value.Center, sphere.Value.Radius))
                    {
                        if (!instance.IsSelected && ScreenSpaceCulling.IsBelowPixelThresholdNormalized(
                                camera.Position,
                                normalizedCameraForward,
                                sphere.Value.Center,
                                sphere.Value.Radius,
                                verticalProjectionScale,
                                _renderHeight,
                                MinimumModelScreenSizePixels))
                        {
                            sizeCulledWMOs++;
                            continue;
                        }

                        visibleWMOs++;
                        var enabledGroups = instance.EnabledGroups;
                        instance.GetPortalVisibilityBuffers(
                            wmo,
                            out var portalVisibleGroups,
                            out var portalVisibleDoodads,
                            out var portalVisibilityScratch);
                        var traversedPortalReferences = 0;
                        var portalApplied = EnableWmoPortalCulling &&
                            WmoPortalVisibility.TryCompute(
                                wmo,
                                instance.GetModelMatrix(),
                                viewProjection,
                                camera.Position,
                                enabledGroups,
                                portalVisibleGroups,
                                portalVisibleDoodads,
                                portalVisibilityScratch,
                                out traversedPortalReferences);
                        if (portalApplied)
                        {
                            traversedWmoPortalReferences += traversedPortalReferences;
                            for (var groupIndex = 0; groupIndex < enabledGroups.Length; groupIndex++)
                            {
                                if (enabledGroups[groupIndex] && !portalVisibleGroups[groupIndex])
                                    portalCulledWmoGroups++;
                            }
                        }
                        else
                        {
                            enabledGroups.CopyTo(portalVisibleGroups, 0);
                            portalVisibleDoodads.AsSpan().Fill(true);
                        }
                        TraceWmoGroupVisibility(
                            wmo,
                            instance,
                            enabledGroups,
                            portalVisibleGroups,
                            portalApplied,
                            camera.Position);
                        instance.SetPortalVisibilityFrame(_renderFrameNumber);
                        GetWmoVisibilityBatch(portalVisibleGroups).InstanceIndices.Add(i);
                    }
                }
                var cullingElapsed = Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;
                WmoCullingTimeMs += cullingElapsed;
                CullingTimeMs += cullingElapsed;

                if (_activeWmoVisibilityBatchCount == 0)
                    continue;

                for (var visibilityBatchIndex = 0;
                     visibilityBatchIndex < _activeWmoVisibilityBatchCount;
                     visibilityBatchIndex++)
                {
                    var visibilityBatch = _wmoVisibilityBatches[visibilityBatchIndex];
                    var visibleInstanceIndices = visibilityBatch.InstanceIndices;
                    var enabledGroups = visibilityBatch.GroupMask;
                    for (int batchStart = 0; batchStart < visibleInstanceIndices.Count; batchStart += MaxInstancesPerBatch)
                    {
                        int batchSize = Math.Min(MaxInstancesPerBatch, visibleInstanceIndices.Count - batchStart);

                        // the normal approach to do updatesubresource doesn't work for dynamic buffers, so we have to do the below block instead
                        unsafe
                        {
                            MappedSubresource mapped = default;
                            SilkMarshal.ThrowHResult(deviceContext.Map(instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                            var dest = new Span<Matrix4x4>(mapped.PData, batchSize);
                            for (int i = 0; i < batchSize; i++)
                                dest[i] = instances[visibleInstanceIndices[batchStart + i]].GetModelMatrix();

                            deviceContext.Unmap(instanceMatrixBuffer, 0);
                            InstanceBufferMapCalls++;
                        }

                        deviceContext.IASetVertexBuffers(1, 1, ref instanceMatrixBuffer, in instanceStride, in instanceOffset);
                        VertexBufferBindings++;

                        var currentGroupId = uint.MaxValue;

                        for (int j = 0; j < wmo.wmoRenderBatches.Length; j++)
                        {
                            var batch = wmo.wmoRenderBatches[j];
                            if (!enabledGroups[batch.groupID])
                                continue;

                            if (currentGroupId != batch.groupID)
                            {
                                var group = wmo.groupBatches[batch.groupID];
                                var vertexBuffer = group.vertexBuffer;
                                var indiceBuffer = group.indiceBuffer;
                                deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in wmoVertexStride, in wmoVertexOffset);
                                deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR16Uint, 0);
                                VertexBufferBindings++;
                                IndexBufferBindings++;
                                currentGroupId = batch.groupID;
                            }

                            wmoConstantBuffer.vertexShader = (int)ShaderEnums.WMOShaders[(int)batch.shader].VertexShader;
                            wmoConstantBuffer.pixelShader = (int)ShaderEnums.WMOShaders[(int)batch.shader].PixelShader;
                        // Match the OpenGL WMO path: -1 means opaque/no alpha
                        // test, while blend mode 1 supplies the alpha-test ref.
                            wmoConstantBuffer.alphaRef = ApplyBlendMode((int)batch.blendType, ref currentBlendType);

                            if (wmoConstantBuffer.vertexShader != lastWmoVertexShader ||
                                wmoConstantBuffer.pixelShader != lastWmoPixelShader ||
                                wmoConstantBuffer.alphaRef != lastWmoAlphaRef)
                            {
                                deviceContext.UpdateSubresource(wmoPerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref wmoConstantBuffer, 0, 0);
                                ConstantBufferUpdates++;
                                lastWmoVertexShader = wmoConstantBuffer.vertexShader;
                                lastWmoPixelShader = wmoConstantBuffer.pixelShader;
                                lastWmoAlphaRef = wmoConstantBuffer.alphaRef;
                            }

                            for (int s = 0; s < batch.materialFDIDs.Length; s++)
                                _srvScratch[s] = ResolveFrameTexture(batch.materialFDIDs[s]);
                            if (batch.materialFDIDs.Length > 0)
                            {
                                deviceContext.PSSetShaderResources(0, (uint)batch.materialFDIDs.Length, ref _srvScratch[0]);
                                TextureBindingCalls++;
                            }

                            deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchSize, batch.firstFace, 0, 0);

                            drawCalls++;
                            WmoDrawCalls++;
                            WmoSubmittedInstances += (uint)batchSize;
                            var submittedIndices = (ulong)batch.numFaces * (uint)batchSize;
                            submittedIndexCount += submittedIndices;
                            WmoSubmittedIndices += submittedIndices;
                        }
                    }
                }
            }
            gpuTimer?.EndWorldModels();
            WmoSubmissionTimeMs = Math.Max(
                0,
                Stopwatch.GetElapsedTime(passStarted).TotalMilliseconds - WmoCullingTimeMs);

            // Set up M2 stuff (unchanged per M2 so we do it before we loop)
            passStarted = Stopwatch.GetTimestamp();
            gpuTimer?.BeginDoodads();
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
            var lastM2BlendMode = float.NaN;
            var lastM2VertexShader = int.MinValue;
            var lastM2PixelShader = int.MinValue;
            var lastM2AlphaRef = float.NaN;

            foreach (var packet in m2InstancePackets.Values)
            {
                var instances = packet.Instances;
                if (!RenderM2 || instances.Count == 0)
                    continue;

                var m2 = instances[0].GetM2();
                if (!packet.EnsureSpatialData(m2))
                    continue;

                candidateM2s += instances.Count;
                var cullingStarted = Stopwatch.GetTimestamp();
                _visibleIndices.Clear();
                for (int i = 0; i < instances.Count; i++)
                {
                    var instance = instances[i];
                    if (instance.ParentWMO != null &&
                        !instance.ParentWMO.IsDoodadPortalVisible(
                            instance.WmoDoodadIndex,
                            _renderFrameNumber))
                    {
                        portalCulledM2s++;
                        continue;
                    }
                    var sphere = packet.WorldBounds[i];
                    if (IsWithinRenderDistance(camera.Position, sphere.Center, sphere.Radius, ModelRenderDistance) &&
                        frustum.IsSphereVisible(sphere.Center, sphere.Radius))
                    {
                        if (!instance.IsSelected && ScreenSpaceCulling.IsBelowPixelThresholdNormalized(
                                camera.Position,
                                normalizedCameraForward,
                                sphere.Center,
                                sphere.Radius,
                                verticalProjectionScale,
                                _renderHeight,
                                MinimumModelScreenSizePixels))
                        {
                            sizeCulledM2s++;
                            continue;
                        }

                        visibleM2s++;
                        _visibleIndices.Add(i);
                    }
                }
                var cullingElapsed = Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;
                M2CullingTimeMs += cullingElapsed;
                CullingTimeMs += cullingElapsed;

                if (_visibleIndices.Count == 0)
                    continue;

                var vertexBuffer = m2.vertexBuffer;
                var indiceBuffer = m2.indiceBuffer;

                deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in m2VertexStride, in m2VertexOffset);
                deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR16Uint, 0);
                VertexBufferBindings++;
                IndexBufferBindings++;

                for (int batchStart = 0; batchStart < _visibleIndices.Count; batchStart += MaxInstancesPerBatch)
                {
                    int batchCount = Math.Min(MaxInstancesPerBatch, _visibleIndices.Count - batchStart);

                    unsafe
                    {
                        MappedSubresource mapped = default;
                        SilkMarshal.ThrowHResult(deviceContext.Map(instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                        var dest = new Span<Matrix4x4>(mapped.PData, batchCount);
                        for (int i = 0; i < batchCount; i++)
                            dest[i] = packet.WorldMatrices[_visibleIndices[batchStart + i]];

                        deviceContext.Unmap(instanceMatrixBuffer, 0);
                        InstanceBufferMapCalls++;
                    }

                    deviceContext.IASetVertexBuffers(1, 1, ref instanceMatrixBuffer, in instanceStride, in instanceOffset);
                    VertexBufferBindings++;

                    for (int j = 0; j < m2.submeshes.Length; j++)
                    {
                        var batch = m2.submeshes[j];

                        m2ConstantBuffer.blendMode = batch.blendType;
                        m2ConstantBuffer.alphaRef = ApplyBlendMode((int)batch.blendType, ref currentBlendType);
                        m2ConstantBuffer.vertexShader = (int)batch.vertexShaderID;
                        m2ConstantBuffer.pixelShader = (int)batch.pixelShaderID;

                        if (m2ConstantBuffer.blendMode != lastM2BlendMode ||
                            m2ConstantBuffer.vertexShader != lastM2VertexShader ||
                            m2ConstantBuffer.pixelShader != lastM2PixelShader ||
                            m2ConstantBuffer.alphaRef != lastM2AlphaRef)
                        {
                            deviceContext.UpdateSubresource(m2PerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref m2ConstantBuffer, 0, 0);
                            ConstantBufferUpdates++;
                            lastM2BlendMode = m2ConstantBuffer.blendMode;
                            lastM2VertexShader = m2ConstantBuffer.vertexShader;
                            lastM2PixelShader = m2ConstantBuffer.pixelShader;
                            lastM2AlphaRef = m2ConstantBuffer.alphaRef;
                        }

                        for (int s = 0; s < batch.material.Length; s++)
                            _srvScratch[s] = ResolveFrameTexture(batch.material[s]);
                        if (batch.material.Length > 0)
                        {
                            deviceContext.PSSetShaderResources(0, (uint)batch.material.Length, ref _srvScratch[0]);
                            TextureBindingCalls++;
                        }

                        deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchCount, batch.firstFace, 0, 0);
                        drawCalls++;
                        M2DrawCalls++;
                        M2SubmittedInstances += (uint)batchCount;
                        var submittedIndices = (ulong)batch.numFaces * (uint)batchCount;
                        submittedIndexCount += submittedIndices;
                        M2SubmittedIndices += submittedIndices;
                    }
                }
            }
            gpuTimer?.EndDoodads();
            M2SubmissionTimeMs = Math.Max(
                0,
                Stopwatch.GetElapsedTime(passStarted).TotalMilliseconds - M2CullingTimeMs);

            ApplyBlendMode(0, ref currentBlendType);

            passStarted = Stopwatch.GetTimestamp();
            gpuTimer?.BeginTerrain();
            if (RenderADT)
            {
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
                deviceContext.UpdateSubresource(
                    layerDataConstantBuffer,
                    0,
                    ref Unsafe.NullRef<Box>(),
                    ref layerCB,
                    0,
                    0);
                ConstantBufferUpdates++;
                var currentAdtShaderLayerCount = 8;
                var currentAdtUsesHeightTextures = true;

                foreach (var adt in adtContainers)
                {
                    if (!adt.IsLoaded)
                        continue;

                    candidateChunks += adt.Terrain.chunkBounds.Length;
                    var cullingStarted = Stopwatch.GetTimestamp();
                    _visibleIndices.Clear();
                    _visibleTerrainFarLod.Clear();

                    if (coarseCulledTileRoots.Contains(adt.Terrain.rootADTFileDataID))
                    {
                        var coarseCullingElapsed = Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;
                        TerrainCullingTimeMs += coarseCullingElapsed;
                        CullingTimeMs += coarseCullingElapsed;
                        continue;
                    }

                    var terrainSphere = adt.Terrain.terrainBoundingSphere;
                    var terrainFrustumIntersection = frustum.ClassifyBox(
                        adt.Terrain.terrainBounds.Min,
                        adt.Terrain.terrainBounds.Max);
                    if (terrainFrustumIntersection != Frustum.BoxIntersection.Outside &&
                        IsWithinRenderDistance(
                            camera.Position,
                            terrainSphere.Center,
                            terrainSphere.Radius,
                            TerrainRenderDistance))
                    {
                        var skipChunkFrustumTests =
                            terrainFrustumIntersection == Frustum.BoxIntersection.Inside;
                        var skipChunkDistanceTests = IsFullyWithinRenderDistance(
                            camera.Position,
                            terrainSphere.Center,
                            terrainSphere.Radius,
                            TerrainRenderDistance);

                        for (var c = 0; c < adt.Terrain.chunkBounds.Length; c++)
                        {
                            var bounds = adt.Terrain.chunkBounds[c];
                            var boundsSphere = adt.Terrain.chunkBoundingSpheres[c];
                            if ((!skipChunkDistanceTests && !IsWithinRenderDistance(
                                    camera.Position,
                                    boundsSphere.Center,
                                    boundsSphere.Radius,
                                    TerrainRenderDistance)) ||
                                (!skipChunkFrustumTests && !frustum.IsBoxVisible(bounds.Min, bounds.Max)))
                            {
                                continue;
                            }

                            var useFarLod = !adt.IsSelected &&
                                TerrainLodTransitionPixels > 0f &&
                                ScreenSpaceCulling.IsBelowPixelThresholdNormalized(
                                    camera.Position,
                                    normalizedCameraForward,
                                    boundsSphere.Center,
                                    boundsSphere.Radius,
                                    verticalProjectionScale,
                                    _renderHeight,
                                    TerrainLodTransitionPixels);

                            visibleChunks++;
                            _visibleIndices.Add(c);
                            _visibleTerrainFarLod.Add(useFarLod);
                            if (useFarLod)
                                farLodTerrainChunks++;
                        }
                    }
                    var cullingElapsed = Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;
                    TerrainCullingTimeMs += cullingElapsed;
                    CullingTimeMs += cullingElapsed;

                    if (_visibleIndices.Count == 0)
                        continue;

                    var vertexBuffer = adt.Terrain.vertexBuffer;
                    var cb = new ADTPerObjectCB
                    {
                        model_matrix = adt.GetModelMatrix(),
                        projection_matrix = projectionMatrix,
                        rotation_matrix = cameraMatrix,
                        firstPos = adt.Terrain.startPos,
                        _pad0 = 0f
                    };

                    deviceContext.UpdateSubresource(adtPerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref cb, 0, 0);
                    ConstantBufferUpdates++;
                    deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in adtVertexStride, in adtVertexOffset);
                    VertexBufferBindings++;
                    var alphaMaterialArray = adt.Terrain.alphaMaterialArray;
                    deviceContext.PSSetShaderResources(16, 1, ref alphaMaterialArray);
                    var alphaSliceBuffer = adt.Terrain.alphaSliceBuffer;
                    deviceContext.PSSetConstantBuffers(2, 1, ref alphaSliceBuffer);
                    var chunkLayerDataBuffer = adt.Terrain.chunkLayerDataBuffer;
                    deviceContext.PSSetConstantBuffers(3, 1, ref chunkLayerDataBuffer);
                    TextureBindingCalls++;

                    bool? currentFarLod = null;
                    for (var visibleIndex = 0; visibleIndex < _visibleIndices.Count; visibleIndex++)
                    {
                        var c = _visibleIndices[visibleIndex];
                        var useFarLod = _visibleTerrainFarLod[visibleIndex];
                        var compatibleChunkCount = TerrainBatching.CountCompatibleContiguousChunks(
                            visibleIndex,
                            _visibleIndices,
                            _visibleTerrainFarLod,
                            adt.Terrain.compatibleRenderRunLengths);
                        if (currentFarLod != useFarLod)
                        {
                            var indexBuffer = useFarLod
                                ? adt.Terrain.farLodIndiceBuffer
                                : adt.Terrain.indiceBuffer;
                            deviceContext.IASetIndexBuffer(indexBuffer, Format.FormatR32Uint, 0);
                            IndexBufferBindings++;
                            currentFarLod = useFarLod;
                        }

                        var batch = adt.Terrain.renderBatches[c];
                        var shaderLayerCount = TerrainBatching.GetShaderLayerCount(batch.layerCount);
                        if (currentAdtShaderLayerCount != shaderLayerCount ||
                            currentAdtUsesHeightTextures != batch.usesHeightTextures)
                        {
                            var shaderIndex = shaderLayerCount switch
                            {
                                1 => 0,
                                2 => 1,
                                4 => 2,
                                _ => 3
                            };
                            if (batch.usesHeightTextures)
                                shaderIndex += 4;
                            var shader = adtLayerShaderPrograms[shaderIndex];
                            deviceContext.PSSetShader(shader.PixelShader, ref nullClassInstance, 0);
                            currentAdtShaderLayerCount = shaderLayerCount;
                            currentAdtUsesHeightTextures = batch.usesHeightTextures;
                        }

                        for (int s = 0; s < shaderLayerCount; s++)
                            _srvScratch[s] = s < batch.materialFDIDs.Length
                                ? ResolveFrameTexture((uint)batch.materialFDIDs[s])
                                : defaultTexture;
                        if (batch.materialFDIDs.Length > 0)
                        {
                            deviceContext.PSSetShaderResources(0, (uint)shaderLayerCount, ref _srvScratch[0]);
                            TextureBindingCalls++;
                        }

                        if (batch.usesHeightTextures)
                        {
                            for (int s = 0; s < shaderLayerCount; s++)
                                _srvScratch[s] = s < batch.heightMaterialFDIDs.Length
                                    ? ResolveFrameTexture((uint)batch.heightMaterialFDIDs[s])
                                    : defaultTexture;
                            deviceContext.PSSetShaderResources(8, (uint)shaderLayerCount, ref _srvScratch[0]);
                            TextureBindingCalls++;
                        }

                        var indexCount = useFarLod ? 384u : 768u;
                        deviceContext.DrawIndexed(
                            indexCount * (uint)compatibleChunkCount,
                            (uint)c * indexCount,
                            0);
                        drawCalls++;
                        TerrainDrawCalls++;
                        TerrainSubmittedChunks += (uint)compatibleChunkCount;
                        var submittedIndices = (ulong)indexCount * (uint)compatibleChunkCount;
                        submittedIndexCount += submittedIndices;
                        TerrainSubmittedIndices += submittedIndices;
                        visibleIndex += compatibleChunkCount - 1;
                    }
                }
            }
            gpuTimer?.EndTerrain();
            TerrainSubmissionTimeMs = RenderADT
                ? Math.Max(0, Stopwatch.GetElapsedTime(passStarted).TotalMilliseconds - TerrainCullingTimeMs)
                : 0;

            // Bounding box rendering
            passStarted = Stopwatch.GetTimestamp();
            gpuTimer?.BeginDebug();
            var drawCallsBeforeDebug = drawCalls;
            if (ShowBoundingBoxes || ShowBoundingSpheres || (SelectionVisualsEnabled && SelectedObject != null) || TerrainBrushWorldPosition.HasValue)
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
                        if (!SelectionVisualsEnabled && sceneObject.IsSelected) continue;
                        if (!ShowBoundingBoxes && !ShowBoundingSpheres &&
                            !(SelectionVisualsEnabled && sceneObject.IsSelected)) continue;

                        var color = SelectionVisualsEnabled && sceneObject.IsSelected
                            ? new Vector4(0, 1, 0, 1)
                            : new Vector4(1, 1, 0, 1);

                        if (ShowBoundingBoxes || (SelectionVisualsEnabled && sceneObject.IsSelected))
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

                                (var boxDrawCalls, _) = DrawBoundingBox(localBox, modelMatrix, color, projectionMatrix, cameraMatrix);
                                drawCalls += boxDrawCalls;
                            }
                        }

                        if (ShowBoundingSpheres || (SelectionVisualsEnabled && sceneObject.IsSelected))
                        {
                            var sphere = sceneObject.GetBoundingSphere();
                            if (sphere.HasValue)
                            {
                                (var sphereDrawCalls, _) = DrawBoundingSphere(sphere.Value, new Vector4(0, 0.5f, 1, 1), projectionMatrix, cameraMatrix);
                                drawCalls += sphereDrawCalls;
                            }
                        }
                    }
                }

                if (TerrainBrushWorldPosition.HasValue &&
                    TerrainBrushRadius > 0f &&
                    _terrainBrushOuterPoints.Length > 0)
                {
                    var outerBrush = DrawTerrainBrushCircle(
                        _terrainBrushOuterPoints,
                        TerrainBrushColor,
                        projectionMatrix,
                        cameraMatrix,
                        ref _terrainBrushOuterVertexBuffer);
                    var innerBrush = _terrainBrushInnerPoints.Length > 0
                        ? DrawTerrainBrushCircle(
                            _terrainBrushInnerPoints,
                            new Vector4(TerrainBrushColor.X, TerrainBrushColor.Y, TerrainBrushColor.Z, 0.8f),
                            projectionMatrix,
                            cameraMatrix,
                            ref _terrainBrushInnerVertexBuffer)
                        : (0u, 0u);
                    drawCalls += outerBrush.drawCalls + innerBrush.Item1;
                }

                deviceContext.RSSetState(rasterizerState);
                deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
                ComPtr<ID3D11DepthStencilState> nullDSS = default;
                deviceContext.OMSetDepthStencilState(nullDSS, 0);
            }
            gpuTimer?.EndDebug();
            DebugSubmissionTimeMs = Stopwatch.GetElapsedTime(passStarted).TotalMilliseconds;
            DebugDrawCalls = drawCalls - drawCallsBeforeDebug;

            //swapchain.Present(1, 0);

            gizmoWasUsing = false;
            gizmoWasOver = false;

            return (drawCalls, submittedIndexCount);
        }

        private static bool IsWithinRenderDistance(Vector3 cameraPosition, Vector3 center, float radius, float distance)
        {
            var maxDistance = Math.Max(0f, distance) + Math.Max(0f, radius);
            return Vector3.DistanceSquared(cameraPosition, center) <= maxDistance * maxDistance;
        }

        private static bool IsFullyWithinRenderDistance(
            Vector3 cameraPosition,
            Vector3 center,
            float radius,
            float distance)
        {
            var innerDistance = Math.Max(0f, distance) - Math.Max(0f, radius);
            return innerDistance >= 0f &&
                Vector3.DistanceSquared(cameraPosition, center) <= innerDistance * innerDistance;
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

        private (Vector3[] Outer, Vector3[] Inner) ProjectTerrainBrush(
            Vector3 center,
            float outerRadius,
            float innerRadius)
        {
            lock (SceneObjectLock)
            {
                var candidates = BuildTerrainRaycastCandidatesLocked(center, outerRadius);
                var outer = ProjectTerrainCircleLocked(center, outerRadius, candidates);
                var inner = innerRadius > 0.01f
                    ? ProjectTerrainCircleLocked(center, innerRadius, candidates)
                    : [];
                return (outer, inner);
            }
        }

        private static Vector3[] ProjectTerrainCircleLocked(
            Vector3 center,
            float radius,
            IReadOnlyList<TerrainRaycastChunk> candidates)
        {
            const int segments = 64;
            var points = new Vector3[segments];
            for (var index = 0; index < segments; index++)
            {
                var angle = MathF.Tau * index / segments;
                var position = new Vector3(
                    center.X + MathF.Cos(angle) * radius,
                    center.Y + MathF.Sin(angle) * radius,
                    center.Z + 10_000f);
                var ray = new Ray(position, -Vector3.UnitZ);
                points[index] = TryRaycastTerrainCandidates(
                        ray,
                        candidates,
                        out var hit)
                    ? hit.WorldPosition + hit.WorldNormal * 0.08f
                    : new Vector3(position.X, position.Y, center.Z + 0.08f);
            }

            return points;
        }

        private static bool TryRaycastTerrainCandidates(
            Ray ray,
            IReadOnlyList<TerrainRaycastChunk> candidates,
            out TerrainRayHit closestHit)
        {
            closestHit = default;
            var closestDistance = float.MaxValue;
            foreach (var candidate in candidates)
            {
                TryRaycastTerrainChunk(
                    candidate,
                    ray,
                    candidate.ChunkIndex,
                    ref closestDistance,
                    ref closestHit);
            }

            return closestDistance < float.MaxValue;
        }

        private unsafe (uint drawCalls, uint verticeCount) DrawTerrainBrushCircle(
            Vector3[] points,
            Vector4 color,
            Matrix4x4 projection,
            Matrix4x4 view,
            ref ComPtr<ID3D11Buffer> vertexBuffer)
        {
            Span<Vector3> vertices = stackalloc Vector3[points.Length * 2];
            for (var index = 0; index < points.Length; index++)
            {
                vertices[index * 2] = points[index];
                vertices[index * 2 + 1] = points[(index + 1) % points.Length];
            }

            if (vertexBuffer.Handle == null)
            {
                var bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)(vertices.Length * sizeof(Vector3)),
                    Usage = Usage.Dynamic,
                    BindFlags = (uint)BindFlag.VertexBuffer,
                    CPUAccessFlags = (uint)CpuAccessFlag.Write
                };
                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, null, ref vertexBuffer));
            }

            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(deviceContext.Map(vertexBuffer, 0, Map.WriteDiscard, 0, ref mapped));
            vertices.CopyTo(new Span<Vector3>(mapped.PData, vertices.Length));
            deviceContext.Unmap(vertexBuffer, 0);

            var constantBuffer = new BBoxCB
            {
                projection_matrix = projection,
                view_matrix = view,
                model_matrix = Matrix4x4.Identity,
                color = color
            };
            MappedSubresource mappedCB = default;
            SilkMarshal.ThrowHResult(deviceContext.Map(bboxConstantBuffer, 0, Map.WriteDiscard, 0, ref mappedCB));
            *(BBoxCB*)mappedCB.PData = constantBuffer;
            deviceContext.Unmap(bboxConstantBuffer, 0);

            uint stride = (uint)sizeof(Vector3);
            uint offset = 0;
            deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in stride, in offset);
            deviceContext.VSSetConstantBuffers(0, 1, ref bboxConstantBuffer);
            deviceContext.PSSetConstantBuffers(0, 1, ref bboxConstantBuffer);
            deviceContext.Draw((uint)vertices.Length, 0);
            return (1, (uint)vertices.Length);
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
                foreach (var bounds in tileSceneBounds.Values)
                    bounds.Dispose();
                tileSceneBounds.Clear();
                tileSceneBoundsByRoot.Clear();

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
                _terrainBrushOuterVertexBuffer.Dispose();
                _terrainBrushInnerVertexBuffer.Dispose();
                rasterizerState.Dispose();
                wmoRasterizerState.Dispose();
                wireframeRasterizerState.Dispose();
                foreach (var bs in _blendStates)
                    bs.Dispose();
            }
        }
    }
}
