using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Input;
using Silk.NET.Maths;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Profiling;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11
{
    [Flags]
    public enum InputModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2
    }

    public enum EditAction
    {
        Default,
        Positive,
        Negative
    }

    public struct WowClientConfig
    {
        public string wowDir = "";
        public string wowProduct = "";

        public string buildConfig = "";
        public string cdnConfig = "";

        public WowClientConfig()
        {
        }
    }

    public interface IImGuiBackend
    {
        void Initialize();
        void Update(float deltaTime);
        void Render();
        void Dispose();
    }

    public struct InputFrame
    {
        public Vector2 MousePosition;
        public bool LeftMouseDown;
        public bool RightMouseDown;
        public EditorModeId Mode;
        public TerrainBrushInput TerrainBrush;
        public InputModifiers Modifiers;

        public float MouseWheel;

        public HashSet<Key> KeysDown;
    }

    public struct TerrainBrushInput
    {
        public TerrainBrushMode ToolMode;
        public EditAction Action;
        public float Radius;
        public float Speed;
        public float InnerRadius;
        public float FlattenHeight;
        public int SmoothIterations;
    }

    public class RendererStats
    {
        public double FrameTimeMs { get; internal set; }
        public double FPS { get; internal set; }

        public uint DrawCalls { get; internal set; }
        public ulong SubmittedIndexCount { get; internal set; }
        public ulong SubmittedTriangleCount => SubmittedIndexCount / 3;
        public double UpdateTimeMs { get; internal set; }
        public double MutexWaitTimeMs { get; internal set; }
        public double TileUpdateTimeMs { get; internal set; }
        public double AssetUploadTimeMs { get; internal set; }
        public double SceneRenderTimeMs { get; internal set; }
        public double CullingTimeMs { get; internal set; }
        public double RenderOverheadTimeMs { get; internal set; }
        public double CpuFrameTimeMs => UpdateTimeMs + TileUpdateTimeMs +
            AssetUploadTimeMs + SceneRenderTimeMs + RenderOverheadTimeMs;
        public double? GpuFrameTimeMs { get; internal set; }
        public double? GpuUploadTimeMs { get; internal set; }
        public double? GpuDrawTimeMs { get; internal set; }
        public double? GpuWorldModelTimeMs { get; internal set; }
        public double? GpuDoodadTimeMs { get; internal set; }
        public double? GpuTerrainTimeMs { get; internal set; }
        public double? GpuDebugTimeMs { get; internal set; }
        public double SceneSetupTimeMs { get; internal set; }
        public double WmoCullingTimeMs { get; internal set; }
        public double WmoSubmissionTimeMs { get; internal set; }
        public double M2CullingTimeMs { get; internal set; }
        public double M2SubmissionTimeMs { get; internal set; }
        public double TerrainCullingTimeMs { get; internal set; }
        public double TerrainSubmissionTimeMs { get; internal set; }
        public double TileHierarchyCullingTimeMs { get; internal set; }
        public double DebugSubmissionTimeMs { get; internal set; }
        public uint WmoDrawCalls { get; internal set; }
        public uint M2DrawCalls { get; internal set; }
        public uint TerrainDrawCalls { get; internal set; }
        public uint DebugDrawCalls { get; internal set; }
        public uint WmoSubmittedInstances { get; internal set; }
        public uint M2SubmittedInstances { get; internal set; }
        public uint TerrainSubmittedChunks { get; internal set; }
        public ulong WmoSubmittedIndices { get; internal set; }
        public ulong M2SubmittedIndices { get; internal set; }
        public ulong TerrainSubmittedIndices { get; internal set; }
        public uint InstanceBufferMapCalls { get; internal set; }
        public uint ConstantBufferUpdates { get; internal set; }
        public uint TextureBindingCalls { get; internal set; }
        public uint BlendStateBindings { get; internal set; }
        public uint VertexBufferBindings { get; internal set; }
        public uint IndexBufferBindings { get; internal set; }
        public int PendingAssetOperations { get; internal set; }
        public int UploadedResources { get; internal set; }
        public int VisibleTerrainChunks { get; internal set; }
        public int CandidateTerrainChunks { get; internal set; }
        public int VisibleWorldModels { get; internal set; }
        public int CandidateWorldModels { get; internal set; }
        public int VisibleDoodads { get; internal set; }
        public int CandidateDoodads { get; internal set; }
        public int SizeCulledWorldModels { get; internal set; }
        public int SizeCulledDoodads { get; internal set; }
        public int FarLodTerrainChunks { get; internal set; }
        public int CandidateTiles { get; internal set; }
        public int CoarseCulledTiles { get; internal set; }
        public int PortalCulledWmoGroups { get; internal set; }
        public int PortalCulledDoodads { get; internal set; }
        public int TraversedWmoPortalReferences { get; internal set; }
    }

    public enum WowViewerEngineState
    {
        Created,
        Initializing,
        LoadingContent,
        Ready,
        Failed,
        Disposed
    }

    public sealed record WowViewerEngineStatus(WowViewerEngineState State, string Message, Exception? Error = null);

    public class WowViewerEngine : IDisposable, IAsyncDisposable
    {
        private static readonly SemaphoreSlim ContentInitializationGate = new(1, 1);
        private static long _activeGeneration;

        private WowClientConfig _wowConfig;
        private readonly long _generation;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private Task? _contentInitializationTask;

        private Dictionary<string, (string buildConfig, string cdnConfig)> _productList = new();

        private string[] _products = Array.Empty<string>(); // simple string list for IMGUI
        private int _currentProduct = -1; // list index for IMGUI

        // if both are true, it will autoload first product. just temporary convenience
        // hardcoded for now, will be depprecated when saving current product config
        private bool _SetDefaultProduct = true; // whether to auto set a default product (first one from build info list) or not, if none is set in config
        private bool _AutoLoadProduct = true; // whether to auto load the product or not. 

        public RendererStats Stats { get; } = new();
        public RendererSettings Settings { get; private set; } = new();
        public bool DetailedGpuProfilingEnabled
        {
            get => _gpuFrameTimer?.DetailedPassTimingEnabled ?? false;
            set
            {
                if (_gpuFrameTimer != null)
                    _gpuFrameTimer.DetailedPassTimingEnabled = value;
            }
        }
        public Vector3? InitialCameraPosition { get; set; }
        public Vector3? InitialCameraDirection { get; set; }
        public uint CurrentMapHighestUniqueId => sceneManager?.CurrentMapHighestUniqueId ?? 0;
        public uint CurrentWdtFileDataId => sceneManager?.CurrentWDTFileDataID ?? 0;
        public WowViewerEngineStatus Status { get; private set; } =
            new(WowViewerEngineState.Created, "Renderer created.");
        public event EventHandler<WowViewerEngineStatus>? StatusChanged;
        private (uint Wdt, byte X, byte Y, Vector2 Position)? _pendingTerrainNavigation;

        private bool disposed = false;

        private IImGuiBackend? imgui;
        private readonly bool renderImGUI;

        private ComPtr<ID3D11Device> device = default;
        private ComPtr<ID3D11DeviceContext> deviceContext = default;
        private ComPtr<IDXGIFactory2> factory = default;

        private ComPtr<ID3D11Texture2D> sharedTexture = default;
        private ComPtr<ID3D11RenderTargetView> _sharedRTV = default;
        private ComPtr<IDXGIKeyedMutex> _keyedMutex = default;
        private GpuFrameTimer? _gpuFrameTimer;
        public ComPtr<ID3D11ShaderResourceView> SharedSRV { get; private set; }
        public uint SharedTextureWidth => (uint)viewportWidth;
        public uint SharedTextureHeight => (uint)viewportHeight;

        public bool IsInitialized = false;
        public bool IsInitializing = false;

        public bool UseKeyedMutex = false;

        /// <summary>
        /// Renders into a caller-owned render target. This is used by the Avalonia
        /// presentation pool so the compositor can consume one image while the
        /// renderer fills another. The caller must acquire/release the target's
        /// keyed mutex around <see cref="RenderTo"/>.
        /// </summary>
        public bool UsesExternalRenderTarget { get; set; }

        private CompiledShader adtShaderProgram;
        private CompiledShader wmoShaderProgram;
        private CompiledShader m2ShaderProgram;
        private CompiledShader bboxShaderProgram;

        private float movementSpeed = 150f;
        private bool hasFocus = true;

        private Vector2 LastMousePosition;
        private Vector2? MouseDownPosition;
        private bool wasMouseDown = false;

        public Camera activeCamera { get; private set; } = null!;

        private int viewportWidth = 1;
        private int viewportHeight = 1;

        private bool shadersReady = false;

        private string WDTFDIDInput = "";

        private bool gizmoWasUsing = false;
        private bool gizmoWasOver = false;
        //  private ImGuizmoOperation currentGizmoOperation = ImGuizmoOperation.Translate;
        private bool wasSpacePressed = false;
        private bool showMapSelection = false;

        private ShaderManager shaderManager = null!;
        private SceneManager sceneManager = null!;
        public Container3D? SelectedObject => sceneManager?.SelectedObject;
        public bool HasUnsavedTerrainChanges => sceneManager?.HasUnsavedTerrainChanges == true;
        public IReadOnlyList<ModifiedTerrainTile> ModifiedTerrainTiles =>
            sceneManager?.GetModifiedTerrainTiles() ?? [];

        public void BeginTerrainStroke() => sceneManager?.BeginTerrainStroke();
        public TerrainStrokeDelta? EndTerrainStroke() => sceneManager?.EndTerrainStroke();
        public void ApplyTerrainStroke(TerrainStrokeDelta delta, bool useAfter) =>
            sceneManager?.ApplyTerrainStroke(delta, useAfter);
        public void MarkTerrainChangesSaved() => sceneManager?.MarkTerrainChangesSaved();

        public void UpdateSelectedObjectTransform(Vector3 position, Vector3 rotationDegrees, float scale) =>
            sceneManager?.UpdateSelectedObjectTransform(
                position,
                rotationDegrees,
                scale,
                lockWorldModelScale: _wowConfig.wowProduct.StartsWith(
                    "wow_classic",
                    StringComparison.OrdinalIgnoreCase));
        public void UpdateSelectedWmoPlacement(ushort doodadSet, ushort nameSet) =>
            sceneManager?.UpdateSelectedWmoPlacement(doodadSet, nameSet);
        // private ImGuiController imGuiController = null;

        private uint frameDelta = 0;

        // calcualte average fps
        private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
        private double _lastFpsTime;
        private uint _frameCount;
        private uint _maxDeltaMS = 500; // update fps every x ms

        private string[] wowProductList = [];
        private IntPtr _cachedSharedHandle = IntPtr.Zero;
        public WowViewerEngine(WowClientConfig wowConfig, IImGuiBackend? imguiBackend, bool renderImGUI)
        {
            _generation = Interlocked.Increment(ref _activeGeneration);
            _wowConfig = wowConfig;

            if (string.IsNullOrEmpty(_wowConfig.wowDir))
            {
                // try to get client path from registry on windows
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var installPath = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft", "InstallPath", null) as string;
                    if (!string.IsNullOrEmpty(installPath))
                    {
                        var lastDir = new DirectoryInfo(installPath).Name;
                        if (lastDir.StartsWith('_'))
                            installPath = Directory.GetParent(installPath.TrimEnd('\\'))?.FullName;

                        if (installPath != null)
                            _wowConfig.wowDir = installPath;
                    }
                    else
                    {
                        _wowConfig.wowDir = "C:\\World of Warcraft";
                    }
                }
            }

            LoadBuildInfo(_wowConfig.wowDir);

            this.imgui = imguiBackend;
            this.renderImGUI = renderImGUI;
        }

        // Load
        public void Initialize(DXGI dxgi, ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> deviceContext, Vector2D<int> frameBufferSize)
        {
            Console.WriteLine("Initializing WowViewerEngine..."); ;
            if (IsInitializing) return;

            IsInitializing = true;
            SetStatus(WowViewerEngineState.Initializing, "Initializing Direct3D renderer...");

            try
            {

            this.device = device;
            this.deviceContext = deviceContext;
            _gpuFrameTimer = new GpuFrameTimer(device, deviceContext);

            // TODO verify if this should be this project or UI app after split
            var exeLocation = Path.GetDirectoryName(AppContext.BaseDirectory);
            if (exeLocation == null)
            {
                Console.WriteLine("Could not determine executable location for shader loading");
                return;
            }

            shaderManager = new ShaderManager(device, Path.Combine(exeLocation, "Shaders"));
            sceneManager = new SceneManager(device, deviceContext, shaderManager);

            // imgui?.Initialize();

            adtShaderProgram = shaderManager.GetOrCompileShader("adt");
            wmoShaderProgram = shaderManager.GetOrCompileShader("wmo");
            m2ShaderProgram = shaderManager.GetOrCompileShader("m2");
            bboxShaderProgram = shaderManager.GetOrCompileShader("boundingbox");

            sceneManager.Initialize(shaderManager, adtShaderProgram, wmoShaderProgram, m2ShaderProgram, bboxShaderProgram);
            sceneManager.TerrainTileHeightAvailable += OnTerrainTileHeightAvailable;

            shadersReady = true;

            WDTFDIDInput = sceneManager.CurrentWDTFileDataID.ToString();
            var startPos = InitialCameraPosition ?? new Vector3(3875f, -2050f, 616f); // quel'thalas, retail.
            if (!InitialCameraPosition.HasValue && _wowConfig.wowProduct == "wow_classic_era")
            {
                startPos = new Vector3(0, 0, 200); // alterac, wow classic.
            }

            // Init camera
            activeCamera = new Camera(
                startPos,
                yaw: 168f, pitch: 13f,
                aspectRatio: frameBufferSize.X / frameBufferSize.Y
            );
            if (InitialCameraDirection.HasValue)
                activeCamera.SetDirection(InitialCameraDirection.Value);
            activeCamera.FarPlane = Math.Max(Settings.TerrainRenderDistance, Settings.ModelRenderDistance);
            activeCamera.ModifyDirection(0, 0);

            ApplySettings(Settings);

            Resize((uint)frameBufferSize.X, (uint)frameBufferSize.Y);

            LoadCurrentProduct();
            IsInitialized = true;
            IsInitializing = false;
            }
            catch (Exception exception)
            {
                IsInitializing = false;
                IsInitialized = false;
                SetStatus(WowViewerEngineState.Failed, "Renderer initialization failed.", exception);
                throw;
            }
        }

        public unsafe void Resize(uint width, uint height)
        {
            if (width == 0 || height == 0)
                return;

            if (viewportWidth == (int)width && viewportHeight == (int)height)
                return;

            if (device.Handle == null)
                return;

            viewportWidth = (int)width;
            viewportHeight = (int)height;
            _cachedSharedHandle = IntPtr.Zero;

            if (activeCamera != null)
                activeCamera.AspectRatio = (float)width / (float)height;

            // Release old shared resources
            if (_keyedMutex.Handle != null) { _keyedMutex.Dispose(); _keyedMutex = default; }
            if (_sharedRTV.Handle != null) { _sharedRTV.Dispose(); _sharedRTV = default; }
            var oldSrv = SharedSRV;
            if (oldSrv.Handle != null) { oldSrv.Dispose(); SharedSRV = default; }
            if (sharedTexture.Handle != null) { sharedTexture.Dispose(); sharedTexture = default; }

            if (UsesExternalRenderTarget)
            {
                // SceneManager still needs a size-dependent depth buffer and viewport,
                // but the actual color target is supplied to RenderTo for each frame.
                sceneManager?.Resize(width, height, default);
                return;
            }

            var texDesc = new Texture2DDesc
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.FormatB8G8R8A8Unorm,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
                BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
                MiscFlags = UseKeyedMutex ? (uint)(ResourceMiscFlag.SharedKeyedmutex) : (uint)(ResourceMiscFlag.Shared)
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in texDesc, null, ref sharedTexture));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(sharedTexture, null, ref _sharedRTV));
            ComPtr<ID3D11ShaderResourceView> srv = default;
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(sharedTexture, null, ref srv));
            SharedSRV = srv;
            if (UseKeyedMutex)
            {
                _keyedMutex = sharedTexture.QueryInterface<IDXGIKeyedMutex>();
            }

            sceneManager?.Resize(width, height, _sharedRTV);
        }

        public unsafe void AcquireSharedTextureForDisplay()
        {
            if (UseKeyedMutex && _keyedMutex.Handle != null)
                _keyedMutex.AcquireSync(1, unchecked((uint)-1));
        }

        public unsafe void ReleaseSharedTextureFromDisplay()
        {
            if (UseKeyedMutex && _keyedMutex.Handle != null)
                _keyedMutex.ReleaseSync(0);
        }

        public unsafe IntPtr GetSharedTextureHandle()
        {
            if (sharedTexture.Handle == null) return IntPtr.Zero;

            if (_cachedSharedHandle != IntPtr.Zero)
                return _cachedSharedHandle;

            var resource = sharedTexture.QueryInterface<IDXGIResource>();
            void* handle = null;
            SilkMarshal.ThrowHResult(resource.GetSharedHandle(&handle));
            resource.Dispose();
            _cachedSharedHandle = (IntPtr)handle;
            return _cachedSharedHandle;
        }
        public void Update(double deltaTime, InputFrame input)
        {
            var started = Stopwatch.GetTimestamp();
            HandleMouseLook(input, false, (float)deltaTime);
            if (input.Mode == EditorModeId.Terrain)
            {
                var terrainBrush = input.TerrainBrush;
                terrainBrush.Action = ResolveEditAction(input.Modifiers);
                sceneManager.UpdateTerrainBrush(
                    input.MousePosition,
                    activeCamera,
                    viewportWidth,
                    viewportHeight,
                    terrainBrush,
                    input.LeftMouseDown && terrainBrush.Action != EditAction.Default,
                    (float)deltaTime);
            }
            else
            {
                sceneManager.ClearTerrainBrush();
            }

            sceneManager.SelectionVisualsEnabled = input.Mode == EditorModeId.Selection;

            HandleClickSelection(input, false);
            HandleKeyboardMovement(input, (float)deltaTime);


            /*
            if (renderImGUI)
            {
                imgui?.Update((float)deltaTime);

                bool gizmoInUse = gizmoWasUsing || gizmoWasOver;

                var io = ImGui.GetIO();
                HandleMouseLook(input, io, gizmoInUse, (float)deltaTime);
                HandleClickSelection(input, io, gizmoInUse);
                HandleKeyboardMovement(input, (float)deltaTime);
            }
            else
            {   HandleMouseLook(input, new ImGuiIOPtr(), false, (float)deltaTime);
                HandleClickSelection(input, new ImGuiIOPtr(), false);
                HandleKeyboardMovement(input, (float)deltaTime);
            }*/

            // TODO : we may need a special update function for controls/camera triggered by events if refresh rate isn't enough
            // eg a key could be pressed and released between two frame
            Stats.UpdateTimeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        private static EditAction ResolveEditAction(InputModifiers modifiers) =>
            modifiers.HasFlag(InputModifiers.Control)
                ? EditAction.Negative
                : modifiers.HasFlag(InputModifiers.Shift)
                    ? EditAction.Positive
                    : EditAction.Default;

        public void Render(double deltaTime) => RenderCore(deltaTime, useSharedMutex: true);

        /// <summary>
        /// Renders directly into an externally-owned RTV. No application-side texture
        /// copy is performed. The target must be keyed-mutex acquired by the caller.
        /// </summary>
        public unsafe void RenderTo(double deltaTime, ComPtr<ID3D11RenderTargetView> target)
        {
            if (target.Handle == null)
                throw new ArgumentException("A valid external render target is required.", nameof(target));

            if (!UsesExternalRenderTarget)
                throw new InvalidOperationException("External render targets are not enabled for this engine.");

            sceneManager.SetRenderTarget(target);
            RenderCore(deltaTime, useSharedMutex: false);
        }

        private unsafe void RenderCore(double deltaTime, bool useSharedMutex)
        {
            if (!IsInitialized) return;
            var renderStarted = Stopwatch.GetTimestamp();
            frameDelta = (uint)(deltaTime * 1000);

            var mutexAcquired = false;
            var mutexStarted = Stopwatch.GetTimestamp();
            if (useSharedMutex && UseKeyedMutex && _keyedMutex.Handle != null)
            {
                var acquireResult = _keyedMutex.AcquireSync(0, unchecked((uint)-1));
                SilkMarshal.ThrowHResult(acquireResult);
                mutexAcquired = true;
            }
            Stats.MutexWaitTimeMs = Stopwatch.GetElapsedTime(mutexStarted).TotalMilliseconds;

            try
            {
                _gpuFrameTimer?.BeginFrame();

                var phaseStarted = Stopwatch.GetTimestamp();
                sceneManager.UpdateTilesByCameraPos(activeCamera.Position);
                Stats.TileUpdateTimeMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;

                phaseStarted = Stopwatch.GetTimestamp();
                _gpuFrameTimer?.BeginUploads();
                sceneManager.ProcessQueue();
                _gpuFrameTimer?.EndUploads();
                Stats.AssetUploadTimeMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
                Stats.UploadedResources = sceneManager.UploadedResourcesLastFrame;

                phaseStarted = Stopwatch.GetTimestamp();
                _gpuFrameTimer?.BeginDraws();
                if (shadersReady)
                {
                    (var drawCalls, var submittedIndices) = sceneManager.RenderScene(
                        activeCamera,
                        out bool renderGizmoWasUsing,
                        out bool renderGizmoWasOver,
                        _gpuFrameTimer);
                    //if (renderImGUI)
                    //    RenderGizmo();

                    Stats.DrawCalls = drawCalls;
                    Stats.SubmittedIndexCount = submittedIndices;
                    Stats.CullingTimeMs = sceneManager.CullingTimeMs;
                    Stats.VisibleTerrainChunks = sceneManager.visibleChunks;
                    Stats.CandidateTerrainChunks = sceneManager.candidateChunks;
                    Stats.VisibleWorldModels = sceneManager.visibleWMOs;
                    Stats.CandidateWorldModels = sceneManager.candidateWMOs;
                    Stats.VisibleDoodads = sceneManager.visibleM2s;
                    Stats.CandidateDoodads = sceneManager.candidateM2s;
                    Stats.SizeCulledWorldModels = sceneManager.sizeCulledWMOs;
                    Stats.SizeCulledDoodads = sceneManager.sizeCulledM2s;
                    Stats.FarLodTerrainChunks = sceneManager.farLodTerrainChunks;
                    Stats.CandidateTiles = sceneManager.candidateTiles;
                    Stats.CoarseCulledTiles = sceneManager.coarseCulledTiles;
                    Stats.PortalCulledWmoGroups = sceneManager.portalCulledWmoGroups;
                    Stats.PortalCulledDoodads = sceneManager.portalCulledM2s;
                    Stats.TraversedWmoPortalReferences = sceneManager.traversedWmoPortalReferences;
                    Stats.SceneSetupTimeMs = sceneManager.SceneSetupTimeMs;
                    Stats.WmoCullingTimeMs = sceneManager.WmoCullingTimeMs;
                    Stats.WmoSubmissionTimeMs = sceneManager.WmoSubmissionTimeMs;
                    Stats.M2CullingTimeMs = sceneManager.M2CullingTimeMs;
                    Stats.M2SubmissionTimeMs = sceneManager.M2SubmissionTimeMs;
                    Stats.TerrainCullingTimeMs = sceneManager.TerrainCullingTimeMs;
                    Stats.TerrainSubmissionTimeMs = sceneManager.TerrainSubmissionTimeMs;
                    Stats.TileHierarchyCullingTimeMs = sceneManager.TileHierarchyCullingTimeMs;
                    Stats.DebugSubmissionTimeMs = sceneManager.DebugSubmissionTimeMs;
                    Stats.WmoDrawCalls = sceneManager.WmoDrawCalls;
                    Stats.M2DrawCalls = sceneManager.M2DrawCalls;
                    Stats.TerrainDrawCalls = sceneManager.TerrainDrawCalls;
                    Stats.DebugDrawCalls = sceneManager.DebugDrawCalls;
                    Stats.WmoSubmittedInstances = sceneManager.WmoSubmittedInstances;
                    Stats.M2SubmittedInstances = sceneManager.M2SubmittedInstances;
                    Stats.TerrainSubmittedChunks = sceneManager.TerrainSubmittedChunks;
                    Stats.WmoSubmittedIndices = sceneManager.WmoSubmittedIndices;
                    Stats.M2SubmittedIndices = sceneManager.M2SubmittedIndices;
                    Stats.TerrainSubmittedIndices = sceneManager.TerrainSubmittedIndices;
                    Stats.InstanceBufferMapCalls = sceneManager.InstanceBufferMapCalls;
                    Stats.ConstantBufferUpdates = sceneManager.ConstantBufferUpdates;
                    Stats.TextureBindingCalls = sceneManager.TextureBindingCalls;
                    Stats.BlendStateBindings = sceneManager.BlendStateBindings;
                    Stats.VertexBufferBindings = sceneManager.VertexBufferBindings;
                    Stats.IndexBufferBindings = sceneManager.IndexBufferBindings;

                    gizmoWasUsing = renderGizmoWasUsing;
                    gizmoWasOver = renderGizmoWasOver;
                }
                _gpuFrameTimer?.EndDraws();
                Stats.SceneRenderTimeMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
                Stats.PendingAssetOperations = sceneManager.GetPendingOperationCount();
            }
            finally
            {
                _gpuFrameTimer?.EndFrame();
                if (mutexAcquired)
                {
                    // Ensure all rendering commands are visible to the compositor
                    // before handing ownership to the consumer keyed-mutex key.
                    deviceContext.Flush();
                    SilkMarshal.ThrowHResult(_keyedMutex.ReleaseSync(1));
                }
            }

            Stats.GpuFrameTimeMs = _gpuFrameTimer?.LatestFrameMilliseconds;
            Stats.GpuUploadTimeMs = _gpuFrameTimer?.LatestUploadMilliseconds;
            Stats.GpuDrawTimeMs = _gpuFrameTimer?.LatestDrawMilliseconds;
            Stats.GpuWorldModelTimeMs = _gpuFrameTimer?.LatestWorldModelMilliseconds;
            Stats.GpuDoodadTimeMs = _gpuFrameTimer?.LatestDoodadMilliseconds;
            Stats.GpuTerrainTimeMs = _gpuFrameTimer?.LatestTerrainMilliseconds;
            Stats.GpuDebugTimeMs = _gpuFrameTimer?.LatestDebugMilliseconds;
            var accountedTime = Stats.MutexWaitTimeMs + Stats.TileUpdateTimeMs +
                Stats.AssetUploadTimeMs + Stats.SceneRenderTimeMs;
            Stats.RenderOverheadTimeMs = Math.Max(
                0,
                Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds - accountedTime);

            _frameCount++;

            // calculate average FPS over the last _maxDeltaMS milliseconds
            double now = _fpsWatch.Elapsed.TotalSeconds;
            double delta = now - _lastFpsTime;

            if (delta >= _maxDeltaMS / 1000.0)
            {
                Stats.FPS = _frameCount / (delta);
                Stats.FrameTimeMs = 1000.0 / Stats.FPS;

                _lastFpsTime = now;
                _frameCount = 0;
            }
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;

            disposed = true;
            IsInitialized = false;
            _lifetimeCancellation.Cancel();

            if (_contentInitializationTask != null)
            {
                try
                {
                    await _contentInitializationTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            await Dx11CacheLifecycle.ResetAsync();

            _keyedMutex.Dispose();
            _gpuFrameTimer?.Dispose();
            _gpuFrameTimer = null;
            _sharedRTV.Dispose();
            SharedSRV.Dispose();
            sharedTexture.Dispose();
            sceneManager?.Dispose();
            shaderManager?.Dispose();
            imgui?.Dispose();
            _lifetimeCancellation.Dispose();
            SetStatus(WowViewerEngineState.Disposed, "Renderer disposed.");
        }

        private void LoadBuildInfo(string wowDirInput)
        {
            var buildInfoPath = Path.Combine(wowDirInput, ".build.info");

            var productList = new Dictionary<string, (string buildConfig, string cdnConfig)>();

            if (!Directory.Exists(wowDirInput) || !File.Exists(buildInfoPath))
            {
                Console.WriteLine("Invalid WoW directory or .build.info not found at " + buildInfoPath);
                return;
            }

            var buildInfo = File.ReadAllLines(buildInfoPath);

            var readFirstLine = false;
            foreach (var line in buildInfo)
            {
                if (!readFirstLine)
                {
                    readFirstLine = true;
                    continue;
                }
                var splitLine = line.Split('|');

                if (splitLine.Length <= 14)
                    continue;

                // TODO: Copy proper .build.info header parsing from WTL
                _productList[splitLine[14]] = (splitLine[2], splitLine[3]);
            }

            _products = _productList.Keys.ToArray();
            // _wowConfig.wowProduct = "wow";
            // optional, set first product as current if none is current yet
            // only if there's exactly one product for now to avoid not being able to switch
            if (_SetDefaultProduct && string.IsNullOrEmpty(_wowConfig.wowProduct) && _products.Length > 0)
            {
                _wowConfig.wowProduct = _products.First();
            }

            _currentProduct = Array.IndexOf(_products, _wowConfig.wowProduct);

            if (!string.IsNullOrEmpty(_wowConfig.wowProduct) && _currentProduct == -1)
            {
                Console.WriteLine("Error : The WoW product (" + _wowConfig.wowProduct + ") set in config could not be found in .build.info.");
            }

        }

        void LoadCurrentProduct()
        {
            if (_currentProduct != -1)
            {
                var selectedProduct = _productList.ElementAt(_currentProduct);
                _wowConfig.wowProduct = selectedProduct.Key;
                if (string.IsNullOrWhiteSpace(_wowConfig.buildConfig))
                    _wowConfig.buildConfig = selectedProduct.Value.buildConfig;
                if (string.IsNullOrWhiteSpace(_wowConfig.cdnConfig))
                    _wowConfig.cdnConfig = selectedProduct.Value.cdnConfig;
            }

            if (!string.IsNullOrWhiteSpace(_wowConfig.wowProduct))
                StartCASCInitialization();
            else
                SetStatus(WowViewerEngineState.Ready, "Renderer ready; select a WoW client to load content.");
        }

        private unsafe void RenderGizmo()
        {
            // TODO
        }

        private void StartCASCInitialization()
        {
            SetStatus(WowViewerEngineState.LoadingContent, $"Loading {_wowConfig.wowProduct} content...");
            _contentInitializationTask = Task.Run(async () =>
            {
                var cancellationToken = _lifetimeCancellation.Token;
                var gateEntered = false;
                try
                {
                    await ContentInitializationGate.WaitAsync(cancellationToken);
                    gateEntered = true;

                    var result = await Services.CASC.CreateBuildAsync(
                        _wowConfig.wowProduct,
                        _wowConfig.wowDir,
                        _wowConfig.buildConfig,
                        _wowConfig.cdnConfig,
                        cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    if (_generation != Volatile.Read(ref _activeGeneration))
                        return;

                    Services.CASC.Activate(result);

                    WowlibFileSystem.OpenForClient(_wowConfig.wowDir, _wowConfig.wowProduct);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_generation != Volatile.Read(ref _activeGeneration))
                        return;

                    sceneManager.GetCurrentWDT();
                    sceneManager.PreloadTEX();

                    SetStatus(WowViewerEngineState.Ready, $"{result.BuildName} ready.");
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    SetStatus(WowViewerEngineState.Failed, "WoW content initialization failed.", exception);
                }
                finally
                {
                    if (gateEntered)
                        ContentInitializationGate.Release();
                }
            });
        }

        private void SetStatus(WowViewerEngineState state, string message, Exception? exception = null)
        {
            Console.WriteLine($"Renderer status: {state} - {message}");
            Status = new WowViewerEngineStatus(state, message, exception);
            StatusChanged?.Invoke(this, Status);
        }

        public static Vector3 QuaternionToEuler(Quaternion q)
        {
            Vector3 euler;

            float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            euler.X = MathF.Atan2(sinr_cosp, cosr_cosp);

            float sinp = 2 * (q.W * q.Y - q.Z * q.X);
            if (MathF.Abs(sinp) >= 1)
                euler.Y = MathF.CopySign(MathF.PI / 2, sinp);
            else
                euler.Y = MathF.Asin(sinp);

            float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            euler.Z = MathF.Atan2(siny_cosp, cosy_cosp);

            return euler;
        }


        public void SetMovementSpeed(float speed)
        {
            movementSpeed = Math.Clamp(speed, 1f, 10_000f);
        }

        public void SetMouseSensitivity(float sensitivity)
        {
            Settings.MouseSensitivity = Math.Clamp(sensitivity, 0.001f, 2f);
        }

        public void ApplySettings(RendererSettings settings)
        {
            Settings = settings.Clone();
            SetMovementSpeed(Settings.MovementSpeed);
            SetMouseSensitivity(Settings.MouseSensitivity);

            if (activeCamera != null)
                activeCamera.FarPlane = Math.Max(Settings.TerrainRenderDistance, Settings.ModelRenderDistance);

            if (sceneManager != null)
            {
                sceneManager.TileLoadingDistance = Math.Clamp(Settings.TileLoadingDistance, 0, 32);
                sceneManager.TerrainRenderDistance = Settings.TerrainRenderDistance;
                sceneManager.ModelRenderDistance = Settings.ModelRenderDistance;
                sceneManager.MinimumModelScreenSizePixels = Math.Clamp(Settings.MinimumModelScreenSizePixels, 0f, 16f);
                sceneManager.TerrainLodTransitionPixels = Math.Clamp(Settings.TerrainLodTransitionPixels, 0f, 256f);
                sceneManager.RenderADT = Settings.RenderADT;
                sceneManager.RenderWMO = Settings.RenderWMO;
                sceneManager.RenderM2 = Settings.RenderM2;
                sceneManager.EnableWmoPortalCulling = Settings.EnableWmoPortalCulling;
                sceneManager.AmbientColor = Settings.AmbientColor;
                sceneManager.DiffuseColor = Settings.DiffuseColor;
                sceneManager.ShowBoundingBoxes = Settings.ShowBoundingBoxes;
                sceneManager.ShowBoundingSpheres = Settings.ShowBoundingSpheres;
                sceneManager.ShowTerrainGrid = Settings.ShowTerrainGrid;
                sceneManager.ShowTerrainWireframe = Settings.ShowTerrainWireframe;
            }
        }

        public void NavigateTo(uint wdtFileDataId, double tileX, double tileY, bool isGlobalWmo)
        {
            if (!IsInitialized || sceneManager == null || activeCamera == null)
                return;

            sceneManager.LoadWDT(wdtFileDataId);
            sceneManager.PreloadTEX();
            _pendingTerrainNavigation = null;

            if (isGlobalWmo && sceneManager.GetCurrentWDT()?.GlobalWmoExtents is { } extents)
            {
                // MODF ground X/Z map to renderer Y/X respectively; renderer Z is elevation.
                var position = new Vector3(-extents.Min.Z, -extents.Min.X, extents.Max.Y);
                var opposite = new Vector3(-extents.Max.Z, -extents.Max.X, extents.Min.Y);
                activeCamera.Position = position;
                activeCamera.SetDirection(opposite - position);
                return;
            }

            var clampedX = Math.Clamp(tileX, 0d, 63.999999d);
            var clampedY = Math.Clamp(tileY, 0d, 63.999999d);
            var worldX = (float)((32d - clampedY) * 533.333d);
            var worldY = (float)((32d - clampedX) * 533.333d);
            var tile = ((byte)Math.Floor(clampedX), (byte)Math.Floor(clampedY));
            _pendingTerrainNavigation = (wdtFileDataId, tile.Item1, tile.Item2, new Vector2(worldX, worldY));
            // Move immediately so this exact tile enters the loading queue.
            activeCamera.Position = new Vector3(worldX, worldY, activeCamera.Position.Z);
            if (sceneManager.TryGetTerrainTileMaxHeight(wdtFileDataId, tile.Item1, tile.Item2, out var height))
                OnTerrainTileHeightAvailable(new MapTile
                {
                    wdtFileDataID = wdtFileDataId,
                    tileX = tile.Item1,
                    tileY = tile.Item2
                }, height);
        }

        private void OnTerrainTileHeightAvailable(MapTile tile, float highestHeight)
        {
            if (_pendingTerrainNavigation is not { } navigation ||
                tile.wdtFileDataID != navigation.Wdt || tile.tileX != navigation.X || tile.tileY != navigation.Y)
                return;

            var center = SceneManager.GetTileCenterPosition(tile.tileX, tile.tileY);
            var horizontal = new Vector2(center.X - navigation.Position.X, center.Y - navigation.Position.Y);
            var direction = horizontal.LengthSquared() > float.Epsilon
                ? Vector3.Normalize(new Vector3(Vector2.Normalize(horizontal), -MathF.Tan(20f * MathF.PI / 180f)))
                : -Vector3.UnitZ;
            activeCamera.Position = new Vector3(navigation.Position, highestHeight + 10f);
            activeCamera.SetDirection(direction);
            _pendingTerrainNavigation = null;
        }

        public void SetHasFocus(bool focus)
        {
            hasFocus = focus;
        }

        #region Inputs
        private void HandleMouseLook(InputFrame input, bool gizmoInUse, float deltaTime)
        {
            // Handle mouse look with right click
            if (input.RightMouseDown && !gizmoInUse)
            {
                var currentMousePos = input.MousePosition;

                if (LastMousePosition == default)
                {
                    LastMousePosition = currentMousePos;
                }
                else
                {
                    var lookSensitivity = Settings.MouseSensitivity;
                    var xOffset = (currentMousePos.X - LastMousePosition.X) * lookSensitivity;
                    var yOffset = (currentMousePos.Y - LastMousePosition.Y) * lookSensitivity;
                    LastMousePosition = currentMousePos;
                    activeCamera.ModifyDirection(-xOffset, yOffset);
                }
            }
            else
            {
                LastMousePosition = default;
            }
        }

        private void HandleClickSelection(InputFrame input, bool gizmoInUse)
        {
            bool mouseDownThisFrame = input.LeftMouseDown;

            if (input.Mode != EditorModeId.Selection)
            {
                wasMouseDown = mouseDownThisFrame;
                MouseDownPosition = null;
                return;
            }

            if (mouseDownThisFrame && !wasMouseDown && !gizmoInUse)
            {
                MouseDownPosition = input.MousePosition;
            }

            if (!mouseDownThisFrame && wasMouseDown)
            {
                if (MouseDownPosition.HasValue)
                {
                    var dragDistance = Vector2.Distance(MouseDownPosition.Value, input.MousePosition);

                    if (dragDistance < 5.0f)
                    {
                        sceneManager.PerformRaycast(
                            input.MousePosition.X,
                            input.MousePosition.Y,
                            activeCamera,
                            viewportWidth,
                            viewportHeight
                        );
                    }
                }

                MouseDownPosition = null;
            }

            wasMouseDown = mouseDownThisFrame;
        }

        private void HandleKeyboardMovement(InputFrame input, float deltaTime)
        {
            if (!IsInitialized) return;
            float moveSpeed = movementSpeed * deltaTime;

            Vector3 currentPos = activeCamera.Position;
            float currentYaw = activeCamera.Yaw;

            if (input.KeysDown.Contains(Key.ShiftLeft))
                moveSpeed *= 2.0f;

            if (input.KeysDown.Contains(Key.ShiftLeft) && input.KeysDown.Contains(Key.ControlLeft))
                moveSpeed *= 4.0f;

            if (input.KeysDown.Contains(Key.AltLeft))
                moveSpeed *= 0.04f;

            var moveFront = Vector3.Normalize(new Vector3(activeCamera.Front.X, 0f, activeCamera.Front.Z));
            if (input.KeysDown.Contains(Key.W))
                activeCamera.Position += moveSpeed * activeCamera.Front;
            if (input.KeysDown.Contains(Key.S))
                activeCamera.Position -= moveSpeed * activeCamera.Front;
            if (input.KeysDown.Contains(Key.A))
                activeCamera.Position -= moveSpeed * activeCamera.Right;
            if (input.KeysDown.Contains(Key.D))
                activeCamera.Position += moveSpeed * activeCamera.Right;
            if (input.KeysDown.Contains(Key.Q))
                activeCamera.Position += moveSpeed * Vector3.UnitZ;
            if (input.KeysDown.Contains(Key.E))
                activeCamera.Position -= moveSpeed * Vector3.UnitZ;

            if (input.KeysDown.Contains(Key.I))
            {
                Console.WriteLine("ADT cache: " + ADTCache.GetCacheCount());
                Console.WriteLine("WMO cache: " + WMOCache.GetCacheCount());
                Console.WriteLine("M2 cache: " + M2Cache.GetCacheCount());
                Console.WriteLine("BLP cache: " + BLPCache.GetCacheCount());
            }

            if (input.KeysDown.Contains(Key.R))
            {
                var firstTile = sceneManager.GetFirstMapTile();
                var newPos = SceneManager.GetTileCenterPosition(firstTile.x, firstTile.y);
                activeCamera.Position = new Vector3(0, 0, 0);
            }

            if (input.KeysDown.Contains(Key.P))
                Console.WriteLine(activeCamera.Position);

            bool spacePressed = input.KeysDown.Contains(Key.Space);
            if (spacePressed && !wasSpacePressed && sceneManager.SelectedObject != null && !gizmoWasUsing)
            {
                // TODO: Gizmo
            }

            if(input.KeysDown.Contains(Key.Up))
                sceneManager.MoveSelectedObject(Vector3.UnitZ * moveSpeed);
            else if(input.KeysDown.Contains(Key.Down))
                sceneManager.MoveSelectedObject(-Vector3.UnitZ * moveSpeed);
            else if (input.KeysDown.Contains(Key.Left))
                sceneManager.MoveSelectedObject(-Vector3.UnitX * moveSpeed);
            else if (input.KeysDown.Contains(Key.Right))
                sceneManager.MoveSelectedObject(Vector3.UnitX * moveSpeed);
            else if(input.KeysDown.Contains(Key.Z))
                sceneManager.MoveSelectedObject(Vector3.UnitY * moveSpeed);
            else if (input.KeysDown.Contains(Key.X))
                sceneManager.MoveSelectedObject(-Vector3.UnitY * moveSpeed);

            //if (input.KeysDown.Contains(Key.ControlLeft) && input.KeysDown.Contains(Key.F))
            //    sceneManager.SaveManualJSON();

            wasSpacePressed = spacePressed;
        }
        #endregion
    }
}
