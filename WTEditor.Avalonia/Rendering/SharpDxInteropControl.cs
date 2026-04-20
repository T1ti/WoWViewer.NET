// Copied and adapted from https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.0/samples/GpuInterop

using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Silk.NET.OpenGL;
using Silk.NET.WGL;
using Silk.NET.WGL.Extensions.NV;

using Silk.NET.Windowing;
using WTEditor.Avalonia.Util;

// using GL = Silk.NET.OpenGL.GL;
using D3DDevice = SharpDX.Direct3D11.Device;


// Based on : https://github.com/MeltyPlayer/MeltyTool/tree/main/FinModelUtility/Fin/Fin.Ui.Avalonia/gl

namespace WTEditor.Avalonia.Rendering
{
    /// <summary>
    ///   Shamelessly stolen from:
    ///   https://github.com/Dragorn421/DragoStuff/blob/aa1ac3434f2701739570adf77377c29e1e3171c1/SharpDXInteropControl.cs
    /// </summary>
    public class SharpDxInteropControl : Control
    {
        private CompositionSurfaceVisual? visual_;
        private Compositor? compositor_;
        private string info_ = string.Empty;
        private bool updateQueued_;
        private bool initialized_ = false;
        private bool _isRendering = false;
        private int _framePendingOrRunning; // 0 or 1 (Interlocked)

        private IWindow? silkWindow_;
        private GL? gl_;
        private WGL? wgl_;
        private NVDXInterop? nvInterop_;

        private IntPtr hDevice_;

        public event Action? OnInit;

        // private IDisposable currentImageDisposable_;
        private D3D11SwapchainImage currentImage_;

        private Action<GL> initGl_;
        private Action<GL> renderGl_;
        private Action<GL> teardownGl_;

        private readonly Action<double>? frameTimeCallback_;
        private Stopwatch _sw = new Stopwatch();
        private double _last;

        // public nint ContextProcAdress = 0;

        protected CompositionDrawingSurface? Surface { get; private set; }

        public static async Task<bool> TryToAddTo(
        Panel parent,
        Action<GL> initGl,
        Action<GL> renderGl,
        Action<GL> teardownGl,
        Action<double>? frameTimeCallback = null)
        {
            bool success = false;

            SharpDxInteropControl? control = null;
            try
            {
                control = new SharpDxInteropControl(initGl, renderGl, teardownGl, frameTimeCallback);
                parent.Children.Add(control);
                success = control.initialized_;
            }
            catch
            {
            }

            if (!success)
            {
                if (control != null)
                {
                    parent.Children.Remove(control);
                }
                return false;
            }

            return true;
        }

        public SharpDxInteropControl(Action<GL> initGl, Action<GL> renderGl, Action<GL> teardownGl, Action<double>? frameTimeCallback = null)
        {
            this.initGl_ = initGl;
            this.renderGl_ = renderGl;
            this.teardownGl_ = teardownGl;
            this.SizeChanged += (sender, e) => { this.QueueNextFrame_(); };

            frameTimeCallback_ = frameTimeCallback;
            _sw.Start();
            _last = _sw.Elapsed.TotalSeconds;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            this.Initialize_().Wait();
        }

        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            if (this.initialized_)
            {
                this.Surface?.Dispose();
                this.FreeGraphicsResources();
            }

            this.initialized_ = false;
            base.OnDetachedFromLogicalTree(e);
        }

        private async Task Initialize_()
        {
            var selfVisual = ElementComposition.GetElementVisual(this)!;
            this.compositor_ = selfVisual.Compositor;

            this.Surface = this.compositor_.CreateDrawingSurface();
            this.visual_ = this.compositor_.CreateSurfaceVisual();
            this.visual_.Size = new(this.Bounds.Width, this.Bounds.Height);
            this.visual_.Surface = this.Surface;
            ElementComposition.SetElementChildVisual(this, this.visual_);
            var interop = await this.compositor_.TryGetCompositionGpuInterop();

            // Create invisible window to host the contextual GL + WGL state
            var options = WindowOptions.Default;
            options.IsVisible = false;
            options.Size = new Silk.NET.Maths.Vector2D<int>(100, 100);
            options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible /*| ContextFlags.Debug*/, new APIVersion(4, 5));
            options.VSync = false;
            // options.ShouldSwapAutomatically = true;

            // TODO : check if we need to swap buffers and vsync

            this.silkWindow_ = Silk.NET.Windowing.Window.Create(options);
            this.silkWindow_.Initialize();

            

            this.gl_ = this.silkWindow_.CreateOpenGL();
            this.wgl_ = WGL.GetApi(/*this.silkWindow_.GLContext*/);
            // this.silkWindow_.GLContext!.MakeCurrent(); // switch context
            GlUtil.SwitchContext(this.silkWindow_!.GLContext);

            if (!this.wgl_.TryGetExtension<NVDXInterop>(out this.nvInterop_))
            {
                throw new PlatformNotSupportedException("NV_DX_interop not available on this device.");
            }

            bool res;
            string info;
            if (interop == null)
                (res, info) = (false, "Compositor doesn't support interop for the current backend");
            else
                (res, info) = this.InitializeGraphicsResources(this.Surface, interop);

