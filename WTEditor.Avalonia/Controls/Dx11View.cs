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
using WTEditor.Avalonia.Rendering;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.Controls
{
    public sealed class Dx11View : Control
    {
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
        private ICompositionImportedGpuImage? _importedImage;

        private int _lastWidth;
        private int _lastHeight;
        private IntPtr _lastSharedHandle;

        private bool _initialized;

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private double _last;

        private ViewModels.Editor3DViewModel? _vm;
        private RenderingConfiguration _renderingConfiguration = new();
        private bool _renderFrameInProgress;
        private bool _restartPending;
        private bool _cleanupPending;
        private bool _attached;
        private int _attachmentGeneration;
        private long _profileFrameNumber;
        private ClientConfiguration _clientConfiguration = new();

        public Dx11View()
        {
            _rendererSession.StatusChanged += OnRendererStatusChanged;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_vm != null)
            {
                _vm.ClientConfigurationChanged -= OnClientConfigurationChanged;
                _vm.RenderingConfigurationChanged -= OnRenderingConfigurationChanged;
            }

            base.OnDataContextChanged(e);
            _vm = DataContext as ViewModels.Editor3DViewModel;

            if (_vm != null)
            {
                _clientConfiguration = _vm.ClientConfiguration;
                _renderingConfiguration = _vm.RenderingConfiguration;
                _vm.ClientConfigurationChanged += OnClientConfigurationChanged;
                _vm.RenderingConfigurationChanged += OnRenderingConfigurationChanged;
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

                ElementComposition.SetElementChildVisual(this, _surfaceVisual);

                _initialized = true;
                RequestRenderFrame();
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
            if (!_initialized || _dxgi == null || !_attached)
                return;

            if (_renderFrameInProgress)
            {
                _restartPending = true;
                return;
            }

            await _lifecycleLock.WaitAsync();
            try
            {
                _initialized = false;
                _importedImage = null;
                _lastSharedHandle = IntPtr.Zero;
                await _rendererSession.DisposeAsync();

                if (!_attached || _dxgi == null)
                    return;

                try
                {
                    CreateEngine(_clientConfiguration);
                    _initialized = true;
                    RequestRenderFrame();
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
            if (_rendererSession.Engine == null)
                return;

            Dispatcher.UIThread.Post(
                () => _rendererSession.Engine?.ApplySettings(_renderingConfiguration.ToDx11()),
                DispatcherPriority.Render);
        }

        private void CreateEngine(ClientConfiguration configuration)
        {
            _rendererSession.Initialize(
                configuration,
                _renderingConfiguration,
                _dxgi!,
                _device,
                _deviceContext,
                new Vector2D<int>(Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height)),
                _vm?.HasInitialCameraPosition == true ? _vm.InitialCameraPosition : null,
                _vm?.HasInitialCameraDirection == true ? _vm.InitialCameraDirection : null);
        }

        private unsafe void CreateD3DDevice()
        {
            _dxgi = DXGI.GetApi(null, false);
            _d3d11 = D3D11.GetApi(null, false);

            SilkMarshal.ThrowHResult(
                _d3d11.CreateDevice(
                    default(ComPtr<IDXGIAdapter>),
                    D3DDriverType.Hardware,
                    Software: default,
#if DEBUG
                    (uint)CreateDeviceFlag.Debug,
#else
                    0,
#endif
                    null,
                    0,
                    D3D11.SdkVersion,
                    ref _device,
                    null,
                    ref _deviceContext
                )
            );
        }

        private void RequestRenderFrame()
        {
            Dispatcher.UIThread.Post(RenderFrame, DispatcherPriority.Render);
        }

        private async void RenderFrame()
        {
            var engine = _rendererSession.Engine;
            if (!_initialized || engine == null || _interop == null || _surface == null || _surfaceVisual == null)
                return;

            if (_renderFrameInProgress)
                return;

            _renderFrameInProgress = true;
            try
            {
                await RenderFrameCore(engine);
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
                _renderFrameInProgress = false;

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
                    RequestRenderFrame();
                }
            }
        }

        private async Task RenderFrameCore(WowViewerEngine engine)
        {
            if (_interop == null || _surface == null || _surfaceVisual == null)
                return;

            double now = _sw.Elapsed.TotalSeconds;
            double delta = now - _last;
            _last = now;

            int width = Math.Max(1, (int)Bounds.Width);
            int height = Math.Max(1, (int)Bounds.Height);

            if (width != _lastWidth || height != _lastHeight)
            {
                _lastWidth = width;
                _lastHeight = height;
                _surfaceVisual.Size = new Vector2(width, height);
                _surfaceVisual.CenterPoint = new Vector3(0, height / 2f, 0);
                engine.Resize((uint)width, (uint)height);
                _lastSharedHandle = IntPtr.Zero;
                _importedImage = null;
            }

            var inputStarted = Stopwatch.GetTimestamp();
            var inputFrame = BuildInputFrame();
            var inputMilliseconds = Stopwatch.GetElapsedTime(inputStarted).TotalMilliseconds;
            var engineFrameStarted = Stopwatch.GetTimestamp();
            engine.Update(delta, inputFrame);
            engine.Render(delta);
            var engineFrameMilliseconds = Stopwatch.GetElapsedTime(engineFrameStarted).TotalMilliseconds;

            var handle = engine.GetSharedTextureHandle();
            if (handle != IntPtr.Zero && handle != _lastSharedHandle)
            {
                _lastSharedHandle = handle;
                _importedImage = _interop.ImportImage(
                    new PlatformHandle(handle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
                    new PlatformGraphicsExternalImageProperties
                    {
                        Width = width,
                        Height = height,
                        Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm
                    });
            }

            if (_importedImage != null)
                await _surface.UpdateWithKeyedMutexAsync(_importedImage, acquireIndex: 1, releaseIndex: 0);

            if (_vm != null)
            {
                _vm.UpdateTelemetry(new ViewportTelemetry(
                    engine.Stats.FPS,
                    engineFrameMilliseconds,
                    engine.activeCamera?.Position ?? Vector3.Zero,
                    engine.activeCamera?.Front ?? Vector3.Zero,
                    (int)engine.Stats.DrawCalls,
                    (int)engine.Stats.VertexCount));

                var gpuUploadMilliseconds = engine.Stats.GpuUploadTimeMs ?? 0;
                var gpuDrawMilliseconds = engine.Stats.GpuDrawTimeMs ?? 0;
                var gpuOtherMilliseconds = Math.Max(
                    0,
                    (engine.Stats.GpuFrameTimeMs ?? 0) - gpuUploadMilliseconds - gpuDrawMilliseconds);
                var steps = new FrameTimingStep[]
                {
                    new("World streaming (CPU)", engine.Stats.TileUpdateTimeMs),
                    new("Resource upload submission (CPU)", engine.Stats.AssetUploadTimeMs),
                    new("Visibility culling (CPU)", engine.Stats.CullingTimeMs),
                    new("Draw submission (CPU)", Math.Max(0, engine.Stats.SceneRenderTimeMs - engine.Stats.CullingTimeMs)),
                    new("Other frame work (CPU)", inputMilliseconds + engine.Stats.UpdateTimeMs + engine.Stats.RenderOverheadTimeMs),
                    new("Resource uploads (GPU)", gpuUploadMilliseconds, FrameTimingDomain.Gpu),
                    new("World drawing (GPU)", gpuDrawMilliseconds, FrameTimingDomain.Gpu),
                    new("Other GPU work", gpuOtherMilliseconds, FrameTimingDomain.Gpu),
                    new("Wait for viewport texture", engine.Stats.MutexWaitTimeMs, FrameTimingDomain.Presentation)
                };
                _vm.UpdatePerformanceProfile(new FrameProfileSnapshot(
                    ++_profileFrameNumber,
                    DateTimeOffset.UtcNow,
                    delta * 1_000d,
                    inputMilliseconds + engine.Stats.CpuFrameTimeMs,
                    engine.Stats.GpuFrameTimeMs,
                    steps,
                    (int)engine.Stats.DrawCalls,
                    (int)engine.Stats.VertexCount,
                    engine.Stats.PendingAssetOperations)
                {
                    EngineFrameMilliseconds = engineFrameMilliseconds,
                    UploadedResources = engine.Stats.UploadedResources,
                    Culling = new CullingMetrics(
                        engine.Stats.VisibleTerrainChunks,
                        engine.Stats.CandidateTerrainChunks,
                        engine.Stats.VisibleWorldModels,
                        engine.Stats.CandidateWorldModels,
                        engine.Stats.VisibleDoodads,
                        engine.Stats.CandidateDoodads)
                });
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
            if (_renderFrameInProgress)
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
                _importedImage = null;
                _surface = null;
                _surfaceVisual = null;
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

