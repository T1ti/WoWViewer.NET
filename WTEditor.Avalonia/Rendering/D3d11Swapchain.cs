// Copied and adapted from https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.0/samples/GpuInterop/D3DDemo/D3D11Swapchain.cs

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;

// using OpenTK.Graphics.OpenGL4;
// using OpenTK.Graphics.Wgl;
// using OpenTK.Platform.Windows;

using Silk.NET.OpenGL;
using Silk.NET.WGL.Extensions.NV;
using Silk.NET.WGL;

using SharpDX.Direct3D11;
using SharpDX.DXGI;

using D3DDevice = SharpDX.Direct3D11.Device;
using DxgiResource = SharpDX.DXGI.Resource;


namespace WTEditor.Avalonia.Rendering
{
    class D3d11Swapchain
    {
        protected ICompositionGpuInterop Interop { get; }
        protected CompositionDrawingSurface Target { get; }
        private readonly List<D3D11SwapchainImage> pendingImages_ = [];
        private readonly D3DDevice device_;

        private readonly GL gl_;
        private readonly NVDXInterop nvInterop_;

        public D3d11Swapchain(
            D3DDevice device,
            ICompositionGpuInterop interop,
            CompositionDrawingSurface target,
            GL gl,
            NVDXInterop nvInterop
        )
        {
            this.Interop = interop;
            this.Target = target;
            this.device_ = device;
            this.gl_ = gl;
            this.nvInterop_ = nvInterop;
        }

        D3D11SwapchainImage? CleanupAndFindNextImage_(PixelSize size)
        {
            D3D11SwapchainImage? firstFound = null;
            var foundMultiple = false;

            for (var c = this.pendingImages_.Count - 1; c > -1; c--)
            {
                var image = this.pendingImages_[c];
                var ready = image.LastPresent == null || image.LastPresent.Status == TaskStatus.RanToCompletion;
                var matches = image.Size == size;

                if (image.LastPresent?.IsFaulted == true || (!matches && ready))
                {
                    _ = image.DisposeAsync();
                    this.pendingImages_.RemoveAt(c);
                }

                if (matches && ready)
                {
                    if (firstFound == null)
                        firstFound = image;
                    else
                        foundMultiple = true;
                }
            }

            return foundMultiple ? firstFound : null;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var img in this.pendingImages_)
                await img.DisposeAsync();
        }

        class AnonymousDisposable : IDisposable
        {
            private volatile Action? dispose_;

