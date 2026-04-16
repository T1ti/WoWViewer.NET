// Copied and adapted from https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.0/samples/GpuInterop

using System;
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

using fin.ui.rendering.gl;

using OpenTK.Graphics.Wgl;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;

using GL = OpenTK.Graphics.OpenGL.GL;


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
        private bool initialized_;

        private IntPtr hDevice_;

        public event Action? OnInit;

        private IDisposable currentImageDisposable_;
        private D3D11SwapchainImage currentImage_;

        private Action initGl_;
        private Action renderGl_;
        private Action teardownGl_;

        protected CompositionDrawingSurface? Surface { get; private set; }

        public static async Task<bool> TryToAddTo(
            Panel parent,
            Action initGl,
            Action renderGl,
            Action teardownGl)
        {
            bool success = false;

            SharpDxInteropControl? control = null;
            try
            {
                control = new SharpDxInteropControl(initGl, renderGl, teardownGl);
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

        public SharpDxInteropControl(Action initGl,
                                     Action renderGl,
                                     Action teardownGl)
        {
            this.initGl_ = initGl;
            this.renderGl_ = renderGl;
            this.teardownGl_ = teardownGl;
            this.SizeChanged += (sender, e) => { this.QueueNextFrame_(); };
        }

        protected override void OnAttachedToVisualTree(
            VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            this.Initialize_().Wait();
        }

        protected override void OnDetachedFromLogicalTree(
            LogicalTreeAttachmentEventArgs e)
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
            bool res;
            string info;
            if (interop == null)
                (res, info) = (
                    false, "Compositor doesn't support interop for the current backend");
            else
                (res, info) = this.InitializeGraphicsResources(this.Surface, interop);
            this.info_ = info;
            this.initialized_ = res;

            var bindingsContext = new GLFWBindingsContext();
            Wgl.LoadBindings(bindingsContext);
            GL.LoadBindings(bindingsContext);

            GlUtil.SwitchContext(this.openTkWindow_!.Context);

            IntPtr hDc = wglGetCurrentDC();
            if (hDc == IntPtr.Zero)
                throw new InvalidOperationException(
                    "No current hDC. Make sure OpenGL context is current."
                );
            string[] extensions = Wgl
                                  .Arb.GetExtensionsString(hDc)
                                  .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool hasInterop = extensions.Contains("WGL_NV_DX_interop");
            if (!hasInterop)
            {
                throw new PlatformNotSupportedException(
                    "NV_DX_interop not available on this device."
                );
            }

            this.initGl_();
            this.OnInit?.Invoke();

            this.hDevice_ = Wgl.DXOpenDeviceNV(this.device_.NativePointer);
            if (this.hDevice_ == IntPtr.Zero)
            {
                throw new Exception("DXOpenDeviceNV failed");
            }

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
            var root = this as IRenderRoot ?? this.VisualRoot;
            if (root == null)
                return;

            this.visual_!.Size = new(this.Bounds.Width, this.Bounds.Height);
            var size = PixelSize.FromSize(this.Bounds.Size, root.RenderScaling);
            this.RenderFrame(size);

            this.QueueNextFrame_();
        }

        private Device? device_;
        private D3d11Swapchain? swapchain_;
        private DeviceContext? context_;
        private PixelSize lastSize_;

        private NativeWindow? openTkWindow_;

        protected (bool success, string info) InitializeGraphicsResources(
            CompositionDrawingSurface surface,
            ICompositionGpuInterop interop
        )
        {
            if (
                !interop.SupportedImageHandleTypes.Contains(
                    KnownPlatformGraphicsExternalImageHandleTypes
                        .D3D11TextureGlobalSharedHandle
                )
            )
                return (
                    false,
                    "DXGI shared handle import is not supported by the current graphics backend"
                );

            var factory = new SharpDX.DXGI.Factory1();
            using var adapter = factory.GetAdapter1(0);
            this.device_ = new Device(
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
            this.swapchain_ = new D3d11Swapchain(this.device_, interop, surface);
            this.context_ = this.device_.ImmediateContext;

            var nativeWindowSettings = GlfwConstants.CreateNewNativeWindowSettings();
            nativeWindowSettings.ClientSize = new(100, 100);

            this.openTkWindow_ = new NativeWindow(nativeWindowSettings);

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

            if (this.hDevice_ != IntPtr.Zero)
            {
                Wgl.DXCloseDeviceNV(this.hDevice_);
            }

            Utilities.Dispose(ref this.context_);
            Utilities.Dispose(ref this.device_);

            this.openTkWindow_?.Dispose();
            this.openTkWindow_ = null;

            this.teardownGl_();
        }

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetCurrentDC();

        protected void RenderFrame(PixelSize pixelSize)
        {
            if (pixelSize == default)
                return;

            GlUtil.SwitchContext(this.openTkWindow_!.Context);

            if (pixelSize != this.lastSize_)
            {
                this.lastSize_ = pixelSize;
                this.Resize_(pixelSize);
            }

            using (this.swapchain_!.BeginDraw(this.hDevice_,
                                              pixelSize,
                                              out this.currentImage_))
            {
                this.renderGl_();
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

            this.openTkWindow_!.ClientSize = (size.Width, size.Height);
            GlUtil.SetViewport(new Rectangle(0, 0, size.Width, size.Height));
        }
    }
}
