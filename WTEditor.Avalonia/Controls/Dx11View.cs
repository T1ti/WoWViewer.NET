using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.ViewModels;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Renderer;

namespace WTEditor.Avalonia.Controls
{
    public sealed class Dx11View : Control
    {
        // Composition callbacks arrive on a discrete cadence. Allow a small lead
        // so a callback that is fractionally early does not miss its slot. The
        // deadline remains phase-locked below, preventing this tolerance from
        // lowering the effective rate to every second compositor pulse.
        private const double FrameDueToleranceSeconds = 0.0015d;

        public static readonly StyledProperty<ViewportRenderActivity> RenderActivityProperty =
            AvaloniaProperty.Register<Dx11View, ViewportRenderActivity>(
                nameof(RenderActivity),
                ViewportRenderActivity.Foreground);

        private readonly Dx11RendererSession _rendererSession = new();
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

        private DXGI? _dxgi;
        private D3D11? _d3d11;
        private ComPtr<ID3D11Device> _device;
        private ComPtr<ID3D11DeviceContext> _deviceContext;

        private Compositor? _compositor;
        private CompositionSurfaceVisual? _surfaceVisual;
        private CompositionDrawingSurface? _surface;
        private ICompositionGpuInterop? _interop;
        private D3D11PresentationBufferPool? _presentationPool;

        private int _lastWidth;
        private int _lastHeight;

        private bool _initialized;

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly Stopwatch _telemetryWatch = Stopwatch.StartNew();
        private readonly ViewportFrameClock _frameClock = new(FrameDueToleranceSeconds);
        private double _last;
        private DispatcherTimer? _suspendedFrameTimer;
        private bool _suspendedFrameDue;

        private ViewModels.Editor3DViewModel? _vm;
        private RenderingConfiguration _renderingConfiguration = new();
        private int _renderFrameInProgress;
        private int _compositionUpdateQueued;
        private int _compositionUpdateGeneration = -1;
        private int _resizeInProgress;
        private bool _restartPending;
        private bool _cleanupPending;
        private bool _attached;
        private int _attachmentGeneration;
        private long _profileFrameNumber;
        private ClientConfiguration _clientConfiguration = new();
        private readonly AutomatedBenchmarkOptions _benchmarkOptions = AutomatedBenchmarkOptions.Current;
        private readonly AutomatedBenchmarkCoordinator? _benchmarkCoordinator;
        private int _lastBenchmarkStatusSecond = -1;
        private Container3D? _lastSelectedObject;
        private EditorObjectId _lastSelectedObjectId;
        private IEditorObjectData? _lastSelectedObjectData;
        private bool _lastSelectionHadListfile;
        private bool _terrainStrokeActive;
        private IUndoTransaction? _terrainStrokeTransaction;
        private bool? _lastPublishedTerrainDirtyState;

        public ViewportRenderActivity RenderActivity
        {
            get => GetValue(RenderActivityProperty);
            set => SetValue(RenderActivityProperty, value);
        }

        public Dx11View()
        {
            _rendererSession.StatusChanged += OnRendererStatusChanged;
            if (_benchmarkOptions.Enabled)
                _benchmarkCoordinator = new AutomatedBenchmarkCoordinator(_benchmarkOptions);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_vm != null)
            {
                _vm.ClientConfigurationChanged -= OnClientConfigurationChanged;
                _vm.RenderingConfigurationChanged -= OnRenderingConfigurationChanged;
                _vm.SelectedObjectTransformRequested -= OnSelectedObjectTransformRequested;
                _vm.SelectedWmoPlacementRequested -= OnSelectedWmoPlacementRequested;
                _vm.WorldNavigationRequested -= OnWorldNavigationRequested;
                _vm.TerrainChunkTexturesRequested -= OnTerrainChunkTexturesRequested;
                _vm.DominantTerrainTextureRequested -= OnDominantTerrainTextureRequested;
                _vm.CurrentTerrainTileTexturesRequested -= OnCurrentTerrainTileTexturesRequested;
            }

            base.OnDataContextChanged(e);
            _vm = DataContext as ViewModels.Editor3DViewModel;

            if (_vm != null)
            {
                _clientConfiguration = _vm.ClientConfiguration;
                _renderingConfiguration = _vm.RenderingConfiguration;
                _vm.ClientConfigurationChanged += OnClientConfigurationChanged;
                _vm.RenderingConfigurationChanged += OnRenderingConfigurationChanged;
                _vm.SelectedObjectTransformRequested += OnSelectedObjectTransformRequested;
                _vm.SelectedWmoPlacementRequested += OnSelectedWmoPlacementRequested;
                _vm.WorldNavigationRequested += OnWorldNavigationRequested;
                _vm.TerrainChunkTexturesRequested += OnTerrainChunkTexturesRequested;
                _vm.DominantTerrainTextureRequested += OnDominantTerrainTextureRequested;
                _vm.CurrentTerrainTileTexturesRequested += OnCurrentTerrainTileTexturesRequested;
                if (_benchmarkOptions.Enabled)
                {
                    _vm.IsDetailedGpuProfilingEnabled = true;
                    _vm.UpdateAutomatedBenchmarkWaitingStatus(
                        TimeSpan.Zero,
                        stableFrames: 0,
                        _benchmarkOptions.StableFrameCount);
                }
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            var generation = ++_attachmentGeneration;
            // Focusable = true;
            // Focus();
            InitializeAsync(generation);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _attached = false;
            _attachmentGeneration++;
            BeginCleanup();
        }

