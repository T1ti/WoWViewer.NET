using System.Numerics;

namespace WTEditor.Application.Models;

public enum RendererLifecycleState
{
    Detached,
    Initializing,
    LoadingContent,
    Ready,
    Failed,
    Disposed
}

public sealed record RendererStatus(
    RendererLifecycleState State,
    string Message,
    string? Error = null)
{
    public bool IsBusy => State is RendererLifecycleState.Initializing or RendererLifecycleState.LoadingContent;
    public bool HasError => State == RendererLifecycleState.Failed;
}

public sealed record ViewportTelemetry(
    double FramesPerSecond,
    double FrameTimeMilliseconds,
    Vector3 CameraPosition,
    Vector3 CameraDirection,
    int DrawCalls,
    long SubmittedTriangleCount,
    double PresentationIntervalMilliseconds = 0,
    uint ActiveWdtFileDataId = 0);
