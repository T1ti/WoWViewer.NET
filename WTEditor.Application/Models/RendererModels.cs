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

public enum ActiveLightingSourceKind
{
    Global,
    Zone,
    Local
}

/// <summary>UI-safe description of one source in the final lighting blend.</summary>
public sealed record ActiveLightingSnapshot(
    int LightId,
    long LightParamId,
    ActiveLightingSourceKind SourceKind,
    float Weight,
    int ZoneLightId = 0,
    string? ZoneName = null);

public sealed record LightingSkyboxSnapshot(
    uint FileDataId,
    int Flags,
    float Opacity);

/// <summary>Final, spatially and temporally blended sky/atmosphere values.</summary>
public sealed record LightingRuntimeSnapshot(
    Vector3 SkyTopColor,
    Vector3 SkyMiddleColor,
    Vector3 SkyBand1Color,
    Vector3 SkyBand2Color,
    Vector3 SkySmogColor,
    Vector3 SkyFogColor,
    Vector3 SunColor,
    Vector3 CloudSunColor,
    Vector3 CloudEmissiveColor,
    Vector3 CloudLayer1AmbientColor,
    Vector3 CloudLayer2AmbientColor,
    bool HasSkyColorData,
    bool HasSunCloudData,
    float ShadowOpacity,
    float FogEnd,
    float FogScaler,
    float CloudDensity,
    float FogDensity,
    float FogHeight,
    float FogHeightScaler,
    float FogHeightDensity,
    float FogZScalar,
    float MainFogStartDistance,
    float MainFogEndDistance,
    float SunFogAngle,
    Vector3 EndFogColor,
    float EndFogColorDistance,
    float FogStartOffset,
    Vector3 SunFogColor,
    float SunFogStrength,
    Vector3 FogHeightColor,
    Vector3 EndFogHeightColor,
    Vector3 GroundAmbientColor,
    Vector3 HorizonAmbientColor,
    Vector4 FogHeightCoefficients,
    Vector4 MainFogCoefficients,
    Vector4 HeightDensityFogCoefficients,
    long ColorGradingFileDataId,
    long DarkerColorGradingFileDataId,
    bool HasFogData,
    bool HighlightSky,
    IReadOnlyList<LightingSkyboxSnapshot> Skyboxes);

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
    bool HasLiquidAlphaData,
    bool IsDynamic,
    IReadOnlyList<ActiveLightingSnapshot>? ActiveLights = null,
    LightingRuntimeSnapshot? Runtime = null);