        private async void InitializeAsync(int generation)
        {
            var compositionVisual = ElementComposition.GetElementVisual(this);
            if (compositionVisual == null)
                return;

            _compositor = compositionVisual.Compositor;

            _interop = await _compositor.TryGetCompositionGpuInterop();
            if (!_attached || generation != _attachmentGeneration)
                return;

            if (_interop == null)
            {
                Console.WriteLine("Dx11View: ICompositionGpuInterop not available on this platform/backend.");
                _vm?.UpdateRendererStatus(new RendererStatus(
                    RendererLifecycleState.Failed,
                    "The active Avalonia backend cannot share GPU images with the renderer."));
                return;
            }

            if (!_interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
            {
                Console.WriteLine("Dx11View: D3D11 shared texture handle import not supported by compositor.");
                _vm?.UpdateRendererStatus(new RendererStatus(
                    RendererLifecycleState.Failed,
                    "The active compositor does not support D3D11 shared textures."));
                return;
            }

            await _lifecycleLock.WaitAsync();
            try
            {
                if (!_attached || generation != _attachmentGeneration)
                    return;

                CreateD3DDevice();

                try
                {
                    CreateEngine(_clientConfiguration);
                }
                catch (Exception exception)
                {
                    _vm?.UpdateRendererStatus(new RendererStatus(
                        RendererLifecycleState.Failed,
                        "Unable to initialize the renderer.",
                        exception.Message));
                    return;
                }

                _surface = _compositor.CreateDrawingSurface();
                _surfaceVisual = _compositor.CreateSurfaceVisual();
                _surfaceVisual.Surface = _surface;
                _surfaceVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
                _surfaceVisual.Scale = new Vector3(1, -1, 1);
                _surfaceVisual.CenterPoint = new Vector3(0, (float)Bounds.Height / 2f, 0);

                var (width, height) = GetPhysicalSize();
                _presentationPool = await D3D11PresentationBufferPool.CreateAsync(
                    _device, _deviceContext, _interop, width, height);
                _lastWidth = width;
                _lastHeight = height;
                ResetFrameClock();

                ElementComposition.SetElementChildVisual(this, _surfaceVisual);

                _initialized = true;
                QueueNextRenderFrame();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private void OnClientConfigurationChanged(object? sender, ClientConfiguration configuration)
        {
            _clientConfiguration = configuration;
            Dispatcher.UIThread.Post(RestartEngine, DispatcherPriority.Render);
        }

        private async void RestartEngine()
        {
            if (_dxgi == null || !_attached)
                return;

            if (Volatile.Read(ref _renderFrameInProgress) != 0 ||
                Volatile.Read(ref _resizeInProgress) != 0)
            {
                _restartPending = true;
                return;
            }

            if (!_initialized)
                return;

            await _lifecycleLock.WaitAsync();
            try
            {
                _initialized = false;
                await DisposePresentationResourcesAsync();
                if (_terrainStrokeActive && _rendererSession.Engine is { } activeEngine)
                    CompleteTerrainStroke(activeEngine);
                await _rendererSession.DisposeAsync();

                if (!_attached || _dxgi == null)
                    return;

                try
                {
                    CreateEngine(_clientConfiguration);
                    var (width, height) = GetPhysicalSize();
                    _presentationPool = await D3D11PresentationBufferPool.CreateAsync(
                        _device, _deviceContext, _interop!, width, height);
                    _lastWidth = width;
                    _lastHeight = height;
                    ResetFrameClock();
                    _initialized = true;
                    QueueNextRenderFrame();
                }
                catch (Exception exception)
                {
                    _vm?.UpdateRendererStatus(new RendererStatus(
                        RendererLifecycleState.Failed,
                        "Unable to restart the renderer.",
                        exception.Message));
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private void OnRenderingConfigurationChanged(object? sender, RenderingConfiguration configuration)
        {
            _renderingConfiguration = configuration;
            ResetFrameClock();
            if (_initialized && _attached && RenderActivity != ViewportRenderActivity.Suspended)
                RequestRenderFrame();

            if (_rendererSession.Engine == null)
                return;

            Dispatcher.UIThread.Post(
                () => _rendererSession.Engine?.ApplySettings(_renderingConfiguration.ToDx11()),
                DispatcherPriority.Render);
        }

        private void CreateEngine(ClientConfiguration configuration)
        {
            var (width, height) = GetPhysicalSize();
            _rendererSession.Initialize(
                configuration,
                _renderingConfiguration,
                _dxgi!,
                _device,
                _deviceContext,
                new Vector2D<int>(width, height),
                _vm?.HasInitialCameraPosition == true ? _vm.InitialCameraPosition : null,
                _vm?.HasInitialCameraDirection == true ? _vm.InitialCameraDirection : null);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property != RenderActivityProperty)
                return;

            ResetFrameClock();
            if (RenderActivity == ViewportRenderActivity.Suspended)
            {
                _suspendedFrameDue = false;
                ScheduleSuspendedFrame();
            }
            else
            {
                StopSuspendedFrameTimer();
                if (_initialized && _attached)
                    RequestRenderFrame();
            }
        }

        private (int Width, int Height) GetPhysicalSize()
        {
            var renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
            if (!double.IsFinite(renderScaling) || renderScaling <= 0)
                renderScaling = 1d;

            return (
                Math.Max(1, (int)Math.Ceiling(Math.Max(0, Bounds.Width) * renderScaling)),
                Math.Max(1, (int)Math.Ceiling(Math.Max(0, Bounds.Height) * renderScaling)));
        }

        private void UpdateSurfaceVisualLayout()
        {
            if (_surfaceVisual == null)
                return;

            if (_surfaceVisual.Size.X != (float)Bounds.Width ||
                _surfaceVisual.Size.Y != (float)Bounds.Height)
            {
                _surfaceVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
                _surfaceVisual.CenterPoint = new Vector3(0, (float)Bounds.Height / 2f, 0);
            }
        }

        private void BeginResize(int width, int height)
        {
            if (Interlocked.CompareExchange(ref _resizeInProgress, 1, 0) != 0)
                return;

            _initialized = false;
            _ = ResizeAsync(width, height);
        }

        private async Task ResizeAsync(int width, int height)
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                if (!_attached || _interop == null || _d3d11 == null)
                    return;

                await DisposePresentationResourcesAsync();
                var engine = _rendererSession.Engine;
                if (engine == null)
                    return;

                engine.Resize((uint)width, (uint)height);
                _presentationPool = await D3D11PresentationBufferPool.CreateAsync(
                    _device, _deviceContext, _interop, width, height);
                _lastWidth = width;
                _lastHeight = height;
                ResetFrameClock();
                _initialized = true;
            }
            catch (Exception exception)
            {
                _vm?.UpdateRendererStatus(new RendererStatus(
                    RendererLifecycleState.Failed,
                    "Unable to resize the renderer.",
                    exception.Message));
            }
            finally
            {
                _lifecycleLock.Release();
                Volatile.Write(ref _resizeInProgress, 0);
                if (_initialized && _attached)
                    QueueNextRenderFrame();
            }
        }

        private async Task DisposePresentationResourcesAsync()
        {
            var pool = _presentationPool;
            _presentationPool = null;
            if (pool != null)
                await pool.DisposeAsync();
        }

        private unsafe void CreateD3DDevice()
        {
            _dxgi = DXGI.GetApi(null, false);
            _d3d11 = D3D11.GetApi(null, false);
            var deviceCreationFlags = Dx11RuntimeOptions.IsDebugLayerRequested
                ? (uint)CreateDeviceFlag.Debug
                : 0;

            // Avalonia may select a different adapter when more than one GPU is
            // present. Importing a texture created on another adapter is undefined,
            // so prefer the compositor's LUID and retain the default hardware path
            // as a fallback for backends that do not expose one.
            ComPtr<IDXGIAdapter> adapter = default;
            ComPtr<IDXGIFactory4> adapterFactory = default;
            try
            {
                var luidBytes = _interop?.DeviceLuid;
                if (luidBytes is { Length: 8 })
                {
                    var luid = default(Luid);
                    luid.Low = BitConverter.ToUInt32(luidBytes, 0);
                    luid.High = BitConverter.ToInt32(luidBytes, 4);
                    try
                    {
                        adapterFactory = _dxgi.CreateDXGIFactory1<IDXGIFactory4>();
                        if (adapterFactory.EnumAdapterByLuid<IDXGIAdapter>(luid, out adapter) != 0)
                            adapter = default;
                    }
                    catch
                    {
                        adapter.Dispose();
                        adapter = default;
                    }
                }

                var driverType = adapter.Handle != null
                    ? D3DDriverType.Unknown
                    : D3DDriverType.Hardware;
                var createResult = _d3d11.CreateDevice(
                    adapter,
                    driverType,
                    Software: default,
                    deviceCreationFlags,
                    null,
                    0,
                    D3D11.SdkVersion,
                    ref _device,
                    null,
                    ref _deviceContext);
                if (createResult != 0 && adapter.Handle != null)
                {
                    adapter.Dispose();
                    adapter = default;
                    createResult = _d3d11.CreateDevice(
                        default(ComPtr<IDXGIAdapter>),
                        D3DDriverType.Hardware,
                        Software: default,
                        deviceCreationFlags,
                        null,
                        0,
                        D3D11.SdkVersion,
                        ref _device,
                        null,
                        ref _deviceContext);
                }

                SilkMarshal.ThrowHResult(createResult);
            }
            finally
            {
                adapter.Dispose();
                adapterFactory.Dispose();
            }
        }

        private void RequestRenderFrame()
        {
            var compositor = _compositor;
            if (!_attached || !_initialized || compositor == null)
                return;

            var generation = Volatile.Read(ref _attachmentGeneration);
            if (Interlocked.CompareExchange(ref _compositionUpdateQueued, 1, 0) != 0)
                return;

            Volatile.Write(ref _compositionUpdateGeneration, generation);

            try
            {
                compositor.RequestCompositionUpdate(() =>
                {
                    // A callback from a detached visual can run after a new
                    // attachment has already queued its first update. Only clear
                    // the queue state if this callback still owns that state.
                    var ownsQueue = Volatile.Read(ref _compositionUpdateGeneration) == generation;
                    if (ownsQueue)
                        Interlocked.Exchange(ref _compositionUpdateQueued, 0);

                    if (_attached &&
                        _initialized &&
                        Volatile.Read(ref _attachmentGeneration) == generation)
                    {
                        RenderFrame();
                    }
                    else if (_attached && _initialized)
                    {
                        // The old callback was stale, but a fresh attachment is
                        // ready. Requeue it unless its callback is already pending.
                        RequestRenderFrame();
                    }
                });
            }
            catch
            {
                if (Volatile.Read(ref _compositionUpdateGeneration) == generation)
                    Interlocked.Exchange(ref _compositionUpdateQueued, 0);
                throw;
            }
        }

        private void ResetFrameClock()
        {
            _last = _sw.Elapsed.TotalSeconds;
            _frameClock.Reset();
        }

        private void RenderFrame()
        {
            var engine = _rendererSession.Engine;
            if (!_initialized || engine == null || _interop == null || _surface == null || _surfaceVisual == null)
                return;

            if (_interop.IsLost)
            {
                _vm?.UpdateRendererStatus(new RendererStatus(
                    RendererLifecycleState.Failed,
                    "The compositor GPU device was lost; stopping the viewport."));
                BeginCleanup();
                return;
            }

            if (RenderActivity == ViewportRenderActivity.Suspended && !_suspendedFrameDue)
                return;

            if (Interlocked.CompareExchange(ref _renderFrameInProgress, 1, 0) != 0)
                return;

            try
            {
                _suspendedFrameDue = false;
                RenderFrameCore(engine);
            }
            catch (Exception exception)
            {
                _initialized = false;
                _vm?.UpdateRendererStatus(new RendererStatus(
                    RendererLifecycleState.Failed,
                    "Rendering stopped after an error.",
                    exception.Message));
            }
            finally
            {
                Volatile.Write(ref _renderFrameInProgress, 0);

                if (_cleanupPending)
                {
                    _cleanupPending = false;
                    _ = CleanupAsync();
                }
                else if (_restartPending)
                {
                    _restartPending = false;
                    RestartEngine();
                }
                else if (_initialized)
                {
                    QueueNextRenderFrame();
                }
            }
        }

        private void RenderFrameCore(WowViewerEngine engine)
        {
            if (_interop == null || _surface == null || _surfaceVisual == null)
                return;

            double now = _sw.Elapsed.TotalSeconds;

            var (width, height) = GetPhysicalSize();
            UpdateSurfaceVisualLayout();

            if (width != _lastWidth || height != _lastHeight)
            {
                // Do not carry time spent waiting for a resize into the next
                // simulation update. ResizeAsync resets the clock again once the
                // replacement presentation pool is ready.
                ResetFrameClock();
                BeginResize(width, height);
                return;
            }

            var frameInterval = ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                _renderingConfiguration.ViewportFrameRateLimit,
                RenderActivity,
                _renderingConfiguration.IsForegroundFrameRateLimitEnabled);
            if (!_frameClock.IsFrameDue(now, frameInterval))
                return;

            var presentationPool = _presentationPool;
            var presentationBuffer = presentationPool?.TryAcquire();
            if (presentationBuffer == null)
                return;

            // A full pool means the compositor is still consuming all three
            // images. Keep the simulation interval intact until a frame can be
            // rendered instead of advancing the clock for a dropped frame.
            var delta = _last > 0 ? Math.Max(0, now - _last) : 0;
            _last = now;

            var inputStarted = Stopwatch.GetTimestamp();
            var inputFrame = BuildInputFrame();
            var inputMilliseconds = Stopwatch.GetElapsedTime(inputStarted).TotalMilliseconds;
            var terrainStrokeRequested = inputFrame.Mode == EditorModeId.Terrain &&
                                         inputFrame.LeftMouseDown &&
                                         inputFrame.Modifiers != InputModifiers.None;
            if (terrainStrokeRequested && !_terrainStrokeActive && _vm != null)
            {
                engine.BeginTerrainStroke();
                _terrainStrokeTransaction = _vm.BeginEditAction("Terrain stroke");
                _terrainStrokeActive = true;
            }

            var engineFrameStarted = Stopwatch.GetTimestamp();
            engine.Update(delta, inputFrame);

            if (_terrainStrokeActive && !terrainStrokeRequested)
                CompleteTerrainStroke(engine);

            PublishSelection(engine.SelectedObject);
            PublishTerrainDirtyState(engine, includeSnapshots: false);
            engine.DetailedGpuProfilingEnabled = _vm?.IsDetailedGpuProfilingEnabled == true;
            var elapsedBeforeRenderMilliseconds = inputMilliseconds +
                Stopwatch.GetElapsedTime(engineFrameStarted).TotalMilliseconds;
            var estimatedRenderMilliseconds = engine.Stats.MutexWaitTimeMs +
                                               engine.Stats.TileUpdateTimeMs +
                                               engine.Stats.SceneRenderTimeMs +
                                               engine.Stats.RenderOverheadTimeMs;
            var streamingBudgetMilliseconds = StreamingFrameBudget.CalculateMilliseconds(
                frameInterval,
                elapsedBeforeRenderMilliseconds,
                estimatedRenderMilliseconds,
                hasPendingWork: engine.Stats.PendingAssetOperations > 0);
            try
            {
                engine.RenderTo(
                    delta,
                    presentationBuffer.RenderTargetView,
                    streamingBudgetMilliseconds);
            }
            catch
            {
                presentationBuffer.AbandonProducer();
                throw;
            }
            var engineFrameMilliseconds = Stopwatch.GetElapsedTime(engineFrameStarted).TotalMilliseconds;

            presentationBuffer.ReleaseProducer();
            try
            {
                var presentTask = _surface.UpdateWithKeyedMutexAsync(
                    presentationBuffer.ImportedImage!, acquireIndex: 1, releaseIndex: 0);
                presentationBuffer.SetLastPresent(presentTask);
                _frameClock.MarkFramePresented(now, frameInterval);
            }
            catch
            {
                // Do not put a texture whose consumer ownership is unknown back into
                // the pool. It will be drained and recreated with the next lifecycle.
                presentationBuffer.MarkPresentationFailed();
                throw;
            }

            if (_vm != null)
            {
                // Scalar bindings and camera persistence are UI-facing work. Keep
                // them at approximately 10 Hz while retaining every frame's profile
                // sample below for capture/benchmark consumers.
                if (_telemetryWatch.ElapsedMilliseconds >= 100)
                {
                    _telemetryWatch.Restart();
                    _vm.UpdateTelemetry(new ViewportTelemetry(
                        engine.Stats.FPS,
                        engineFrameMilliseconds,
                        engine.activeCamera?.Position ?? Vector3.Zero,
                        engine.activeCamera?.Front ?? Vector3.Zero,
                        (int)engine.Stats.DrawCalls,
                        checked((long)engine.Stats.SubmittedTriangleCount),
                        delta * 1_000d,
                        engine.CurrentWdtFileDataId));
                }

                var gpuUploadMilliseconds = engine.Stats.GpuUploadTimeMs ?? 0;
                var gpuWorldModelMilliseconds = engine.Stats.GpuWorldModelTimeMs ?? 0;
                var gpuDoodadMilliseconds = engine.Stats.GpuDoodadTimeMs ?? 0;
                var gpuTerrainMilliseconds = engine.Stats.GpuTerrainTimeMs ?? 0;
                var gpuDebugMilliseconds = engine.Stats.GpuDebugTimeMs ?? 0;
                var hasDetailedGpuTiming =
                    engine.Stats.GpuWorldModelTimeMs.HasValue ||
                    engine.Stats.GpuDoodadTimeMs.HasValue ||
                    engine.Stats.GpuTerrainTimeMs.HasValue ||
                    engine.Stats.GpuDebugTimeMs.HasValue;
                var gpuOtherMilliseconds = Math.Max(
                    0,
                    (engine.Stats.GpuFrameTimeMs ?? 0) - gpuUploadMilliseconds -
                    (hasDetailedGpuTiming
                        ? gpuWorldModelMilliseconds + gpuDoodadMilliseconds +
                          gpuTerrainMilliseconds + gpuDebugMilliseconds
                        : engine.Stats.GpuDrawTimeMs ?? 0));
                var profiledSceneCpuMilliseconds =
                    engine.Stats.SceneSetupTimeMs +
                    engine.Stats.TileHierarchyCullingTimeMs +
                    engine.Stats.WmoCullingTimeMs + engine.Stats.WmoSubmissionTimeMs +
                    engine.Stats.M2CullingTimeMs + engine.Stats.M2SubmissionTimeMs +
                    engine.Stats.TerrainCullingTimeMs + engine.Stats.TerrainSubmissionTimeMs +
                    engine.Stats.DebugSubmissionTimeMs;
                var sceneSetupDebugAndOtherMilliseconds =
                    engine.Stats.SceneSetupTimeMs + engine.Stats.DebugSubmissionTimeMs +
                    Math.Max(0, engine.Stats.SceneRenderTimeMs - profiledSceneCpuMilliseconds);
                var steps = new List<FrameTimingStep>
                {
                    new("World streaming (CPU)", engine.Stats.TileUpdateTimeMs),
                    new("Resource upload submission (CPU)", engine.Stats.AssetUploadTimeMs),
                    new("Tile hierarchy culling (CPU)", engine.Stats.TileHierarchyCullingTimeMs),
                    new("WMO culling (CPU)", engine.Stats.WmoCullingTimeMs),
                    new("WMO command submission (CPU)", engine.Stats.WmoSubmissionTimeMs),
                    new("M2 culling (CPU)", engine.Stats.M2CullingTimeMs),
                    new("M2 command submission (CPU)", engine.Stats.M2SubmissionTimeMs),
                    new("Terrain culling (CPU)", engine.Stats.TerrainCullingTimeMs),
                    new("Terrain command submission (CPU)", engine.Stats.TerrainSubmissionTimeMs),
                    new("Scene setup / debug (CPU)", sceneSetupDebugAndOtherMilliseconds),
                    new("Other frame work (CPU)", inputMilliseconds + engine.Stats.UpdateTimeMs + engine.Stats.RenderOverheadTimeMs),
                    new("Resource uploads (GPU timeline)", gpuUploadMilliseconds, FrameTimingDomain.Gpu)
                };
                if (hasDetailedGpuTiming)
                {
                    steps.Add(new("WMO span (GPU timeline)", gpuWorldModelMilliseconds, FrameTimingDomain.Gpu));
                    steps.Add(new("M2 span (GPU timeline)", gpuDoodadMilliseconds, FrameTimingDomain.Gpu));
                    steps.Add(new("Terrain span (GPU timeline)", gpuTerrainMilliseconds, FrameTimingDomain.Gpu));
                    steps.Add(new("Debug span (GPU timeline)", gpuDebugMilliseconds, FrameTimingDomain.Gpu));
                }
                else
                {
                    steps.Add(new("World span (GPU timeline)", engine.Stats.GpuDrawTimeMs ?? 0, FrameTimingDomain.Gpu));
                }
                steps.Add(new("Other GPU timeline", gpuOtherMilliseconds, FrameTimingDomain.Gpu));
                steps.Add(new("Producer mutex wait", engine.Stats.MutexWaitTimeMs, FrameTimingDomain.Presentation));
                var profileSnapshot = new FrameProfileSnapshot(
                    ++_profileFrameNumber,
                    DateTimeOffset.UtcNow,
                    delta * 1_000d,
                    inputMilliseconds + engine.Stats.CpuFrameTimeMs,
                    engine.Stats.GpuFrameTimeMs,
                    steps,
                    (int)engine.Stats.DrawCalls,
                    checked((long)engine.Stats.SubmittedIndexCount),
                    engine.Stats.PendingAssetOperations)
                {
                    EngineFrameMilliseconds = engineFrameMilliseconds,
                    StreamingBudgetMilliseconds = streamingBudgetMilliseconds,
                    UploadedResources = engine.Stats.UploadedResources,
                    ViewportWidth = width,
                    ViewportHeight = height,
                    Culling = new CullingMetrics(
                        engine.Stats.VisibleTerrainChunks,
                        engine.Stats.CandidateTerrainChunks,
                        engine.Stats.VisibleWorldModels,
                        engine.Stats.CandidateWorldModels,
                        engine.Stats.VisibleDoodads,
                        engine.Stats.CandidateDoodads,
                        engine.Stats.SizeCulledWorldModels,
                        engine.Stats.SizeCulledDoodads,
                        engine.Stats.FarLodTerrainChunks,
                        engine.Stats.CandidateTiles,
                        engine.Stats.CoarseCulledTiles,
                        engine.Stats.PortalCulledWmoGroups,
                        engine.Stats.PortalCulledDoodads,
                        engine.Stats.TraversedWmoPortalReferences),
                    RenderWorkload = new RenderWorkloadMetrics(
                        new RenderPassMetrics[]
                        {
                            new(
                                "World models (WMO)",
                                engine.Stats.WmoCullingTimeMs,
                                engine.Stats.WmoSubmissionTimeMs,
                                engine.Stats.GpuWorldModelTimeMs,
                                (int)engine.Stats.WmoDrawCalls,
                                (int)engine.Stats.WmoSubmittedInstances,
                                "instance-batch submissions",
                                checked((long)engine.Stats.WmoSubmittedIndices)),
                            new(
                                "Doodads (M2)",
                                engine.Stats.M2CullingTimeMs,
                                engine.Stats.M2SubmissionTimeMs,
                                engine.Stats.GpuDoodadTimeMs,
                                (int)engine.Stats.M2DrawCalls,
                                (int)engine.Stats.M2SubmittedInstances,
                                "instance-batch submissions",
                                checked((long)engine.Stats.M2SubmittedIndices)),
                            new(
                                "Terrain (ADT)",
                                engine.Stats.TerrainCullingTimeMs,
                                engine.Stats.TerrainSubmissionTimeMs,
                                engine.Stats.GpuTerrainTimeMs,
                                (int)engine.Stats.TerrainDrawCalls,
                                (int)engine.Stats.TerrainSubmittedChunks,
                                "visible chunks",
                                checked((long)engine.Stats.TerrainSubmittedIndices))
                        },
                        (int)engine.Stats.InstanceBufferMapCalls,
                        (int)engine.Stats.ConstantBufferUpdates,
                        (int)engine.Stats.TextureBindingCalls,
                        (int)engine.Stats.BlendStateBindings,
                        (int)engine.Stats.VertexBufferBindings,
                        (int)engine.Stats.IndexBufferBindings),
                    AssetStreaming = new AssetStreamingProfile(
                        ToProfile(engine.Stats.AssetStreaming.Adt),
                        ToProfile(engine.Stats.AssetStreaming.Blp),
                        ToProfile(engine.Stats.AssetStreaming.M2),
                        ToProfile(engine.Stats.AssetStreaming.Wmo))
                };
                _vm.UpdatePerformanceProfile(profileSnapshot);
                UpdateAutomatedBenchmark(profileSnapshot);
            }
        }

        private static AssetPipelineProfile ToProfile(
            WoWRenderLib.DX11.Streaming.AssetPipelineMetrics metrics) => new(
                metrics.Pending,
                metrics.Active,
                metrics.Completed,
                metrics.Skipped,
                metrics.Failed,
                metrics.LastProcessingMilliseconds,
                metrics.MaximumProcessingMilliseconds);

        private void QueueNextRenderFrame()
        {
            if (RenderActivity == ViewportRenderActivity.Suspended)
            {
                ScheduleSuspendedFrame();
                return;
            }

            RequestRenderFrame();
        }

        private void ScheduleSuspendedFrame()
        {
            if (!_attached || !_initialized || _suspendedFrameTimer?.IsEnabled == true)
                return;

            _suspendedFrameTimer ??= new DispatcherTimer();
            _suspendedFrameTimer.Interval = TimeSpan.FromSeconds(1);
            _suspendedFrameTimer.Tick -= OnSuspendedFrameTimerTick;
            _suspendedFrameTimer.Tick += OnSuspendedFrameTimerTick;
            _suspendedFrameTimer.Start();
        }

        private void StopSuspendedFrameTimer()
        {
            if (_suspendedFrameTimer != null)
                _suspendedFrameTimer.Stop();
        }

        private void OnSuspendedFrameTimerTick(object? sender, EventArgs e)
        {
            StopSuspendedFrameTimer();
            if (!_attached || !_initialized || RenderActivity != ViewportRenderActivity.Suspended)
                return;

            _suspendedFrameDue = true;
            RequestRenderFrame();
        }

        private void PublishSelection(Container3D? selectedObject)
        {
            if (_vm == null)
                return;

            if (selectedObject == null)
            {
                _lastSelectedObject = null;
                _vm.UpdateSelectedObject(null);
                return;
            }

            if (!ReferenceEquals(_lastSelectedObject, selectedObject))
            {
                _lastSelectedObject = selectedObject;
                _lastSelectedObjectId = EditorObjectId.New();
                _lastSelectedObjectData = CreateObjectData(selectedObject);
                _lastSelectionHadListfile = WoWRenderLib.Listfile.IsLoaded;
            }
            else if (!_lastSelectionHadListfile && WoWRenderLib.Listfile.IsLoaded)
            {
                _lastSelectedObjectData = CreateObjectData(selectedObject);
                _lastSelectionHadListfile = true;
            }
            else if (selectedObject is WMOContainer selectedWmo &&
                     _lastSelectedObjectData is WorldModelObjectData { IsLoaded: false } &&
                     selectedWmo.IsLoaded)
            {
                _lastSelectedObjectData = CreateObjectData(selectedObject);
            }
            else if (selectedObject is ADTContainer selectedAdt &&
                     _lastSelectedObjectData is TerrainObjectData terrainData &&
                     terrainData.IsModified != selectedAdt.IsModified)
            {
                _lastSelectedObjectData = CreateObjectData(selectedAdt);
            }

            var rotation = selectedObject.Rotation * (MathF.PI / 180f);
            var transform = new ObjectTransform(
                selectedObject.Position,
                Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z),
                new Vector3(selectedObject.Scale));

            var snapshot = new EditorObjectSnapshot(
                _lastSelectedObjectId,
                GetObjectDisplayName(selectedObject, _lastSelectedObjectData),
                selectedObject switch
                {
                    M2Container => "M2 model",
                    WMOContainer => "World model",
                    ADTContainer => "Terrain tile",
                    _ => selectedObject.GetType().Name
                },
                transform,
                _lastSelectedObjectData);

            _vm.UpdateSelectedObject(snapshot);
        }

        private static IEditorObjectData? CreateObjectData(Container3D selectedObject) => selectedObject switch
        {
            M2Container m2 => CreateM2ObjectData(m2),
            WMOContainer wmo => CreateWorldModelObjectData(wmo),
            ADTContainer adt => new TerrainObjectData(
                adt.FileDataId,
                adt.mapTile.tileX,
                adt.mapTile.tileY,
                adt.IsLoaded,
                adt.IsModified),
            _ => null
        };

        private static M2ObjectData CreateM2ObjectData(M2Container m2)
        {
            try
            {
                var model = m2.GetM2();
                var textureDetails = model.mats.Select((texture, index) => new ModelTextureData(
                    index,
                    new AssetReference(texture.fileDataID, WoWRenderLib.Listfile.GetDisplayName(texture.fileDataID)),
                    (uint)texture.flags)).ToArray();
                var materials = model.submeshes.Select((batch, index) => new ModelMaterialData(
                    index,
                    batch.blendType,
                    batch.renderFlags,
                    string.Empty,
                    EnumName<ShaderEnums.M2VertexShader>(batch.vertexShaderID),
                    EnumName<ShaderEnums.M2PixelShader>(batch.pixelShaderID),
                    SafeAssets(() => batch.material),
                    TextureSlots: SafeM2TextureSlots(() => batch.textureIndices, textureDetails))).ToArray();
                var batches = model.submeshes.Select((batch, index) => new ModelBatchData(
                    index,
                    null,
                    index,
                    batch.firstFace,
                    batch.numFaces,
                    batch.blendType,
                    batch.renderFlags,
                    string.Empty,
                    EnumName<ShaderEnums.M2VertexShader>(batch.vertexShaderID),
                    EnumName<ShaderEnums.M2PixelShader>(batch.pixelShaderID),
                    SafeAssets(() => batch.material))).ToArray();
                var enabledGeosets = m2.EnabledGeosets;
                var geosets = model.geosets.Select((geoset, index) => new ModelGeosetData(
                    index,
                    geoset.id,
                    GetGeosetType(geoset.id),
                    geoset.level,
                    geoset.firstVertex,
                    geoset.vertexCount,
                    geoset.firstIndex,
                    geoset.indexCount,
                    index < enabledGeosets.Length && enabledGeosets[index])).ToArray();
                return new M2ObjectData(
                    m2.FileDataId,
                    m2.ParentFileDataId,
                    m2.EnabledGeosets.Length,
                    m2.ParentWMO != null,
                    WoWRenderLib.Listfile.GetDisplayName(m2.FileDataId),
                    SafeAssets(() => model.mats.Select(material => material.fileDataID)),
                    m2.ParentWMO == null && m2.UniqueID != 0 ? m2.UniqueID : null,
                    WoWRenderLib.Listfile.GetDisplayName(m2.ParentFileDataId),
                    new ModelAdvancedData(
                        model.submeshes?.Length ?? 0,
                        model.vertexCount,
                        model.indexCount / 3,
                        model.animationCount,
                        model.particleEmitterCount,
                        model.boneCount,
                        model.attachmentCount),
                    m2.ParentWMO == null
                        ? new MapPlacementData(MapPlacementKind.Mddf, m2.UniqueID, m2.PlacementFlags)
                        : null,
                    materials,
                    batches,
                    geosets,
                    textureDetails);
            }
            catch
            {
                return new M2ObjectData(
                    m2.FileDataId,
                    m2.ParentFileDataId,
                    0,
                    m2.ParentWMO != null,
                    WoWRenderLib.Listfile.GetDisplayName(m2.FileDataId),
                    [],
                    m2.ParentWMO == null && m2.UniqueID != 0 ? m2.UniqueID : null,
                    WoWRenderLib.Listfile.GetDisplayName(m2.ParentFileDataId),
                    null,
                    m2.ParentWMO == null
                        ? new MapPlacementData(MapPlacementKind.Mddf, m2.UniqueID, m2.PlacementFlags)
                        : null);
            }
        }

        private static WorldModelObjectData CreateWorldModelObjectData(WMOContainer wmo)
        {
            if (!wmo.IsLoaded)
                return CreateUnloadedWorldModelObjectData(wmo);

            try
            {
                return ProjectLoadedWorldModelObjectData(
                    wmo.GetWMO(),
                    wmo.FileDataId,
                    wmo.ParentFileDataId,
                    wmo.UniqueID,
                    wmo.PlacementFlags,
                    wmo.PlacementDoodadSet,
                    wmo.PlacementNameSet,
                    wmo.ActiveDoodads.Count);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Unable to project loaded WMO {wmo.FileDataId} for the inspector: {exception}");
                Console.Error.WriteLine($"Unable to project loaded WMO {wmo.FileDataId} for the inspector: {exception.Message}");
                return CreateUnloadedWorldModelObjectData(wmo);
            }
        }

        internal static WorldModelObjectData ProjectLoadedWorldModelObjectData(
            WoWRenderLib.DX11.Structs.WorldModel model,
            uint fileDataId,
            uint parentFileDataId,
            uint uniqueId,
            ushort placementFlags,
            ushort placementDoodadSet,
            ushort placementNameSet,
            int activeDoodadCount)
        {
                var preppedMaterials = model.preppedMats ?? [];
                var materials = preppedMaterials.Select((material, index) => new ModelMaterialData(
                    index,
                    material.BlendMode,
                    material.Flags,
                    EnumName<MOMTShader>((uint)material.Shader),
                    material.VertexShader.ToString(),
                    material.PixelShader.ToString(),
                    SafeAssets(() => GetWmoTextureIds(material)),
                    Color1: material.Color1,
                    Color1B: material.Color1B,
                    Color2: material.Color2,
                    Color3: material.Color3,
                    GroundType: material.GroundType,
                    ExtendedFlags: material.Flags3,
                    TextureSlots: GetWmoTextureSlots(material))).ToArray();
                var renderBatches = model.wmoRenderBatches ?? [];
                var detailedBatches = renderBatches.Select((batch, index) => new ModelBatchData(
                    index,
                    checked((int)batch.groupID),
                    batch.materialIndex,
                    batch.firstFace,
                    batch.numFaces,
                    batch.blendType,
                    0,
                    EnumName<MOMTShader>(batch.shader),
                    batch.materialIndex >= 0 && batch.materialIndex < preppedMaterials.Length
                        ? preppedMaterials[batch.materialIndex].VertexShader.ToString()
                        : string.Empty,
                    batch.materialIndex >= 0 && batch.materialIndex < preppedMaterials.Length
                        ? preppedMaterials[batch.materialIndex].PixelShader.ToString()
                        : string.Empty,
                    SafeAssets(() => batch.materialFDIDs))).ToArray();
                var groups = (model.groupBatches ?? []).Select((group, index) =>
                {
                    var batches = renderBatches
                        .Where(batch => batch.groupID == (uint)index)
                        .ToArray();
                    return new WorldModelGroupData(
                        index,
                        string.IsNullOrWhiteSpace(group.groupName) ? $"Group {index}" : group.groupName,
                        group.mogiGroupName ?? string.Empty,
                        group.groupID,
                        batches.Length,
                        checked((int)group.verticeCount),
                        checked((int)(batches.Sum(batch => (long)batch.numFaces) / 3)),
                        group.doodadReferences?.Length ?? 0,
                        group.flags);
                }).ToArray();
                return new WorldModelObjectData(
                    fileDataId,
                    parentFileDataId,
                    groups.Length,
                    model.doodadSets?.Length ?? 0,
                    activeDoodadCount,
                    model.rootWMOFileDataID == fileDataId,
                    WoWRenderLib.Listfile.GetDisplayName(fileDataId),
                    SafeAssets(() => preppedMaterials.SelectMany(GetWmoTextureIds)),
                    uniqueId,
                    WoWRenderLib.Listfile.GetDisplayName(parentFileDataId),
                    groups,
                    new MapPlacementData(
                        MapPlacementKind.Modf,
                        uniqueId,
                        placementFlags,
                        placementDoodadSet,
                        placementNameSet),
                    new WorldModelRootData(model.ambientColor, model.flags),
                    materials,
                    detailedBatches,
                    (model.doodadSets ?? []).Select((name, index) =>
                        string.IsNullOrWhiteSpace(name) ? $"Set {index}" : name).ToArray());
        }

        private static WorldModelObjectData CreateUnloadedWorldModelObjectData(WMOContainer wmo) =>
            new(
                    wmo.FileDataId,
                    wmo.ParentFileDataId,
                    0,
                    0,
                    wmo.ActiveDoodads.Count,
                    false,
                    WoWRenderLib.Listfile.GetDisplayName(wmo.FileDataId),
                    [],
                    wmo.UniqueID,
                    WoWRenderLib.Listfile.GetDisplayName(wmo.ParentFileDataId),
                    [],
                    new MapPlacementData(
                        MapPlacementKind.Modf,
                        wmo.UniqueID,
                        wmo.PlacementFlags,
                        wmo.PlacementDoodadSet,
                        wmo.PlacementNameSet));

        private static string GetObjectDisplayName(Container3D selectedObject, IEditorObjectData? data)
        {
            var fileName = data switch
            {
                M2ObjectData m2 => m2.FileName,
                WorldModelObjectData wmo => wmo.FileName,
                _ => string.Empty
            };
            return string.IsNullOrWhiteSpace(fileName) || fileName.StartsWith("FDID ", StringComparison.Ordinal)
                ? $"{selectedObject.GetType().Name.Replace("Container", string.Empty)} {selectedObject.FileDataId}"
                : Path.GetFileName(fileName);
        }

        private static IEnumerable<uint> GetWmoTextureIds(WoWRenderLib.Structs.PreppedWMOMaterial material)
        {
            yield return material.TexFileDataID0;
            yield return material.TexFileDataID1;
            yield return material.TexFileDataID2;
            yield return material.TexFileDataID3;
            yield return material.TexFileDataID4;
            yield return material.TexFileDataID5;
            yield return material.TexFileDataID6;
            yield return material.TexFileDataID7;
            yield return material.TexFileDataID8;
        }

        private static IReadOnlyList<ModelTextureData> GetWmoTextureSlots(
            WoWRenderLib.Structs.PreppedWMOMaterial material)
        {
            var ids = new[]
            {
                material.TexFileDataID0, material.TexFileDataID1, material.TexFileDataID2,
                material.TexFileDataID3, material.TexFileDataID4, material.TexFileDataID5,
                material.TexFileDataID6, material.TexFileDataID7, material.TexFileDataID8
            };
            return ids.Select((fileDataId, index) => (fileDataId, index))
                .Where(item => item.fileDataId is not 0 and not uint.MaxValue)
                .Select(item => new ModelTextureData(
                    item.index + 1,
                    new AssetReference(item.fileDataId, WoWRenderLib.Listfile.GetDisplayName(item.fileDataId))))
                .ToArray();
        }

        private static IReadOnlyList<AssetReference> SafeAssets(Func<IEnumerable<uint>> getIds)
        {
            try
            {
                return getIds()
                    .Where(fileDataId => fileDataId is not 0 and not uint.MaxValue)
                    .Distinct()
                    .Select(fileDataId => new AssetReference(
                        fileDataId,
                        WoWRenderLib.Listfile.GetDisplayName(fileDataId)))
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }

        private static IReadOnlyList<ModelTextureData> SafeM2TextureSlots(
            Func<IEnumerable<int>> getIndices,
            IReadOnlyList<ModelTextureData> textures)
        {
            try
            {
                return getIndices()
                    .Where(index => index >= 0 && index < textures.Count)
                    .Select((index, slot) => new ModelTextureData(
                        slot + 1,
                        textures[index].Asset,
                        textures[index].Flags))
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }

        private static string EnumName<TEnum>(uint value) where TEnum : struct, Enum
        {
            var enumValue = (TEnum)Enum.ToObject(typeof(TEnum), value);
            return Enum.IsDefined(enumValue) ? enumValue.ToString() : value.ToString();
        }

        private static string GetGeosetType(ushort id) => (id / 100) switch
        {
            0 => "Base skin",
            1 => "Hair",
            2 => "Facial hair 1",
            3 => "Facial hair 2",
            4 => "Facial hair 3",
            5 => "Gloves",
            6 => "Boots",
            7 => "Ears",
            8 => "Wristbands",
            9 => "Kneepads",
            10 => "Chest",
            11 => "Pants",
            12 => "Tabard",
            13 => "Trousers",
            14 => "Cloak",
            16 => "Eye effects",
            17 => "Belt",
            18 => "Bones",
            19 => "Feet",
            20 => "Head",
            21 => "Torso",
            22 => "Hand attachment",
            23 => "Head attachment",
            var category => $"Category {category}"
        };

        private void OnSelectedObjectTransformRequested(object? sender, ObjectTransform transform)
        {
            var rotation = ToEulerDegrees(transform.Rotation);
            _rendererSession.Engine?.UpdateSelectedObjectTransform(
                transform.Position,
                rotation,
                transform.Scale.X);
        }

        private void OnWorldNavigationRequested(object? sender, ViewModels.WorldNavigationRequest request) =>
            _rendererSession.Engine?.NavigateTo(
                request.WdtFileDataId,
                request.Position.X,
                request.Position.Y,
                request.IsGlobalWmo);

        private void OnTerrainChunkTexturesRequested(object? sender, Vector2 mousePosition)
        {
            var layers = _rendererSession.Engine?.GetTerrainChunkTextures(mousePosition) ?? [];
            _vm?.PublishTerrainChunkTextures(layers);
        }

        private void OnDominantTerrainTextureRequested(object? sender, Vector2 mousePosition)
        {
            var texture = _rendererSession.Engine?.GetDominantTerrainTexture(mousePosition);
            if (texture is { } selected)
                _vm?.PublishDominantTerrainTexture(selected);
        }

        private void OnCurrentTerrainTileTexturesRequested(object? sender, EventArgs eventArgs)
        {
            var textures = _rendererSession.Engine?.GetCurrentTerrainTileTextures() ?? [];
            _vm?.PublishCurrentTerrainTileTextures(textures);
        }

        private void CompleteTerrainStroke(WowViewerEngine engine)
        {
            var delta = engine.EndTerrainStroke();
            if (delta is { IsEmpty: false } && _vm != null)
            {
                _vm.RecordAppliedEdit(new DelegateEditorCommand(
                    "Terrain stroke",
                    () => _rendererSession.Engine?.ApplyTerrainStroke(delta, useAfter: true),
                    () => _rendererSession.Engine?.ApplyTerrainStroke(delta, useAfter: false)));
                _terrainStrokeTransaction?.Commit();
            }

            _terrainStrokeTransaction?.Dispose();
            _terrainStrokeTransaction = null;
            _terrainStrokeActive = false;
            PublishTerrainDirtyState(engine, includeSnapshots: true);
        }

        private void PublishTerrainDirtyState(WowViewerEngine engine, bool includeSnapshots)
        {
            if (_vm == null)
                return;

            var hasUnsavedChanges = engine.HasUnsavedTerrainChanges;
            if (!includeSnapshots && _lastPublishedTerrainDirtyState == hasUnsavedChanges)
                return;

            _vm.UpdateTerrainDirtyState(
                hasUnsavedChanges,
                engine.ModifiedTerrainTiles);
            _lastPublishedTerrainDirtyState = hasUnsavedChanges;
        }

        private void OnSelectedWmoPlacementRequested(object? sender, ViewModels.WmoPlacementSelection selection)
        {
            _rendererSession.Engine?.UpdateSelectedWmoPlacement(selection.DoodadSet, selection.NameSet);
            if (_lastSelectedObject is WMOContainer wmo)
                _lastSelectedObjectData = CreateWorldModelObjectData(wmo);
        }

        private static Vector3 ToEulerDegrees(Quaternion quaternion)
        {
            quaternion = Quaternion.Normalize(quaternion);
            var sinPitch = 2f * (quaternion.W * quaternion.X - quaternion.Z * quaternion.Y);
            var pitch = MathF.Abs(sinPitch) >= 1f
                ? MathF.CopySign(MathF.PI / 2f, sinPitch)
                : MathF.Asin(sinPitch);
            var yaw = MathF.Atan2(
                2f * (quaternion.W * quaternion.Y + quaternion.X * quaternion.Z),
                1f - 2f * (quaternion.X * quaternion.X + quaternion.Y * quaternion.Y));
            var roll = MathF.Atan2(
                2f * (quaternion.W * quaternion.Z + quaternion.X * quaternion.Y),
                1f - 2f * (quaternion.X * quaternion.X + quaternion.Z * quaternion.Z));
            return new Vector3(pitch, yaw, roll) * (180f / MathF.PI);
        }

        private static int SafeCount(Func<int> getCount)
        {
            try
            {
                return getCount();
            }
            catch
            {
                return 0;
            }
        }

        private static bool SafeValue(Func<bool> getValue)
        {
            try
            {
                return getValue();
            }
            catch
            {
                return false;
            }
        }

        private void UpdateAutomatedBenchmark(FrameProfileSnapshot snapshot)
        {
            if (_vm == null || _benchmarkCoordinator == null)
                return;

            var action = _benchmarkCoordinator.Observe(snapshot);
            var elapsedSecond = (int)_benchmarkCoordinator.Elapsed.TotalSeconds;
            if (elapsedSecond != _lastBenchmarkStatusSecond && action == AutomatedBenchmarkAction.None)
            {
                _lastBenchmarkStatusSecond = elapsedSecond;
                _vm.UpdateAutomatedBenchmarkWaitingStatus(
                    _benchmarkCoordinator.Elapsed,
                    _benchmarkCoordinator.StableFrames,
                    _benchmarkOptions.StableFrameCount);
            }

            switch (action)
            {
                case AutomatedBenchmarkAction.StartCapture:
                    if (!_vm.TryStartAutomatedPerformanceCapture(
                            _benchmarkOptions.WarmupDuration,
                            _benchmarkOptions.CaptureDuration))
                    {
                        _vm.FailAutomatedPerformanceCapture(
                            "Automated benchmark could not start its performance capture.");
                    }
                    break;
                case AutomatedBenchmarkAction.Timeout:
                    _vm.FailAutomatedPerformanceCapture(
                        $"Automated benchmark timed out after {_benchmarkCoordinator.Elapsed.TotalSeconds:0} s " +
                        "before the world workload became idle and stable.");
                    break;
            }
        }

        private InputFrame BuildInputFrame()
        {
            var keysDown = new HashSet<Silk.NET.Input.Key>();

            if (_vm != null)
            {
                if (_vm.Forward) keysDown.Add(Silk.NET.Input.Key.W);
                if (_vm.Backward) keysDown.Add(Silk.NET.Input.Key.S);
                if (_vm.Left) keysDown.Add(Silk.NET.Input.Key.A);
                if (_vm.Right) keysDown.Add(Silk.NET.Input.Key.D);
                if (_vm.Up) keysDown.Add(Silk.NET.Input.Key.Q);
                if (_vm.Down) keysDown.Add(Silk.NET.Input.Key.E);
                if (_vm.Shift) keysDown.Add(Silk.NET.Input.Key.ShiftLeft);
                if (_vm.Ctrl) keysDown.Add(Silk.NET.Input.Key.ControlLeft);
                if (_vm.Space) keysDown.Add(Silk.NET.Input.Key.Space);
            }

            return new InputFrame
            {
                MousePosition = _vm?.MousePosition ?? Vector2.Zero,
                LeftMouseDown = _vm?.LeftMouseDown ?? false,
                RightMouseDown = _vm?.RightMouseDown ?? false,
                Mode = _vm?.EditorMode ?? EditorModeId.Selection,
                Modifiers = (_vm?.Shift == true ? InputModifiers.Shift : InputModifiers.None) |
                            (_vm?.Ctrl == true ? InputModifiers.Control : InputModifiers.None),
                Brush = new BrushInput(
                    (float)(_vm?.BrushSize ?? 10),
                    (float)(_vm?.BrushFalloff ?? 0.35),
                    _vm?.BrushHasFalloff ?? true,
                    _vm?.BrushShape ?? BrushShape.Circle,
                    _vm?.BrushFalloffProfile ?? BrushFalloffProfile.Smooth),
                TerrainBrush = new TerrainBrushInput
                {
                    ToolMode = (TerrainBrushMode)(_vm?.TerrainBrushToolMode ?? 0),
                    Speed = (float)(_vm?.TerrainBrushSpeed ?? 5),
                    FlattenHeight = (float)(_vm?.TerrainFlattenHeight ?? 0),
                    FlattenTarget = (TerrainFlattenTarget)(_vm?.TerrainFlattenTarget ?? 0),
                    SmoothIterations = _vm?.TerrainSmoothIterations ?? 1
                },
                TextureBrush = new TextureBrushInput(
                    (TextureBrushMode)(_vm?.TextureBrushToolMode ?? 0),
                    _vm?.TextureBrushTextureFileDataId ?? 0,
                    (byte)Math.Clamp((int)Math.Round(_vm?.TextureBrushOpacity ?? 255), 0, 255),
                    (float)(_vm?.TextureBrushStrength ?? 1)),
                MouseWheel = _vm?.MouseWheel ?? 0f,
                KeysDown = keysDown
            };
        }

        private void OnRendererStatusChanged(object? sender, RendererStatus status)
        {
            Dispatcher.UIThread.Post(() => _vm?.UpdateRendererStatus(status));
        }

        private void BeginCleanup()
        {
            _initialized = false;
            _restartPending = false;
            _suspendedFrameDue = false;
            StopSuspendedFrameTimer();
            if (Volatile.Read(ref _renderFrameInProgress) != 0)
            {
                _cleanupPending = true;
                return;
            }

            _ = CleanupAsync();
        }

        private async Task CleanupAsync()
        {
            await _lifecycleLock.WaitAsync();
            try
            {
                await DisposePresentationResourcesAsync();
                var surface = _surface;
                var surfaceVisual = _surfaceVisual;
                _surface = null;
                _surfaceVisual = null;
                if (surfaceVisual != null)
                    ElementComposition.SetElementChildVisual(this, null);
                if (surface != null)
                    surface.Dispose();
                if (_terrainStrokeActive && _rendererSession.Engine is { } activeEngine)
                    CompleteTerrainStroke(activeEngine);
                await _rendererSession.DisposeAsync();
                _deviceContext.Dispose();
                _device.Dispose();
                _d3d11?.Dispose();
                _dxgi?.Dispose();
                _deviceContext = default;
                _device = default;
                _d3d11 = null;
                _dxgi = null;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }
    }
}

