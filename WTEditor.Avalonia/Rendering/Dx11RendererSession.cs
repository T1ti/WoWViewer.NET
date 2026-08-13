using System;
using System.Numerics;
using System.Threading.Tasks;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using WTEditor.Application.Models;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.Rendering;

public sealed class Dx11RendererSession : IAsyncDisposable
{
    private WowViewerEngine? _engine;

    public event EventHandler<RendererStatus>? StatusChanged;

    public WowViewerEngine? Engine => _engine;

    public void Initialize(
        ClientConfiguration client,
        RenderingConfiguration rendering,
        DXGI dxgi,
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> deviceContext,
        Vector2D<int> size,
        Vector3? cameraPosition,
        Vector3? cameraDirection)
    {
        if (_engine != null)
            throw new InvalidOperationException("Dispose the current renderer before initializing another one.");

        Publish(new RendererStatus(RendererLifecycleState.Initializing, "Initializing renderer..."));

        var engine = new WowViewerEngine(client.ToDx11(), null, false)
        {
            UseKeyedMutex = true,
            InitialCameraPosition = cameraPosition,
            InitialCameraDirection = cameraDirection
        };
        engine.StatusChanged += OnEngineStatusChanged;

        try
        {
            engine.Initialize(dxgi, device, deviceContext, size);
            engine.ApplySettings(rendering.ToDx11());
            _engine = engine;
            OnEngineStatusChanged(engine, engine.Status);
        }
        catch (Exception exception)
        {
            engine.StatusChanged -= OnEngineStatusChanged;
            engine.Dispose();
            Publish(new RendererStatus(
                RendererLifecycleState.Failed,
                "Renderer initialization failed.",
                exception.Message));
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var engine = _engine;
        _engine = null;
        if (engine == null)
            return;

        engine.StatusChanged -= OnEngineStatusChanged;
        Publish(new RendererStatus(RendererLifecycleState.Detached, "Stopping renderer..."));
        await engine.DisposeAsync();
        Publish(new RendererStatus(RendererLifecycleState.Disposed, "Renderer stopped."));
    }

    private void OnEngineStatusChanged(object? sender, WowViewerEngineStatus status)
    {
        var state = status.State switch
        {
            WowViewerEngineState.Created => RendererLifecycleState.Detached,
            WowViewerEngineState.Initializing => RendererLifecycleState.Initializing,
            WowViewerEngineState.LoadingContent => RendererLifecycleState.LoadingContent,
            WowViewerEngineState.Ready => RendererLifecycleState.Ready,
            WowViewerEngineState.Failed => RendererLifecycleState.Failed,
            WowViewerEngineState.Disposed => RendererLifecycleState.Disposed,
            _ => RendererLifecycleState.Failed
        };

        Publish(new RendererStatus(state, status.Message, status.Error?.Message));
    }

    private void Publish(RendererStatus status) => StatusChanged?.Invoke(this, status);
}