            this.info_ = info;
            this.initialized_ = res;

            if (!res)
                return;

            this.initGl_(this.gl_);
            this.OnInit?.Invoke();

            unsafe
            {
                this.hDevice_ = this.nvInterop_.DxopenDevice((void*)(nint)this.device_!.NativePointer);
                if (this.hDevice_ == IntPtr.Zero)
                {
                    throw new Exception("DXOpenDeviceNV failed");
                }
            }

            _sw.Start();
            _last = _sw.Elapsed.TotalSeconds;

            this.QueueNextFrame_();
        }

        private void QueueNextFrame_()
        {
            if (this.initialized_ && !this.updateQueued_ && this.compositor_ != null)
            {
                this.updateQueued_ = true;
                this.compositor_?.RequestCompositionUpdate(this.UpdateFrame_);
            }
        }

        private void UpdateFrame_()
        {
            this.updateQueued_ = false;
            var root = /*this as IRenderRoot ??*/ this.VisualRoot;
            if (root == null)
                return;

            bool test_early_queuing = false;
            if (test_early_queuing)
            {
                if (_isRendering)
                {
                    QueueNextFrame_();
                    return;
                }

                _isRendering = true;

                // schedule next frame early
                QueueNextFrame_();
            }

            this.visual_!.Size = new(this.Bounds.Width, this.Bounds.Height);

            var scaling = 1f;
            // if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window window)
            // {
            //     scaling = (float)window.RenderScaling;
            // }

            var size = PixelSize.FromSize(this.Bounds.Size, scaling);

            // mMeasure internal frame time, don't include time between frames (refresh rate limits)
            double frameStart = _sw.Elapsed.TotalSeconds;
            double frameDelta = frameStart - _last;
            _last = frameStart;
            //
            this.RenderFrame(size);

            // timing end
            var end = _sw.Elapsed.TotalSeconds;
            var frameTime = end - frameStart;
            frameTimeCallback_?.Invoke(frameTime * 1000.0); // ms

            if (test_early_queuing)
                _isRendering = false;
            else
                this.QueueNextFrame_();
        }

        private D3DDevice? device_;
        private D3d11Swapchain? swapchain_;
        private DeviceContext? context_;
        private PixelSize lastSize_;

        protected (bool success, string info) InitializeGraphicsResources(
            CompositionDrawingSurface surface,
            ICompositionGpuInterop interop
        )
        {
            if (!interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
                return (
                    false,
                    "DXGI shared handle import is not supported by the current graphics backend"
                );

            var factory = new SharpDX.DXGI.Factory1();
            using var adapter = factory.GetAdapter1(0);
            this.device_ = new D3DDevice(
                adapter,
                GlConstants.Debug ? DeviceCreationFlags.Debug : DeviceCreationFlags.None,
                new[] {
            FeatureLevel.Level_12_1,
            FeatureLevel.Level_12_0,
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_0,
            FeatureLevel.Level_9_3,
            FeatureLevel.Level_9_2,
            FeatureLevel.Level_9_1,
                }
            );
            this.swapchain_ = new D3d11Swapchain(this.device_, interop, surface, this.gl_!, this.nvInterop_!);
            this.context_ = this.device_.ImmediateContext;

            return (
                true,
                $"D3D11 ({this.device_.FeatureLevel}) {adapter.Description1.Description}");
        }

        protected void FreeGraphicsResources()
        {
            if (this.swapchain_ is not null)
            {
                this.swapchain_.DisposeAsync().GetAwaiter().GetResult();
                this.swapchain_ = null;
            }

            if (this.hDevice_ != IntPtr.Zero && this.nvInterop_ != null)
            {
                this.nvInterop_.DxcloseDevice(this.hDevice_);
            }

            Utilities.Dispose(ref this.context_);
            Utilities.Dispose(ref this.device_);

            this.silkWindow_?.Dispose();
            this.silkWindow_ = null;

            if (this.gl_ != null)
            {
                this.teardownGl_(this.gl_);
            }
        }

        protected void RenderFrame(PixelSize pixelSize)
        {
            if (pixelSize == default)
                return;

            // this.silkWindow_!.GLContext!.MakeCurrent();
            GlUtil.SwitchContext(this.silkWindow_!.GLContext);

            // silkWindow_.SwapBuffers();

            if (pixelSize != this.lastSize_)
            {
                this.lastSize_ = pixelSize;
                this.Resize_(pixelSize);
            }

            using (this.swapchain_!.BeginDraw(this.hDevice_,
                                              pixelSize,
                                              out this.currentImage_))
            {
                // call render event on the control
                this.renderGl_(this.gl_!);
            }
        }

        private void Resize_(PixelSize size)
        {
            if (this.device_ is null)
                return;

            // Setup targets and viewport for rendering
            this.device_.ImmediateContext.Rasterizer.SetViewport(
                0,
                0,
                size.Width,
                size.Height);

            this.silkWindow_!.Size = new Silk.NET.Maths.Vector2D<int>(size.Width, size.Height);

            this.gl_!.Viewport(0, 0, (uint)size.Width, (uint)size.Height);
            // GlUtil.SetViewport(new Rectangle(0, 0, size.Width, size.Height));
        }
    }
}
