using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
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
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.Controls
{
    public sealed class Dx11View : Control
    {
        private WowClientConfig _wowConfig;
        private WowViewerEngine? _engine;

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
        private RendererSettings _rendererSettings = new();
        private bool _renderFrameInProgress;
        private bool _restartPending;
        private WowClientConfig _clientConfig = new()
        {
            wowDir = @"C:\Program Files (x86)\World of Warcraft",
            wowProduct = "wow_classic_era"
        };

        public Dx11View()
        {
            
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_vm != null)
            {
                _vm.ClientConfigChanged -= OnClientConfigChanged;
                _vm.RendererSettingsChanged -= OnRendererSettingsChanged;
            }

            base.OnDataContextChanged(e);
            _vm = DataContext as ViewModels.Editor3DViewModel;

            if (_vm != null)
            {
                _clientConfig = _vm.ClientConfig;
                _rendererSettings = _vm.RendererSettings.Clone();
                _vm.ClientConfigChanged += OnClientConfigChanged;
                _vm.RendererSettingsChanged += OnRendererSettingsChanged;
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // Focusable = true;
            // Focus();
            InitializeAsync();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Cleanup();
        }

        private async void InitializeAsync()
        {
            var compositionVisual = ElementComposition.GetElementVisual(this);
            if (compositionVisual == null)
                return;

            _compositor = compositionVisual.Compositor;

            _interop = await _compositor.TryGetCompositionGpuInterop();
            if (_interop == null)
            {
                Console.WriteLine("Dx11View: ICompositionGpuInterop not available on this platform/backend.");
                return;
            }

            if (!_interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
            {
                Console.WriteLine("Dx11View: D3D11 shared texture handle import not supported by compositor.");
                return;
            }

            CreateD3DDevice();

            CreateEngine(_clientConfig);

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

        private void OnClientConfigChanged(object? sender, WowClientConfig config)
        {
            _clientConfig = config;
            Dispatcher.UIThread.Post(RestartEngine, DispatcherPriority.Render);
        }

        private void RestartEngine()
        {
            if (!_initialized || _dxgi == null)
                return;

            if (_renderFrameInProgress)
            {
                _restartPending = true;
                return;
            }

            _initialized = false;
            _importedImage = null;
            _lastSharedHandle = IntPtr.Zero;
            _engine?.Dispose();
            CreateEngine(_clientConfig);
            _initialized = true;
            RequestRenderFrame();
        }

        private void OnRendererSettingsChanged(object? sender, RendererSettings settings)
        {
            _rendererSettings = settings.Clone();
            if (_engine == null)
                return;

            Dispatcher.UIThread.Post(() => _engine?.ApplySettings(_rendererSettings), DispatcherPriority.Render);
        }

        private void CreateEngine(WowClientConfig config)
        {
            _wowConfig = config;
            _engine = new WowViewerEngine(_wowConfig, null, false)
            {
                UseKeyedMutex = true
            };
            _engine.Initialize(_dxgi!, _device, _deviceContext,
                new Vector2D<int>(Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height)));
            _engine.ApplySettings(_rendererSettings);
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
            if (!_initialized || _engine == null || _interop == null || _surface == null || _surfaceVisual == null)
                return;

            if (_renderFrameInProgress)
                return;

            _renderFrameInProgress = true;
            var engine = _engine;

            try
            {
                await RenderFrameCore(engine);
            }
            finally
            {
                _renderFrameInProgress = false;

                if (_restartPending)
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

            var inputFrame = BuildInputFrame();
            if (_vm != null)
            {
                engine.SetMovementSpeed(_vm.MoveSpeed);
                engine.SetMouseSensitivity(_vm.MouseSensitivity);
            }
            engine.Update(delta, inputFrame);
            engine.Render(delta);

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
                _vm.Fps = engine.Stats.FPS;
                _vm.FrameTime = engine.Stats.FrameTimeMs;
                _vm.CameraPosition = engine.activeCamera?.Position ?? Vector3.Zero;
                _vm.DrawCalls = (int)engine.Stats.DrawCalls;
                _vm.VertexCount = (int)engine.Stats.VertexCount;
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

        private void Cleanup()
        {
            if (_vm != null)
            {
                _vm.ClientConfigChanged -= OnClientConfigChanged;
                _vm.RendererSettingsChanged -= OnRendererSettingsChanged;
            }

            _initialized = false;
            _importedImage = null;
            _surface = null;
            _surfaceVisual = null;
            _engine?.Dispose();
            _engine = null;
            _deviceContext.Dispose();
            _device.Dispose();
            _d3d11?.Dispose();
            _dxgi?.Dispose();
        }
    }
}

