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

/// <summary>UI-safe immutable projection of the renderer's active lighting.</summary>
public sealed record LightingSettingsSnapshot(
    long LightParamId,
    long Time,
    Vector3 LightDirection,
    Vector3 AmbientColor,
    Vector3 DiffuseColor,
    Vector3 OceanCloseColor,
    Vector3 OceanFarColor,
    Vector3 RiverCloseColor,
    Vector3 RiverFarColor,
    float WaterShallowAlpha,
    float WaterDeepAlpha,
    float OceanShallowAlpha,
    float OceanDeepAlpha,
    bool HasLiquidColorData,
    bool HasLiquidAlphaData);
