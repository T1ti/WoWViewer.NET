using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace WTEditor.Avalonia.Rendering;

/// <summary>
/// A bounded set of keyed-mutex D3D11 render targets. A buffer remains unavailable
/// until Avalonia has finished importing/snapshotting it, which lets the producer
/// render ahead without overwriting a texture still owned by the compositor.
/// </summary>
internal sealed class D3D11PresentationBufferPool : IAsyncDisposable
{
    private const int BufferCount = 3;
    private readonly List<D3D11PresentationBuffer> _buffers = new(BufferCount);
    private bool _disposed;

    private D3D11PresentationBufferPool()
    {
    }

    public IReadOnlyList<D3D11PresentationBuffer> Buffers => _buffers;

    public static async ValueTask<D3D11PresentationBufferPool> CreateAsync(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> deviceContext,
        ICompositionGpuInterop interop,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var pool = new D3D11PresentationBufferPool();
        try
        {
            unsafe
            {
                for (var index = 0; index < BufferCount; index++)
                    pool._buffers.Add(D3D11PresentationBuffer.Create(
                        device, deviceContext, interop, width, height));
            }
            return pool;
        }
        catch
        {
            await pool.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public D3D11PresentationBuffer? TryAcquire()
    {
        if (_disposed)
            return null;

        foreach (var buffer in _buffers)
        {
            if (buffer.TryAcquireProducer())
                return buffer;
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var buffer in _buffers)
            await buffer.DisposeAsync().ConfigureAwait(false);
        _buffers.Clear();
    }
}

internal sealed class D3D11PresentationBuffer : IAsyncDisposable
{
    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);
    private readonly ComPtr<ID3D11DeviceContext> _deviceContext;
    private bool _producerAcquired;
    private bool _disposed;
    private bool _presentationFailed;

    private D3D11PresentationBuffer(
        ComPtr<ID3D11Texture2D> texture,
        ComPtr<ID3D11RenderTargetView> renderTargetView,
        ComPtr<IDXGIKeyedMutex> keyedMutex,
        IntPtr sharedHandle,
        ICompositionImportedGpuImage importedImage,
        ComPtr<ID3D11DeviceContext> deviceContext,
        int width,
        int height)
    {
        Texture = texture;
        RenderTargetView = renderTargetView;
        KeyedMutex = keyedMutex;
        SharedHandle = sharedHandle;
        ImportedImage = importedImage;
        _deviceContext = deviceContext;
        Width = width;
        Height = height;
        LastPresent = Task.CompletedTask;
    }

    public ComPtr<ID3D11Texture2D> Texture { get; }
    public ComPtr<ID3D11RenderTargetView> RenderTargetView { get; }
    public ComPtr<IDXGIKeyedMutex> KeyedMutex { get; }
    public IntPtr SharedHandle { get; }
    public ICompositionImportedGpuImage? ImportedImage { get; private set; }
    public Task LastPresent { get; private set; }
    public int Width { get; }
    public int Height { get; }

    public static unsafe D3D11PresentationBuffer Create(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> deviceContext,
        ICompositionGpuInterop interop,
        int width,
        int height)
    {
        var texture = default(ComPtr<ID3D11Texture2D>);
        var renderTargetView = default(ComPtr<ID3D11RenderTargetView>);
        var keyedMutex = default(ComPtr<IDXGIKeyedMutex>);
        try
        {
            var textureDescription = new Texture2DDesc
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.FormatB8G8R8A8Unorm,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
                BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
                MiscFlags = (uint)ResourceMiscFlag.SharedKeyedmutex
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in textureDescription, null, ref texture));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(texture, null, ref renderTargetView));
            keyedMutex = texture.QueryInterface<IDXGIKeyedMutex>();

            var resource = texture.QueryInterface<IDXGIResource>();
            void* rawHandle = null;
            SilkMarshal.ThrowHResult(resource.GetSharedHandle(&rawHandle));
            resource.Dispose();
            // Avalonia 12.1.1 exposes the D3D11 global-shared-handle import path;
            // its PlatformHandle contract does not provide a safe NT-handle owner
            // here, so retain the global handle until the texture is released.
            var handle = (IntPtr)rawHandle;
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("D3D11 returned an invalid shared texture handle.");

            var importedImage = interop.ImportImage(
                new PlatformHandle(handle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
                new PlatformGraphicsExternalImageProperties
                {
                    Width = width,
                    Height = height,
                    Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm
                });

            return new D3D11PresentationBuffer(
                texture,
                renderTargetView,
                keyedMutex,
                handle,
                importedImage,
                deviceContext,
                width,
                height);
        }
        catch
        {
            keyedMutex.Dispose();
            renderTargetView.Dispose();
            texture.Dispose();
            throw;
        }
    }

    public bool TryAcquireProducer()
    {
        if (_disposed || _presentationFailed || _producerAcquired || !LastPresent.IsCompletedSuccessfully)
            return false;

        var result = KeyedMutex.AcquireSync(0, 0);
        if (result == 0)
        {
            _producerAcquired = true;
            return true;
        }

        if (result == DxgiErrorWaitTimeout)
            return false;

        SilkMarshal.ThrowHResult(result);
        return false;
    }

    public void ReleaseProducer()
    {
        var producerAcquired = _producerAcquired;
        _producerAcquired = false;
        if (!producerAcquired)
            return;

        // All rendering commands must be submitted before ownership changes to the
        // compositor. This is required even though Flush is asynchronous on D3D11.
        _deviceContext.Flush();
        SilkMarshal.ThrowHResult(KeyedMutex.ReleaseSync(1));
    }

    public void AbandonProducer()
    {
        if (!_producerAcquired)
            return;

        try
        {
            _deviceContext.Flush();
            SilkMarshal.ThrowHResult(KeyedMutex.ReleaseSync(1));
        }
        finally
        {
            _producerAcquired = false;
        }
    }

    public void SetLastPresent(Task presentTask)
    {
        if (presentTask == null)
            throw new ArgumentNullException(nameof(presentTask));

        LastPresent = presentTask;
        _ = ObservePresentationAsync(presentTask);
    }

    public void MarkPresentationFailed() => _presentationFailed = true;

    private async Task ObservePresentationAsync(Task presentTask)
    {
        try
        {
            await presentTask.ConfigureAwait(false);
        }
        catch
        {
            // A failed compositor update cannot be safely retried with this keyed
            // mutex state. Keep this bounded buffer out of circulation; the control
            // will recreate the pool on the next lifecycle restart.
            _presentationFailed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            await LastPresent.ConfigureAwait(false);
        }
        catch
        {
            // The image is still safe to release after the update task has completed,
            // even when the update itself failed.
        }

        if (_producerAcquired)
        {
            try
            {
                AbandonProducer();
            }
            catch
            {
            }
        }

        try
        {
            if (ImportedImage != null)
                await ImportedImage.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // A lost compositor can reject imported-object disposal. Native D3D
            // references still need to be released so the device can shut down.
        }
        finally
        {
            ImportedImage = null;
            KeyedMutex.Dispose();
            RenderTargetView.Dispose();
            Texture.Dispose();
        }
    }
}
