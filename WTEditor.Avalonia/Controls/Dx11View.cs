using System;
using System.Diagnostics;
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
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.ViewModels;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Renderer;
using WoWRenderLib.Diagnostics;

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
        private readonly SelectedObjectDisplayProjection _selectionDisplayProjection = new();
        private int _lastBenchmarkStatusSecond = -1;
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
                _vm.LightingSettingsChanged -= OnLightingSettingsChanged;
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
                _vm.LightingSettingsChanged += OnLightingSettingsChanged;
                _vm.SelectedObjectTransformRequested += OnSelectedObjectTransformRequested;
                _vm.SelectedWmoPlacementRequested += OnSelectedWmoPlacementRequested;
                _vm.WorldNavigationRequested += OnWorldNavigationRequested;
                _vm.TerrainChunkTexturesRequested += OnTerrainChunkTexturesRequested;
                _vm.DominantTerrainTextureRequested += OnDominantTerrainTextureRequested;
                _vm.CurrentTerrainTileTexturesRequested += OnCurrentTerrainTileTexturesRequested;
                ReplayCurrentWorldNavigation();
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
                    LoadDiagnostics.Error("Initializing the DX11 renderer view", exception);
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
                    LoadDiagnostics.Error("Restarting the DX11 renderer view", exception);
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

        private void OnLightingSettingsChanged(
            object? sender,
            LightingSettingsSnapshot lighting)
        {
            if (_rendererSession.Engine == null)
                return;

            void ApplyLighting() => _rendererSession.Engine?.ApplyWorldLighting(
                LightingSettingsProjection.ToRenderer(lighting));

            // ColorPicker changes originate on the UI/render thread. Apply
            // them before the next telemetry snapshot instead of queueing a
            // Render-priority callback that can let the old renderer state
            // overwrite the editor while a color is being dragged.
            if (Dispatcher.UIThread.CheckAccess())
                ApplyLighting();
            else
                Dispatcher.UIThread.Post(ApplyLighting, DispatcherPriority.Render);
            if (_initialized && _attached && RenderActivity != ViewportRenderActivity.Suspended)
                RequestRenderFrame();
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
            _rendererSession.Engine?.ApplyWorldLighting(WorldLightingSettings.Defaults with
            {
                Time = _renderingConfiguration.WorldLightingTime,
                IsDynamic = _renderingConfiguration.UseLocalWorldLightingTime
            });
            ReplayCurrentWorldNavigation();
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
                LoadDiagnostics.Error("Resizing the DX11 renderer view", exception);
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
                LoadDiagnostics.Error("Rendering a DX11 viewport frame", exception);
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
            var inputFrame = ViewportInputProjection.Create(_vm);
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
                        engine.CurrentMapId));
                    _vm.UpdateActiveLighting(
                        LightingSettingsProjection.ToDisplay(
                            engine.ActiveWorldLighting,
                            engine.ActiveWorldSky,
                            engine.ActiveWorldLightingContributions));
                }

                var profileSnapshot = FrameProfileSnapshotFactory.Create(
                    ++_profileFrameNumber,
                    DateTimeOffset.UtcNow,
                    delta,
                    inputMilliseconds,
                    engineFrameMilliseconds,
                    streamingBudgetMilliseconds,
                    width,
                    height,
                    engine.Stats);
                _vm.UpdatePerformanceProfile(profileSnapshot);
                UpdateAutomatedBenchmark(profileSnapshot);
            }
        }

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

            _vm.UpdateSelectedObject(
                _selectionDisplayProjection.CreateDisplaySnapshot(selectedObject));
        }

        private void OnSelectedObjectTransformRequested(object? sender, ObjectTransform transform)
        {
            var rotation = ToEulerDegrees(transform.Rotation);
            _rendererSession.Engine?.UpdateSelectedObjectTransform(
                transform.Position,
                rotation,
                transform.Scale.X);
        }

        private void OnWorldNavigationRequested(object? sender, ViewModels.WorldNavigationRequest request)
        {
            ApplyWorldNavigation(request);
        }

        private void ReplayCurrentWorldNavigation()
        {
            if (_vm?.CurrentWorldNavigation is { } navigation)
                ApplyWorldNavigation(navigation);
        }

        private void ApplyWorldNavigation(ViewModels.WorldNavigationRequest request)
        {
            _rendererSession.Engine?.NavigateTo(
                request.MapId,
                request.WdtPath,
                request.WdtFileDataIdHint,
                request.Position.X,
                request.Position.Y,
                request.IsGlobalWmo);
            // Navigation is retained until client initialization is complete,
            // but an already-ready renderer should consume it immediately even
            // when the viewport was otherwise idle.
            RequestRenderFrame();
        }

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
            if (_rendererSession.Engine?.SelectedObject is WMOContainer wmo)
                _selectionDisplayProjection.RefreshDisplayData(wmo);
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

