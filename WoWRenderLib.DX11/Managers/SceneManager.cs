using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MapObjDefFlags = WoWLib.Formats.Common.MapObjDefFlags;
using M2MaterialFlags = WoWLib.Formats.M2.Root.Record.MaterialFlags;
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
    public partial class SceneManager : IDisposable
    {
        private readonly ComPtr<ID3D11Device> _device;
        private readonly ComPtr<ID3D11DeviceContext> _deviceContext;
        private readonly ShaderManager _shaderManager;
        public List<Container3D> SceneObjects { get; } = [];
        public Lock SceneObjectLock { get; } = new();

        private Queue<MapTile> tilesToLoad = new();
        internal static readonly TimeSpan TileUnloadDelay = TimeSpan.FromMilliseconds(750);
        private readonly TimeProvider streamingClock = TimeProvider.System;
        private readonly HashSet<MapTile> tilesQueuedForLoad = [];
        private readonly HashSet<MapTile> tilesInFlight = [];
        private readonly HashSet<MapTile> desiredTiles = [];
        private readonly HashSet<MapTile> failedDesiredTiles = [];

        private readonly Dictionary<uint, uint> uuidUsers = [];
        private readonly HashSet<MapTile> loadedTiles = [];
        private readonly List<ADTContainer> adtContainers = [];
        private readonly HashSet<(byte X, byte Y)> availableWdtTiles = [];
        // The components of MapTile fit exactly in 48 bits. The packed key
        // avoids repeatedly hashing/comparing the struct for bounds lookups;
        // the full MapTile remains stored by TileSceneBounds for diagnostics.
        private readonly Dictionary<ulong, TileSceneBounds> tileSceneBounds = [];
        private readonly Dictionary<uint, TileSceneBounds> tileSceneBoundsByRoot = [];
        private readonly HashSet<uint> coarseCulledTileRoots = [];
        public event Action<MapTile, float>? TerrainTileHeightAvailable;
        public event Action<string, Exception>? SceneLoadFailed;

        private static ulong GetTileBoundsKey(MapTile tile) =>
            ((ulong)tile.wdtFileDataID << 16) |
            ((ulong)tile.tileX << 8) |
            tile.tileY;

        private WdtFile? currentWDT;
        public uint CurrentWDTFileDataID { get; private set; } = 775971;
        public uint CurrentMapHighestUniqueId { get; private set; }
        public Container3D? SelectedObject { get; set; } = null;
        public bool SelectionVisualsEnabled { get; set; } = true;
        public bool ShowBoundingBoxes { get; set; } = false;
        public bool ShowBoundingSpheres { get; set; } = false;
        public bool ShowTerrainGrid { get; set; }
        public bool ShowTerrainWireframe { get; set; }

        public bool RenderADT { get; set; } = true;
        public bool RenderLiquid { get; set; } = true;
        public bool RenderWMO { get; set; } = true;
        public bool RenderM2 { get; set; } = true;
        public bool EnableWmoPortalCulling { get; set; }
        public int TileLoadingDistance { get; set; } = 4;
        public float TerrainRenderDistance { get; set; } = 20_000f;
        public float ModelRenderDistance { get; set; } = 20_000f;
        public float MinimumModelScreenSizePixels { get; set; } = 1f;
        public float TerrainLodTransitionPixels { get; set; } = 32f;
        public Vector3? BrushWorldPosition { get; private set; }
        public float BrushRadius { get; private set; }
        public float BrushFalloff { get; private set; }
        public bool BrushHasFalloff { get; private set; }
        public BrushShape BrushShape { get; private set; }
        public BrushFalloffProfile BrushFalloffProfile { get; private set; }
        public Vector4 BrushColor { get; private set; } = new(0.2f, 0.85f, 1f, 1f);
        private Dictionary<TerrainTileId, ADTVertex[]>? _activeTerrainStrokeBefore;
        private float? _flattenStrokeHeight;

        // World-space light from north-west at a 45° elevation. WoW's world
        // axes map north/west to the negative X/Y directions in this renderer.
        public Vector3 LightDirection { get; set; } = new(-0.5f, -0.5f, 0.70710678f);
        public Vector3 AmbientColor { get; set; } = new(104f / 255f, 130f / 255f, 154f / 255f);
        public Vector3 DiffuseColor { get; set; } = new(1f, 136f / 255f, 0f);
        public WorldLightingData? ClientWorldLighting { get; private set; }
        private WorldLightingSettings _activeWorldLighting = WorldLightingSettings.Defaults;
        private IReadOnlyList<WorldLightingContribution> _activeWorldLightingContributions =
            Array.Empty<WorldLightingContribution>();
        private WorldSkyLighting _activeWorldSky = WorldSkyLighting.None;

        public WorldLightingSettings ActiveWorldLighting => _activeWorldLighting with
        {
            LightDirection = LightDirection,
            AmbientColor = AmbientColor,
            DiffuseColor = DiffuseColor
        };
        public IReadOnlyList<WorldLightingContribution> ActiveWorldLightingContributions =>
            _activeWorldLightingContributions;
        public WorldSkyLighting ActiveWorldSky => _activeWorldSky;

        private const int MaxInstancesPerBatch = 1024;
        private const int MaxTerrainChunksPerTile = 256;
        private const int TerrainVerticesPerChunk = 145;
        private const uint TerrainIndicesPerChunk = 768;
        private const float TerrainChunkGridHalfWidthInCell = 0.004f;
        private const float TerrainAdtGridHalfWidthInCell = 0.0065f;
        private const uint TerrainFarLodIndicesPerChunk = 384;
        private const int MaxTerrainLayers = 8;
        private const int TerrainHeightTextureSlot = 8;
        private const int TerrainAlphaTextureSlot = 16;
        private const int ShaderResourceSlotCount = 16;

        private CompiledShader adtShaderProgram;
        private readonly CompiledShader[] adtLayerShaderPrograms = new CompiledShader[MaxTerrainLayers];
        private CompiledShader wmoShaderProgram;
        private CompiledShader m2ShaderProgram;
        private readonly WorldLiquidRenderer _worldLiquidRenderer;
        private readonly List<WmoLiquidInstance> _visibleWmoLiquids = [];
        private readonly SkyRenderer _skyRenderer;
        private readonly DebugBoundsRenderer _debugBoundsRenderer;
        private readonly M2DepthStateController _m2DepthStates;

        public SceneManager(
            ComPtr<ID3D11Device> device,
            ComPtr<ID3D11DeviceContext> deviceContext,
            ShaderManager shaderManager)
        {
            _device = device;
            _deviceContext = deviceContext;
            _shaderManager = shaderManager ?? throw new ArgumentNullException(nameof(shaderManager));
            _worldLiquidRenderer = new WorldLiquidRenderer(device, deviceContext);
            _skyRenderer = new SkyRenderer(device, deviceContext);
            _debugBoundsRenderer = new DebugBoundsRenderer(device, deviceContext);
            _m2DepthStates = new M2DepthStateController(device, deviceContext);
        }

        private sealed class PendingAdtPopulation(
            ADTContainer container,
            Terrain terrain,
            TileSceneBounds bounds)
        {
            public ADTContainer Container { get; } = container;
            public Terrain Terrain { get; } = terrain;
            public TileSceneBounds Bounds { get; } = bounds;
            public int NextWorldModel { get; set; }
            public int NextDoodad { get; set; }
        }

        private sealed class PendingWmoDoodadPopulation(WMOContainer container)
        {
            public WMOContainer Container { get; } = container;
            public int NextDoodad { get; set; }
            public bool Initialized { get; set; }
            public bool RegisteredLoadedGroups { get; set; }
        }

        private sealed class PendingTileUnload(
            ADTContainer container,
            List<WMOContainer> worldModels,
            List<M2Container> doodads)
        {
            public ADTContainer Container { get; } = container;
            public List<WMOContainer> WorldModels { get; } = worldModels;
            public List<M2Container> Doodads { get; } = doodads;
            public int NextWorldModel { get; set; }
            public int NextDoodad { get; set; }
        }

        private readonly Queue<PendingAdtPopulation> pendingAdtPopulations = [];
        private readonly Queue<PendingWmoDoodadPopulation> pendingWMODoodads = [];
        private readonly Queue<PendingTileUnload> pendingTileUnloads = [];
        public readonly Dictionary<(uint FileDataID, string EnabledGroupSignature), List<WMOContainer>> wmoInstances = [];
        public readonly Dictionary<uint, List<M2Container>> m2Instances = [];
        private readonly Dictionary<uint, M2InstancePacket> m2InstancePackets = [];
        private int nextUploadQueue;
        private int nextPopulationQueue;
        private int nextStreamingPhase;

        private ComPtr<ID3D11Buffer> adtPerObjectConstantBuffer = default;
        private ComPtr<ID3D11Buffer> layerDataConstantBuffer = default;
        private ComPtr<ID3D11Buffer> wmoPerObjectConstantBuffer = default;
        private ComPtr<ID3D11Buffer> m2PerObjectConstantBuffer = default;
        private ComPtr<ID3D11Buffer> m2BonePaletteConstantBuffer = default;
        private readonly Matrix4x4[] m2BonePalette = new Matrix4x4[M2Animation.MaxGpuBones];
        private readonly long m2AnimationEpoch = Stopwatch.GetTimestamp();
        private ComPtr<ID3D11Buffer> instanceMatrixBuffer = default;
        private ComPtr<ID3D11DepthStencilView> depthStencilView = default;
        private ComPtr<ID3D11Texture2D> depthTexture = default;
        private ComPtr<ID3D11SamplerState> textureSampler = default;
        private ComPtr<ID3D11SamplerState> clampSampler = default;
        private readonly ComPtr<ID3D11SamplerState>[] m2TextureSamplers =
            new ComPtr<ID3D11SamplerState>[4];
        private ComPtr<ID3D11RenderTargetView> renderTargetView = default;
        private ComPtr<ID3D11RasterizerState> rasterizerState = default;
        private ComPtr<ID3D11RasterizerState> wmoRasterizerState = default;
        private ComPtr<ID3D11RasterizerState> m2TwoSidedRasterizerState = default;
        private ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        private ComPtr<ID3D11ShaderResourceView> missingTexture;
        private ComPtr<ID3D11ShaderResourceView> emptyTerrainTexture;
        private readonly ComPtr<ID3D11BlendState>[] _blendStates = new ComPtr<ID3D11BlendState>[14];

        private readonly ComPtr<ID3D11ShaderResourceView>[] _srvScratch =
            new ComPtr<ID3D11ShaderResourceView>[ShaderResourceSlotCount];
        private readonly ComPtr<ID3D11SamplerState>[] _samplerScratch =
            new ComPtr<ID3D11SamplerState>[4];
        private readonly Dictionary<uint, ComPtr<ID3D11ShaderResourceView>> _frameTextureSrvs = [];
        private readonly List<int> _visibleIndices = new(64);
        private readonly List<bool> _visibleTerrainFarLod = new(MaxTerrainChunksPerTile);
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
        public double LiquidCullingTimeMs { get; private set; }
        public double LiquidSubmissionTimeMs { get; private set; }
        public double SkySubmissionTimeMs { get; private set; }
        public double TileHierarchyCullingTimeMs { get; private set; }
        public double DebugSubmissionTimeMs { get; private set; }
        public uint WmoDrawCalls { get; private set; }
        public uint M2DrawCalls { get; private set; }
        public uint TerrainDrawCalls { get; private set; }
        public uint LiquidDrawCalls { get; private set; }
        public uint SkyDrawCalls { get; private set; }
        public uint DebugDrawCalls { get; private set; }
        public uint WmoSubmittedInstances { get; private set; }
        public uint M2SubmittedInstances { get; private set; }
        public uint TerrainSubmittedChunks { get; private set; }
        public ulong WmoSubmittedIndices { get; private set; }
        public ulong M2SubmittedIndices { get; private set; }
        public ulong TerrainSubmittedIndices { get; private set; }
        public ulong LiquidSubmittedIndices { get; private set; }
        public ulong SkySubmittedIndices { get; private set; }
        public int candidateLiquidBatches { get; private set; }
        public int visibleLiquidBatches { get; private set; }
        public uint InstanceBufferMapCalls { get; private set; }
        public uint ConstantBufferUpdates { get; private set; }
        public uint TextureBindingCalls { get; private set; }
        public uint BlendStateBindings { get; private set; }
        public uint VertexBufferBindings { get; private set; }
        public uint IndexBufferBindings { get; private set; }

        public bool SceneLoaded => loadedTiles.Count > 0; // this won't work for WMO only maps
        public string StatusMessage { get; private set; } = "";

        /// <summary>
        /// Applies the temporary fixed LightData profile to the shared world
        /// lighting inputs used by terrain, models, and MH2O liquids. The
        /// settings/default values remain the fallback when the optional
        /// client database row cannot be loaded.
        /// </summary>
        public void ApplyClientWorldLighting(WorldLightingData lighting)
        {
            ArgumentNullException.ThrowIfNull(lighting);
            ClientWorldLighting = lighting;

            if (lighting.TryGetNumeric("ambient_color", out _))
                AmbientColor = lighting.AmbientColor;
            if (lighting.TryGetNumeric("direct_color", out _))
                DiffuseColor = lighting.DirectColor;

            _activeWorldLighting = new WorldLightingSettings(
                lighting.LightParamId,
                lighting.Time,
                LightDirection,
                AmbientColor,
                DiffuseColor,
                lighting.OceanCloseColor,
                lighting.OceanFarColor,
                lighting.RiverCloseColor,
                lighting.RiverFarColor,
                lighting.WaterShallowAlpha,
                lighting.WaterDeepAlpha,
                lighting.OceanShallowAlpha,
                lighting.OceanDeepAlpha,
                lighting.HasLiquidColorData,
                lighting.HasLiquidAlphaData,
                false);
            _activeWorldLightingContributions = Array.Empty<WorldLightingContribution>();
        }

        /// <summary>Applies a user-edited lighting snapshot immediately.</summary>
        public void ApplyWorldLighting(
            WorldLightingSettings lighting,
            IReadOnlyList<WorldLightingContribution>? activeLightContributions = null)
        {
            _activeWorldLighting = lighting.NormalizeForRendering();
            _activeWorldLightingContributions = activeLightContributions is { Count: > 0 }
                ? activeLightContributions.ToArray()
                : Array.Empty<WorldLightingContribution>();
            LightDirection = _activeWorldLighting.LightDirection;
            AmbientColor = _activeWorldLighting.AmbientColor;
            DiffuseColor = _activeWorldLighting.DiffuseColor;
        }

        public void ApplyWorldSky(WorldSkyLighting sky)
        {
            _activeWorldSky = sky;
            _skyRenderer.SetLighting(sky);
        }

        private static Vector3 ClampLightingColor(Vector3 color) => new(
            Math.Clamp(color.X, 0f, 4f),
            Math.Clamp(color.Y, 0f, 4f),
            Math.Clamp(color.Z, 0f, 4f));

        public void Initialize(ShaderManager shaderManager, CompiledShader adtShader, CompiledShader wmoShader, CompiledShader m2Shader, CompiledShader bboxShader)
        {
            adtShaderProgram = adtShader;
            LoadAdtLayerShaders();
            wmoShaderProgram = wmoShader;
            m2ShaderProgram = m2Shader;
            missingTexture = BLPLoader.CreatePlaceholderTexture(_device);
            emptyTerrainTexture = BLPLoader.CreateWhiteTexture(_device);

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

                SilkMarshal.ThrowHResult(_device.CreateSamplerState(in samplerDesc, ref textureSampler));

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

                SilkMarshal.ThrowHResult(_device.CreateSamplerState(in clampSamplerDesc, ref clampSampler));

                // M2Texture flags select wrapping independently on U and V.
                // Keep all four combinations available; using the terrain's
                // wrap/wrap sampler for clamp-addressed foliage repeats its
                // atlas outside the intended leaf card UV range.
                for (var wrapX = 0; wrapX <= 1; wrapX++)
                {
                    for (var wrapY = 0; wrapY <= 1; wrapY++)
                    {
                        var m2SamplerDesc = samplerDesc;
                        m2SamplerDesc.AddressU = wrapX != 0
                            ? TextureAddressMode.Wrap
                            : TextureAddressMode.Clamp;
                        m2SamplerDesc.AddressV = wrapY != 0
                            ? TextureAddressMode.Wrap
                            : TextureAddressMode.Clamp;
                        m2SamplerDesc.AddressW = TextureAddressMode.Clamp;
                        var samplerIndex = (wrapX << 1) | wrapY;
                        SilkMarshal.ThrowHResult(_device.CreateSamplerState(
                            in m2SamplerDesc,
                            ref m2TextureSamplers[samplerIndex]));
                    }
                }

                // PER OBJECT CONSTANT BUFFERS
                var bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)Marshal.SizeOf<ADTPerObjectCB>(),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };

                SilkMarshal.ThrowHResult(_device.CreateBuffer(in bufferDesc, null, ref adtPerObjectConstantBuffer));

                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)sizeof(WMOPerObjectCB),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };

                SilkMarshal.ThrowHResult(_device.CreateBuffer(in bufferDesc, null, ref wmoPerObjectConstantBuffer));

                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)sizeof(M2PerObjectCB),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };

                SilkMarshal.ThrowHResult(_device.CreateBuffer(in bufferDesc, null, ref m2PerObjectConstantBuffer));

                bufferDesc.ByteWidth = (uint)(M2Animation.MaxGpuBones * sizeof(Matrix4x4));
                SilkMarshal.ThrowHResult(_device.CreateBuffer(in bufferDesc, null, ref m2BonePaletteConstantBuffer));

                // Instance buffer
                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)(MaxInstancesPerBatch * sizeof(Matrix4x4)),
                    Usage = Usage.Dynamic,
                    BindFlags = (uint)BindFlag.VertexBuffer,
                    CPUAccessFlags = (uint)CpuAccessFlag.Write
                };

                SilkMarshal.ThrowHResult(_device.CreateBuffer(in bufferDesc, null, ref instanceMatrixBuffer));

                // ADT layer data
                bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)Marshal.SizeOf<LayerData>(),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ConstantBuffer
                };

                SilkMarshal.ThrowHResult(_device.CreateBuffer(in bufferDesc, null, ref layerDataConstantBuffer));

                // Rasterizers, need to be merged once ADTs are fixed
                var rastDesc = new RasterizerDesc
                {
                    FillMode = FillMode.Solid,
                    CullMode = CullMode.Back, // TODO: Fix, then merge rasterizers
                    FrontCounterClockwise = false,
                    DepthClipEnable = true
                };

                SilkMarshal.ThrowHResult(_device.CreateRasterizerState(in rastDesc, ref rasterizerState));
                _deviceContext.RSSetState(rasterizerState);

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
                SilkMarshal.ThrowHResult(_device.CreateRasterizerState(in wmoRastDesc, ref wmoRasterizerState));

                var m2TwoSidedRastDesc = wmoRastDesc;
                m2TwoSidedRastDesc.CullMode = CullMode.None;
                SilkMarshal.ThrowHResult(_device.CreateRasterizerState(
                    in m2TwoSidedRastDesc,
                    ref m2TwoSidedRasterizerState));

                ComPtr<ID3D11RasterizerState> rastState = default;
                _device.CreateRasterizerState(in rastDesc, ref rastState);
                _deviceContext.RSSetState(rastState);

                CreateBlendStates();
            }

            _worldLiquidRenderer.Initialize(shaderManager);
            _skyRenderer.Initialize(shaderManager, m2Shader);
            _debugBoundsRenderer.Initialize(bboxShader);
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
                SilkMarshal.ThrowHResult(_device.CreateBlendState(in blendDesc, ref _blendStates[i]));
            }
        }

        private unsafe float ApplyBlendMode(int blendType, ref int currentBlendType)
        {
            if ((uint)blendType >= (uint)_blendStates.Length)
                blendType = 0;

            if (currentBlendType != blendType)
            {
                float blendFactor = 1f;
                _deviceContext.OMSetBlendState(_blendStates[blendType], ref blendFactor, 0xFFFFFFFF);
                currentBlendType = blendType;
                BlendStateBindings++;
            }

            return GetAlphaReference(blendType);
        }

        // Alpha-key materials use the client midpoint (128/255). The former
        // 0.904 threshold discarded nearly every texel in foliage textures.
        internal static float GetAlphaReference(int blendType) =>
            blendType == 1 ? 128f / 255f : -1.0f;

        internal static bool IsM2TwoSided(ushort renderFlags) =>
            (renderFlags & (ushort)M2MaterialFlags.TwoSided) != 0;

        internal static int GetM2SamplerIndex(uint textureFlags) =>
            ((textureFlags & 0x1) != 0 ? 2 : 0) |
            ((textureFlags & 0x2) != 0 ? 1 : 0);

        private ComPtr<ID3D11ShaderResourceView> ResolveFrameTexture(uint fileDataId)
        {
            if (fileDataId == 0)
                return missingTexture;

            if (_frameTextureSrvs.TryGetValue(fileDataId, out var texture))
                return texture;

            texture = BLPCache.GetCurrent(fileDataId, missingTexture);
            _frameTextureSrvs.Add(fileDataId, texture);
            return texture;
        }

        private ComPtr<ID3D11ShaderResourceView> ResolveTerrainDiffuseTexture(int fileDataId) =>
            fileDataId > 0
                ? ResolveFrameTexture(checked((uint)fileDataId))
                : emptyTerrainTexture;

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
                    _shaderManager.GetOrCompileAdtShader(layerCounts[index], false);
                adtLayerShaderPrograms[index + 4] =
                    _shaderManager.GetOrCompileAdtShader(layerCounts[index], true);
            }
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
            SilkMarshal.ThrowHResult(_device.CreateTexture2D(in depthDesc, null, ref depthTexture));
            SilkMarshal.ThrowHResult(_device.CreateDepthStencilView(depthTexture, null, ref depthStencilView));

            var viewport = new Viewport
            {
                TopLeftX = 0,
                TopLeftY = 0,
                Width = width,
                Height = height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };
            _deviceContext.RSSetViewports(1, in viewport);

            _renderWidth = width;
            _renderHeight = height;
        }

        public unsafe void Resize(uint width, uint height, ComPtr<ID3D11RenderTargetView> rtv)
        {
            if (width == 0 || height == 0)
                return;

            _deviceContext.OMSetRenderTargets(0, (ID3D11RenderTargetView**)null, (ID3D11DepthStencilView*)null);
            _deviceContext.ClearState();

            renderTargetView = default; // don't dispose, we dont own it!
            if (depthStencilView.Handle != null) { depthStencilView.Dispose(); depthStencilView = default; }
            if (depthTexture.Handle != null) { depthTexture.Dispose(); depthTexture = default; }

            CreateSizeDependentResources(width, height, rtv);
        }

        /// <summary>
        /// Changes only the render target used by the next frame. The depth buffer and
        /// viewport remain size-dependent resources and are intentionally left intact.
        /// The caller owns <paramref name="rtv"/> and must keep it alive while rendering.
        /// </summary>
        public void SetRenderTarget(ComPtr<ID3D11RenderTargetView> rtv)
        {
            renderTargetView = rtv;
        }

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
            LiquidCullingTimeMs = 0;
            LiquidSubmissionTimeMs = 0;
            SkySubmissionTimeMs = 0;
            TileHierarchyCullingTimeMs = 0;
            DebugSubmissionTimeMs = 0;
            WmoDrawCalls = 0;
            M2DrawCalls = 0;
            TerrainDrawCalls = 0;
            LiquidDrawCalls = 0;
            SkyDrawCalls = 0;
            DebugDrawCalls = 0;
            WmoSubmittedInstances = 0;
            M2SubmittedInstances = 0;
            TerrainSubmittedChunks = 0;
            WmoSubmittedIndices = 0;
            M2SubmittedIndices = 0;
            TerrainSubmittedIndices = 0;
            LiquidSubmittedIndices = 0;
            SkySubmittedIndices = 0;
            InstanceBufferMapCalls = 0;
            ConstantBufferUpdates = 0;
            TextureBindingCalls = 0;
            BlendStateBindings = 0;
            VertexBufferBindings = 0;
            IndexBufferBindings = 0;
            _frameTextureSrvs.Clear();
            var currentBlendType = -1;

            _deviceContext.RSSetState(rasterizerState);

