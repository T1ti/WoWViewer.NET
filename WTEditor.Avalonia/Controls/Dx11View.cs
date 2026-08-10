using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
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

        public Dx11View()
        {
            
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            _vm = DataContext as ViewModels.Editor3DViewModel;
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

            // PLACEHOLDER until config menu is added
            _wowConfig = new WowClientConfig
            {
                wowDir = "C:\\Program Files (x86)\\World of Warcraft",
                wowProduct = "wow_classic_era",
                // buildConfig = buildConfig,
                // cdnConfig = cdnConfig
            };

            _engine = new WowViewerEngine(_wowConfig, null, false);
            _engine.UseKeyedMutex = true;
            _engine.Initialize(_dxgi!, _device, _deviceContext, new Vector2D<int>(1, 1));

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
                _engine.Resize((uint)width, (uint)height);
                _lastSharedHandle = IntPtr.Zero;
                _importedImage = null;
            }

            var inputFrame = BuildInputFrame();
            _engine.Update(delta, inputFrame);
            _engine.Render(delta);

            var handle = _engine.GetSharedTextureHandle();
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
                _vm.Fps = _engine.Stats.FPS;
                _vm.FrameTime = _engine.Stats.FrameTimeMs;
                _vm.CameraPosition = _engine.activeCamera?.Position ?? Vector3.Zero;
                _vm.DrawCalls = (int)_engine.Stats.DrawCalls;
                _vm.VertexCount = (int)_engine.Stats.VertexCount;
            }

            RequestRenderFrame();
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