            public AnonymousDisposable(Action dispose)
            {
                this.dispose_ = dispose;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref this.dispose_, null)?.Invoke();
            }
        }

        public IDisposable BeginDraw(IntPtr hDevice,
                                     PixelSize size,
                                     out D3D11SwapchainImage image)
        {
            // var img = this.CleanupAndFindNextImage_(size) ??
            //           new(hDevice, this.device_, size, this.Interop, this.Target);

            var img = this.CleanupAndFindNextImage_(size) ??
                        new(hDevice, this.device_, size, this.Interop, this.Target, this.gl_, this.nvInterop_);

            img.BeginDraw();
            this.device_.ImmediateContext.OutputMerger.SetTargets(img.RenderTargetView);

            this.pendingImages_.Remove(img);
            var rv = new AnonymousDisposable(() => {
                img.Present();
                this.pendingImages_.Add(img);
            });
            image = img;
            return rv;
        }
    }

    public sealed class D3D11SwapchainImage
    {
        public PixelSize Size { get; }
        private readonly ICompositionGpuInterop interop_;
        private readonly CompositionDrawingSurface target_;
        private readonly Texture2D texture_;
        public Texture2D Texture => this.texture_;
        private readonly KeyedMutex mutex_;
        private readonly PlatformHandle platformHandle_;
        private PlatformGraphicsExternalImageProperties properties_;
        private ICompositionImportedGpuImage? imported_;
        public Task? LastPresent { get; private set; }
        public RenderTargetView RenderTargetView { get; }

        private readonly GL gl_;
        private readonly NVDXInterop nvInterop_;

        private nint[] hCfbs_ = new nint[1];
        private readonly IntPtr hDevice_;
        private readonly uint fboId_;
        private readonly uint colorTextureId_;
        private readonly uint depthTextureId_;

        public D3D11SwapchainImage(
            IntPtr hDevice,
            D3DDevice device,
            PixelSize size,
            ICompositionGpuInterop interop,
            CompositionDrawingSurface target,
            GL gl,
            NVDXInterop nvInterop
            )
        {
            this.Size = size;
            this.interop_ = interop;
            this.target_ = target;
            this.gl_ = gl;
            this.nvInterop_ = nvInterop;

            this.texture_ = new Texture2D(
                device,
                new Texture2DDescription
                {
                    Format = Format.B8G8R8A8_UNorm,
                    Width = size.Width,
                    Height = size.Height,
                    ArraySize = 1,
                    MipLevels = 1,
                    SampleDescription = new SampleDescription { Count = 1, Quality = 0 },
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.SharedKeyedmutex,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                }
            );
            this.mutex_ = this.texture_.QueryInterface<KeyedMutex>();

            using (var res = this.texture_.QueryInterface<DxgiResource>())
            {
                var handle = res.SharedHandle;
                this.platformHandle_ = new PlatformHandle(
                    handle,
                    KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle
                );
            }

            this.properties_ = new PlatformGraphicsExternalImageProperties
            {
                Width = size.Width,
                Height = size.Height,
                Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm
            };

            this.RenderTargetView = new RenderTargetView(device, this.texture_);

            this.hDevice_ = hDevice;
            this.fboId_ = this.gl_.GenFramebuffer();
            this.colorTextureId_ = this.gl_.GenTexture();
            this.depthTextureId_ = this.gl_.GenTexture();


            // var hCfb = Wgl.DXRegisterObjectNV(
            //     hDevice,
            //     this.Texture.NativePointer, // wrong?
            //     this.colorTextureId_,
            //     (int)TextureTarget2d.Texture2D,
            //     WGL_NV_DX_interop.AccessReadWrite
            // );

            unsafe
            {

                var hCfb = this.nvInterop_.DxregisterObject(
                    this.hDevice_,
                    (void*)this.Texture.NativePointer,
                    this.colorTextureId_,
                    (NV)TextureTarget.Texture2D,// NV.TextureRectangleNV, // (uint)TextureTarget.Texture2D,
                    NV.AccessReadWriteNV // WGL_ACCESS_READ_WRITE_NV
                );
            

                if (hCfb == IntPtr.Zero)
                {
                    throw new Exception("DXRegisterObjectNV failed");
                }

                this.hCfbs_[0] = hCfb;

                // var lockResult = Wgl.DXLockObjectsNV(hDevice, 1, this.hCfbs_);
                var lockResult = this.nvInterop_.DxlockObjects(this.hDevice_, 1, this.hCfbs_);
                if (!lockResult)
                {
                    throw new Exception($"DXLockObjectsNV failed {GetLastError()}");
                }

            }

            this.gl_.BindTexture(TextureTarget.Texture2D, this.depthTextureId_);
            unsafe
            {
                this.gl_.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent32, (uint)size.Width, (uint)size.Height, 0, GLEnum.DepthComponent, PixelType.UnsignedInt, null);
            }
            // things go horribly wrong if DepthComponent's Bitcount does not match the main Framebuffer's Depth
            this.gl_.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            this.gl_.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            this.gl_.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            this.gl_.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            this.gl_.BindFramebuffer(FramebufferTarget.Framebuffer, this.fboId_);
            this.gl_.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, this.colorTextureId_, 0);
            this.gl_.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, this.depthTextureId_, 0);

            var fbStatus = this.gl_.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (fbStatus != GLEnum.FramebufferComplete)
            {
                throw new Exception($"incomplete framebuffer: {fbStatus}");
            }

            var unlockResult = this.nvInterop_.DxunlockObjects(this.hDevice_, 1, this.hCfbs_);
            if (!unlockResult)
            {
                throw new Exception($"DXUnlockObjectsNV failed {GetLastError()}");
            }
        }

        private readonly object lock_ = new();

        public void BeginDraw()
        {
            lock (lock_)
            {
                this.mutex_.Acquire(0, int.MaxValue);

                // Needs to lock here to prevent flickering.
                var lockResult = this.nvInterop_.DxlockObjects(this.hDevice_, 1, this.hCfbs_);
                if (!lockResult)
                {
                    throw new Exception($"DXLockObjectsNV failed {GetLastError()}");
                }

                this.gl_.BindFramebuffer(FramebufferTarget.Framebuffer, this.fboId_);
            }
        }

        public void Present()
        {
            lock (lock_)
            {
                // Needs to unlock here to prevent flickering.
                var unlockResult = this.nvInterop_.DxunlockObjects(this.hDevice_, 1, hCfbs_);
                if (!unlockResult)
                {
                    throw new Exception($"DXUnlockObjectsNV failed {GetLastError()}");
                }

                this.gl_.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

                this.mutex_.Release(1);
                this.imported_ ??= this.interop_.ImportImage(
                    this.platformHandle_,
                    this.properties_
                );
                this.LastPresent =
                    this.target_.UpdateWithKeyedMutexAsync(this.imported_, 1, 0);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (this.LastPresent != null)
            {
                try
                {
                    await this.LastPresent;
                }
                catch
                {
                    // Ignore
                }
            }

            this.RenderTargetView.Dispose();
            this.mutex_.Dispose();
            this.texture_.Dispose();

            if (this.hCfbs_[0] != 0)
            {
                this.nvInterop_.DxunregisterObject(this.hDevice_, this.hCfbs_[0]);
            }

            this.gl_.DeleteFramebuffer(this.fboId_);
            this.gl_.DeleteTexture(this.colorTextureId_);
            this.gl_.DeleteTexture(this.depthTextureId_);
        }

        [DllImport("Kernel32.dll")]
        public static extern int GetLastError();
    }
}