#if DEBUG
            if (_shaderManager.CheckForChanges())
            {
                adtShaderProgram = _shaderManager.GetOrCompileShader("adt");
                LoadAdtLayerShaders();
                wmoShaderProgram = _shaderManager.GetOrCompileShader("wmo");
                m2ShaderProgram = _shaderManager.GetOrCompileShader("m2");
                _worldLiquidRenderer.RefreshShader();
                _skyRenderer.RefreshShaders();
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
            candidateLiquidBatches = 0;
            visibleLiquidBatches = 0;
            _visibleWmoLiquids.Clear();
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
            _deviceContext.PSSetShaderResources(0, 1, ref nullSRV);

            _deviceContext.ClearRenderTargetView(renderTargetView, ref backgroundColour[0]);
            _deviceContext.OMSetRenderTargets(1, ref renderTargetView, depthStencilView);
            _deviceContext.ClearDepthStencilView(depthStencilView, (uint)ClearFlag.Depth, 1.0f, 0);

            _deviceContext.PSSetSamplers(0, 1, ref textureSampler);
            _deviceContext.PSSetSamplers(1, 1, ref clampSampler);

            _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);

            var skyStats = _skyRenderer.Render(camera);
            SkyDrawCalls = skyStats.DrawCalls;
            SkySubmittedIndices = skyStats.SubmittedIndices;
            SkySubmissionTimeMs = skyStats.SubmissionMilliseconds;
            drawCalls += skyStats.DrawCalls;
            submittedIndexCount += skyStats.SubmittedIndices;

            var adtVertexStride = (uint)Marshal.SizeOf<ADTGpuVertex>();
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

            ApplyBlendMode(0, ref currentBlendType);

            var passStarted = Stopwatch.GetTimestamp();
            gpuTimer?.BeginTerrain();
            if (RenderADT)
            {
                _deviceContext.RSSetState(rasterizerState);
                // Terrain uses wrapped diffuse textures and a clamped alpha map.
                // Bind both explicitly after the sky pass and before any models.
                _deviceContext.PSSetSamplers(0, 1, ref textureSampler);
                _deviceContext.PSSetSamplers(1, 1, ref clampSampler);
                _deviceContext.IASetInputLayout(adtShaderProgram.InputLayout);
                _deviceContext.VSSetShader(adtShaderProgram.VertexShader, ref nullClassInstance, 0);
                var terrainGeometryShader = ShowTerrainWireframe
                    ? adtShaderProgram.GeometryShader
                    : default;
                _deviceContext.GSSetShader(terrainGeometryShader, ref nullClassInstance, 0);
                _deviceContext.PSSetShader(adtShaderProgram.PixelShader, ref nullClassInstance, 0);
                _deviceContext.VSSetConstantBuffers(0, 1, ref adtPerObjectConstantBuffer);
                _deviceContext.PSSetConstantBuffers(0, 1, ref adtPerObjectConstantBuffer);
                _deviceContext.VSSetConstantBuffers(1, 1, ref layerDataConstantBuffer);
                _deviceContext.PSSetConstantBuffers(1, 1, ref layerDataConstantBuffer);

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
                _deviceContext.UpdateSubresource(
                    layerDataConstantBuffer,
                    0,
                    ref Unsafe.NullRef<Box>(),
                    ref layerCB,
                    0,
                    0);
                ConstantBufferUpdates++;
                var currentAdtShaderLayerCount = MaxTerrainLayers;
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
                        ScreenSpaceCulling.IntersectsRenderDistance(
                            camera.Position,
                            terrainSphere.Center,
                            terrainSphere.Radius,
                            TerrainRenderDistance))
                    {
                        var skipChunkFrustumTests =
                            terrainFrustumIntersection == Frustum.BoxIntersection.Inside;
                        var skipChunkDistanceTests = ScreenSpaceCulling.IsFullyWithinRenderDistance(
                            camera.Position,
                            terrainSphere.Center,
                            terrainSphere.Radius,
                            TerrainRenderDistance);

                        for (var c = 0; c < adt.Terrain.chunkBounds.Length; c++)
                        {
                            var bounds = adt.Terrain.chunkBounds[c];
                            var boundsSphere = adt.Terrain.chunkBoundingSpheres[c];
                            if ((!skipChunkDistanceTests && !ScreenSpaceCulling.IntersectsRenderDistance(
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
                    var modelMatrix = adt.GetModelMatrix();
                    var brushWorldPosition = BrushWorldPosition;
                    var brushCenter = Vector3.Zero;
                    var renderBrush = 0u;
                    if (brushWorldPosition.HasValue &&
                        BrushRadius > 0f &&
                        Matrix4x4.Invert(modelMatrix, out var inverseTerrainModel))
                    {
                        brushCenter = Vector3.Transform(
                            brushWorldPosition.Value,
                            inverseTerrainModel);
                        renderBrush = 1u;
                    }

                    var cb = new ADTPerObjectCB
                    {
                        model_matrix = modelMatrix,
                        projection_matrix = projectionMatrix,
                        rotation_matrix = cameraMatrix,
                        firstPos = adt.Terrain.startPos,
                        renderTerrainGrid = ShowTerrainGrid ? 1u : 0u,
                        terrainGridSettings = new Vector4(
                            TerrainChunkGridHalfWidthInCell,
                            TerrainAdtGridHalfWidthInCell,
                            0f,
                            0f),
                        renderTerrainWireframe = ShowTerrainWireframe ? 1u : 0u,
                        terrainWireframePadding = Vector3.Zero,
                        brushCenter = brushCenter,
                        brushOuterRadius = BrushRadius,
                        brushFalloffRadius = BrushRadius * BrushFalloff,
                        renderBrush = renderBrush,
                        brushShape = (uint)BrushShape,
                        brushFalloffProfile = (uint)BrushFalloffProfile,
                        brushColor = BrushColor
                    };

                    _deviceContext.UpdateSubresource(adtPerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref cb, 0, 0);
                    ConstantBufferUpdates++;
                    _deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in adtVertexStride, in adtVertexOffset);
                    VertexBufferBindings++;
                    var alphaMaterialArray = adt.Terrain.alphaMaterialArray;
                    _deviceContext.PSSetShaderResources(TerrainAlphaTextureSlot, 1, ref alphaMaterialArray);
                    var alphaSliceBuffer = adt.Terrain.alphaSliceBuffer;
                    _deviceContext.PSSetConstantBuffers(2, 1, ref alphaSliceBuffer);
                    var chunkLayerDataBuffer = adt.Terrain.chunkLayerDataBuffer;
                    _deviceContext.PSSetConstantBuffers(3, 1, ref chunkLayerDataBuffer);
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
                            _deviceContext.IASetIndexBuffer(indexBuffer, Format.FormatR32Uint, 0);
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
                            _deviceContext.PSSetShader(shader.PixelShader, ref nullClassInstance, 0);
                            currentAdtShaderLayerCount = shaderLayerCount;
                            currentAdtUsesHeightTextures = batch.usesHeightTextures;
                        }

                        for (int s = 0; s < shaderLayerCount; s++)
                            _srvScratch[s] = s < batch.materialFDIDs.Length
                                ? ResolveTerrainDiffuseTexture(batch.materialFDIDs[s])
                                : emptyTerrainTexture;
                        _deviceContext.PSSetShaderResources(0, (uint)shaderLayerCount, ref _srvScratch[0]);
                        TextureBindingCalls++;

                        if (batch.usesHeightTextures)
                        {
                            for (int s = 0; s < shaderLayerCount; s++)
                                _srvScratch[s] = s < batch.heightMaterialFDIDs.Length
                                    ? ResolveFrameTexture((uint)batch.heightMaterialFDIDs[s])
                                    : missingTexture;
                            _deviceContext.PSSetShaderResources(
                                TerrainHeightTextureSlot,
                                (uint)shaderLayerCount,
                                ref _srvScratch[0]);
                            TextureBindingCalls++;
                        }

                        var indexCount = useFarLod
                            ? TerrainFarLodIndicesPerChunk
                            : TerrainIndicesPerChunk;
                        _deviceContext.DrawIndexed(
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
            ComPtr<ID3D11GeometryShader> nullGeometryShader = default;
            _deviceContext.GSSetShader(nullGeometryShader, ref nullClassInstance, 0);
            gpuTimer?.EndTerrain();
            TerrainSubmissionTimeMs = RenderADT
                ? Math.Max(0, Stopwatch.GetElapsedTime(passStarted).TotalMilliseconds - TerrainCullingTimeMs)
                : 0;

            // Set up WMO stuff, we do this before the loop since they're all shared
            passStarted = Stopwatch.GetTimestamp();
            gpuTimer?.BeginWorldModels();
            _deviceContext.RSSetState(wmoRasterizerState);
            _deviceContext.IASetInputLayout(wmoShaderProgram.InputLayout);
            _deviceContext.VSSetShader(wmoShaderProgram.VertexShader, ref nullClassInstance, 0);
            _deviceContext.PSSetShader(wmoShaderProgram.PixelShader, ref nullClassInstance, 0);
            _deviceContext.PSSetSamplers(0, 1, ref textureSampler);
            _deviceContext.VSSetConstantBuffers(0, 1, ref wmoPerObjectConstantBuffer);
            _deviceContext.PSSetConstantBuffers(0, 1, ref wmoPerObjectConstantBuffer);

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
            var lastWmoLegacyLighting = -1;
            var lastWmoSamplerIndex = 4; // The WMO pass starts with textureSampler bound.

            var viewProjection = cameraMatrix * projectionMatrix;
            foreach (var (_, instances) in wmoInstances)
            {
                if (!RenderWMO || instances.Count == 0)
                    continue;

                var firstInstance = instances[0];
                if (!firstInstance.IsLoaded)
                    continue;

                var wmo = firstInstance.GetWMO();
                wmoConstantBuffer.useLegacyLighting = wmo.legacyLighting ? 1 : 0;
                candidateWMOs += instances.Count;
                var cullingStarted = Stopwatch.GetTimestamp();
                ResetWmoVisibilityBatches();
                for (int i = 0; i < instances.Count; i++)
                {
                    var instance = instances[i];
                    var sphere = instance.CachedBoundingSphere ?? instance.GetBoundingSphere();
                    var cameraSphere = sphere.GetValueOrDefault();
                    var cameraVisible = sphere.HasValue &&
                        ScreenSpaceCulling.IntersectsRenderDistance(camera.Position, cameraSphere.Center, cameraSphere.Radius, ModelRenderDistance) &&
                        frustum.IsSphereVisible(cameraSphere.Center, cameraSphere.Radius);
                    instance.SetCameraVisibilityFrame(_renderFrameNumber, cameraVisible);
                    if (cameraVisible)
                    {
                        if (!instance.IsSelected && ScreenSpaceCulling.IsBelowPixelThresholdNormalized(
                                camera.Position,
                                normalizedCameraForward,
                                cameraSphere.Center,
                                cameraSphere.Radius,
                                verticalProjectionScale,
                                _renderHeight,
                                MinimumModelScreenSizePixels))
                        {
                            instance.SetCameraVisibilityFrame(_renderFrameNumber, false);
                            sizeCulledWMOs++;
                            continue;
                        }

                        visibleWMOs++;
                        var enabledGroups = instance.EnabledGroups;
                        if (!EnableWmoPortalCulling)
                        {
                            GetWmoVisibilityBatch(enabledGroups).InstanceIndices.Add(i);
                            continue;
                        }

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
                    if (RenderLiquid)
                    {
                        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
                        {
                            if (!enabledGroups[groupIndex] || !wmo.groupBatches[groupIndex].liquid.HasGeometry)
                                continue;
                            foreach (var instanceIndex in visibleInstanceIndices)
                                _visibleWmoLiquids.Add(new WmoLiquidInstance(instances[instanceIndex], groupIndex));
                        }
                    }
                    for (int batchStart = 0; batchStart < visibleInstanceIndices.Count; batchStart += MaxInstancesPerBatch)
                    {
                        int batchSize = Math.Min(MaxInstancesPerBatch, visibleInstanceIndices.Count - batchStart);

                        // the normal approach to do updatesubresource doesn't work for dynamic buffers, so we have to do the below block instead
                        unsafe
                        {
                            MappedSubresource mapped = default;
                            SilkMarshal.ThrowHResult(_deviceContext.Map(instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                            var dest = new Span<Matrix4x4>(mapped.PData, batchSize);
                            for (int i = 0; i < batchSize; i++)
                                dest[i] = instances[visibleInstanceIndices[batchStart + i]].GetModelMatrix();

                            _deviceContext.Unmap(instanceMatrixBuffer, 0);
                            InstanceBufferMapCalls++;
                        }

                        _deviceContext.IASetVertexBuffers(1, 1, ref instanceMatrixBuffer, in instanceStride, in instanceOffset);
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
                                _deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in wmoVertexStride, in wmoVertexOffset);
                                _deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR16Uint, 0);
                                VertexBufferBindings++;
                                IndexBufferBindings++;
                                currentGroupId = batch.groupID;
                            }

                            wmoConstantBuffer.vertexShader = (int)ShaderEnums.WMOShaders[(int)batch.shader].VertexShader;
                            wmoConstantBuffer.pixelShader = (int)ShaderEnums.WMOShaders[(int)batch.shader].PixelShader;
                            ApplyBlendMode((int)batch.blendType, ref currentBlendType);
                            wmoConstantBuffer.alphaRef = WmoMaterialPolicy.AlphaReference(
                                batch.blendType, wmo.legacyLighting);

                            if (wmoConstantBuffer.vertexShader != lastWmoVertexShader ||
                                wmoConstantBuffer.pixelShader != lastWmoPixelShader ||
                                wmoConstantBuffer.alphaRef != lastWmoAlphaRef ||
                                wmoConstantBuffer.useLegacyLighting != lastWmoLegacyLighting)
                            {
                                _deviceContext.UpdateSubresource(wmoPerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref wmoConstantBuffer, 0, 0);
                                ConstantBufferUpdates++;
                                lastWmoVertexShader = wmoConstantBuffer.vertexShader;
                                lastWmoPixelShader = wmoConstantBuffer.pixelShader;
                                lastWmoAlphaRef = wmoConstantBuffer.alphaRef;
                                lastWmoLegacyLighting = wmoConstantBuffer.useLegacyLighting;
                            }

                            for (int s = 0; s < batch.materialFDIDs.Length; s++)
                                _srvScratch[s] = ResolveFrameTexture(batch.materialFDIDs[s]);
                            if (batch.materialFDIDs.Length > 0)
                            {
                                _deviceContext.PSSetShaderResources(0, (uint)batch.materialFDIDs.Length, ref _srvScratch[0]);
                                TextureBindingCalls++;
                            }

                            var samplerIndex = wmo.legacyLighting
                                ? WmoMaterialPolicy.SamplerIndex(wmo.preppedMats[batch.materialIndex].Flags)
                                : 4;
                            if (samplerIndex != lastWmoSamplerIndex)
                            {
                                var materialSampler = samplerIndex == 4
                                    ? textureSampler
                                    : m2TextureSamplers[samplerIndex];
                                _deviceContext.PSSetSamplers(0, 1, ref materialSampler);
                                lastWmoSamplerIndex = samplerIndex;
                            }

                            _deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchSize, batch.firstFace, 0, 0);

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
            _m2DepthStates.BeginPass();
            _deviceContext.RSSetState(wmoRasterizerState);
            _deviceContext.IASetInputLayout(m2ShaderProgram.InputLayout);
            _deviceContext.VSSetShader(m2ShaderProgram.VertexShader, ref nullClassInstance, 0);
            _deviceContext.PSSetShader(m2ShaderProgram.PixelShader, ref nullClassInstance, 0);
            _deviceContext.VSSetConstantBuffers(0, 1, ref m2PerObjectConstantBuffer);
            _deviceContext.PSSetConstantBuffers(0, 1, ref m2PerObjectConstantBuffer);
            _deviceContext.VSSetConstantBuffers(1, 1, ref m2BonePaletteConstantBuffer);

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
                globalOpacity = 1f,
                materialColor = Vector4.One,
                diffuseColor = DiffuseColor,
                alphaRef = 1.0f,
                blendMode = 0,
                _pad = Vector3.Zero
            };
            var lastM2BlendMode = float.NaN;
            var lastM2VertexShader = int.MinValue;
            var lastM2PixelShader = int.MinValue;
            var lastM2AlphaRef = float.NaN;
            var lastM2HasSkinning = -1;
            var lastM2HasAnimation = false;
            var lastM2MaterialColor = Vector4.Zero;
            var lastM2TexMatrix1 = Matrix4x4.Identity;
            var lastM2TexMatrix2 = Matrix4x4.Identity;
            var lastM2HasTexMatrix1 = -1;
            var lastM2HasTexMatrix2 = -1;
            bool? lastM2TwoSided = null;

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
                    if (RenderWMO &&
                        instance.ParentWMO is { } parentWmo &&
                        !parentWmo.IsCameraVisibleForFrame(_renderFrameNumber))
                        continue;

                    if (EnableWmoPortalCulling &&
                        instance.ParentWMO is { } &&
                        !instance.ParentWMO.IsDoodadPortalVisible(
                            instance.WmoDoodadIndex,
                            _renderFrameNumber))
                    {
                        portalCulledM2s++;
                        continue;
                    }
                    var sphere = packet.WorldBounds[i];
                    if (ScreenSpaceCulling.IntersectsRenderDistance(camera.Position, sphere.Center, sphere.Radius, ModelRenderDistance) &&
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

                var animationTime = Stopwatch.GetElapsedTime(m2AnimationEpoch).TotalMilliseconds;
                m2ConstantBuffer.hasSkinning = m2.animation is { HasAnimatedBones: true } ? 1 : 0;
                if (m2.animation is { HasAnimatedBones: true } animation)
                {
                    Array.Fill(m2BonePalette, Matrix4x4.Identity);
                    animation.Evaluate(0, animationTime, m2BonePalette);
                    _deviceContext.UpdateSubresource(m2BonePaletteConstantBuffer, 0,
                        ref Unsafe.NullRef<Box>(), ref m2BonePalette[0], 0, 0);
                    ConstantBufferUpdates++;
                }

                var vertexBuffer = m2.vertexBuffer;
                var indiceBuffer = m2.indiceBuffer;

                _deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in m2VertexStride, in m2VertexOffset);
                _deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR16Uint, 0);
                VertexBufferBindings++;
                IndexBufferBindings++;

                for (int batchStart = 0; batchStart < _visibleIndices.Count; batchStart += MaxInstancesPerBatch)
                {
                    int batchCount = Math.Min(MaxInstancesPerBatch, _visibleIndices.Count - batchStart);

                    unsafe
                    {
                        MappedSubresource mapped = default;
                        SilkMarshal.ThrowHResult(_deviceContext.Map(instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                        var dest = new Span<Matrix4x4>(mapped.PData, batchCount);
                        for (int i = 0; i < batchCount; i++)
                            dest[i] = packet.WorldMatrices[_visibleIndices[batchStart + i]];

                        _deviceContext.Unmap(instanceMatrixBuffer, 0);
                        InstanceBufferMapCalls++;
                    }

                    _deviceContext.IASetVertexBuffers(1, 1, ref instanceMatrixBuffer, in instanceStride, in instanceOffset);
                    VertexBufferBindings++;

                    for (int j = 0; j < m2.submeshes.Length; j++)
                    {
                        var batch = m2.submeshes[j];
                        _m2DepthStates.Apply(m2.usesLegacyDepthFlags, batch.renderFlags);

                        var isTwoSided = IsM2TwoSided(batch.renderFlags);
                        if (lastM2TwoSided != isTwoSided)
                        {
                            _deviceContext.RSSetState(
                                isTwoSided ? m2TwoSidedRasterizerState : wmoRasterizerState);
                            lastM2TwoSided = isTwoSided;
                        }

                        m2ConstantBuffer.blendMode = batch.blendType;
                        m2ConstantBuffer.alphaRef = ApplyBlendMode((int)batch.blendType, ref currentBlendType);
                        m2ConstantBuffer.vertexShader = (int)batch.vertexShaderID;
                        m2ConstantBuffer.pixelShader = (int)batch.pixelShaderID;
                        if (m2.animation is { } materialAnimation)
                        {
                            var material = materialAnimation.EvaluateMaterial(batch, 0, animationTime);
                            m2ConstantBuffer.materialColor = material.Color;
                            m2ConstantBuffer.texMatrix1 = material.TextureMatrix1;
                            m2ConstantBuffer.texMatrix2 = material.TextureMatrix2;
                            m2ConstantBuffer.hasTexMatrix1 = material.HasTextureMatrix1 ? 1 : 0;
                            m2ConstantBuffer.hasTexMatrix2 = material.HasTextureMatrix2 ? 1 : 0;
                        }
                        else
                        {
                            m2ConstantBuffer.materialColor = Vector4.One;
                            m2ConstantBuffer.texMatrix1 = Matrix4x4.Identity;
                            m2ConstantBuffer.texMatrix2 = Matrix4x4.Identity;
                            m2ConstantBuffer.hasTexMatrix1 = 0;
                            m2ConstantBuffer.hasTexMatrix2 = 0;
                        }

                        if (m2ConstantBuffer.blendMode != lastM2BlendMode ||
                            m2ConstantBuffer.vertexShader != lastM2VertexShader ||
                            m2ConstantBuffer.pixelShader != lastM2PixelShader ||
                            m2ConstantBuffer.alphaRef != lastM2AlphaRef ||
                            m2ConstantBuffer.hasSkinning != lastM2HasSkinning ||
                            lastM2HasAnimation != (m2.animation is not null) ||
                            m2ConstantBuffer.materialColor != lastM2MaterialColor ||
                            !m2ConstantBuffer.texMatrix1.Equals(lastM2TexMatrix1) ||
                            !m2ConstantBuffer.texMatrix2.Equals(lastM2TexMatrix2) ||
                            m2ConstantBuffer.hasTexMatrix1 != lastM2HasTexMatrix1 ||
                            m2ConstantBuffer.hasTexMatrix2 != lastM2HasTexMatrix2)
                        {
                            _deviceContext.UpdateSubresource(m2PerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref m2ConstantBuffer, 0, 0);
                            ConstantBufferUpdates++;
                            lastM2BlendMode = m2ConstantBuffer.blendMode;
                            lastM2VertexShader = m2ConstantBuffer.vertexShader;
                            lastM2PixelShader = m2ConstantBuffer.pixelShader;
                            lastM2AlphaRef = m2ConstantBuffer.alphaRef;
                            lastM2HasSkinning = m2ConstantBuffer.hasSkinning;
                            lastM2HasAnimation = m2.animation is not null;
                            lastM2MaterialColor = m2ConstantBuffer.materialColor;
                            lastM2TexMatrix1 = m2ConstantBuffer.texMatrix1;
                            lastM2TexMatrix2 = m2ConstantBuffer.texMatrix2;
                            lastM2HasTexMatrix1 = m2ConstantBuffer.hasTexMatrix1;
                            lastM2HasTexMatrix2 = m2ConstantBuffer.hasTexMatrix2;
                        }

                        for (int s = 0; s < batch.material.Length; s++)
                            _srvScratch[s] = ResolveFrameTexture(batch.material[s]);
                        if (batch.material.Length > 0)
                        {
                            _deviceContext.PSSetShaderResources(0, (uint)batch.material.Length, ref _srvScratch[0]);
                            var samplerCount = Math.Min(batch.material.Length, _samplerScratch.Length);
                            for (var s = 0; s < samplerCount; s++)
                            {
                                var flags = batch.textureFlags is { } textureFlags && s < textureFlags.Length
                                    ? textureFlags[s]
                                    : 0;
                                _samplerScratch[s] = m2TextureSamplers[GetM2SamplerIndex(flags)];
                            }
                            _deviceContext.PSSetSamplers(0, (uint)samplerCount, ref _samplerScratch[0]);
                            TextureBindingCalls++;
                        }

                        _deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchCount, batch.firstFace, 0, 0);
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
            _m2DepthStates.EndPass();
            M2SubmissionTimeMs = Math.Max(
                0,
                Stopwatch.GetElapsedTime(passStarted).TotalMilliseconds - M2CullingTimeMs);

            ApplyBlendMode(0, ref currentBlendType);

            // Liquid is a separate pass for ADT MH2O and visible WMO groups.
            // It remains available when terrain geometry is hidden.
            if (RenderLiquid)
            {
                var liquidStats = _worldLiquidRenderer.Render(
                    camera,
                    adtContainers,
                    _visibleWmoLiquids,
                    coarseCulledTileRoots,
                    TerrainRenderDistance,
                    ModelRenderDistance,
                    Environment.TickCount64,
                    LightDirection,
                    AmbientColor,
                    DiffuseColor,
                    ActiveWorldLighting);
                candidateLiquidBatches = liquidStats.CandidateBatches;
                visibleLiquidBatches = liquidStats.VisibleBatches;
                LiquidDrawCalls = liquidStats.DrawCalls;
                LiquidSubmittedIndices = liquidStats.SubmittedIndices;
                LiquidCullingTimeMs = liquidStats.CullingMilliseconds;
                LiquidSubmissionTimeMs = liquidStats.SubmissionMilliseconds;
                drawCalls += liquidStats.DrawCalls;
                submittedIndexCount += liquidStats.SubmittedIndices;
                CullingTimeMs += liquidStats.CullingMilliseconds;

                // The liquid renderer owns transient state while submitting
                // MH2O. Restore the scene's opaque raster/depth defaults even
                // when the debug pass is disabled so later passes and callers
                // observe the same state contract as before liquid support.
                _deviceContext.RSSetState(rasterizerState);
                ComPtr<ID3D11DepthStencilState> nullLiquidDepthState = default;
                _deviceContext.OMSetDepthStencilState(nullLiquidDepthState, 0);
            }

            // Debug bounds rendering
            passStarted = Stopwatch.GetTimestamp();
            gpuTimer?.BeginDebug();
            if (ShowBoundingBoxes || ShowBoundingSpheres ||
                (SelectionVisualsEnabled && SelectedObject != null))
            {
                lock (SceneObjectLock)
                {
                    DebugDrawCalls = _debugBoundsRenderer.Render(
                        SceneObjects,
                        ShowBoundingBoxes,
                        ShowBoundingSpheres,
                        SelectionVisualsEnabled && SelectedObject != null,
                        projectionMatrix,
                        cameraMatrix,
                        rasterizerState);
                }
                drawCalls += DebugDrawCalls;
            }
            gpuTimer?.EndDebug();
            DebugSubmissionTimeMs = Stopwatch.GetElapsedTime(passStarted).TotalMilliseconds;

            //swapchain.Present(1, 0);

            gizmoWasUsing = false;
            gizmoWasOver = false;

            return (drawCalls, submittedIndexCount);
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
                _debugBoundsRenderer.Dispose();
                _m2DepthStates.Dispose();
                _skyRenderer.Dispose();
                _worldLiquidRenderer.Dispose();
                foreach (var bounds in tileSceneBounds.Values)
                    bounds.Dispose();
                tileSceneBounds.Clear();
                tileSceneBoundsByRoot.Clear();

                textureSampler.Dispose();
                clampSampler.Dispose();
                foreach (var sampler in m2TextureSamplers)
                    sampler.Dispose();
                depthStencilView.Dispose();
                depthTexture.Dispose();
                adtPerObjectConstantBuffer.Dispose();
                layerDataConstantBuffer.Dispose();
                m2PerObjectConstantBuffer.Dispose();
                m2BonePaletteConstantBuffer.Dispose();
                wmoPerObjectConstantBuffer.Dispose();
                instanceMatrixBuffer.Dispose();
                emptyTerrainTexture.Dispose();
                missingTexture.Dispose();
                rasterizerState.Dispose();
                wmoRasterizerState.Dispose();
                m2TwoSidedRasterizerState.Dispose();
                foreach (var bs in _blendStates)
                    bs.Dispose();
            }
        }
    }
}
