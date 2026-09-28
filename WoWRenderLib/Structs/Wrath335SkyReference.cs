using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>
/// Fixed sky values from Wow.exe 3.3.5 build 12340. These are reference
/// behavior, not arbitrary renderer tuning defaults.
/// </summary>
public static class Wrath335SkyReference
{
    public const int AzimuthSegments = 24;
    public const int InteriorRings = 5;
    public const float RenderScale = 6.6666665f;
    public const float VerticalOffset = 0.70710678f;
    public const float MinimumSkyDepth = 0.999023f;
    public const float SkyboxSuppressionOpacity = 0.99f;
    public const int SkyboxCombineFlag = 0x2;
    public const float PackedColorFloorBias = 0.5f;
    public const float AntisolarZenithBlend = 0.7f;
    public const float SunwardAzimuthPhase = 0.25f;
    public const float SinePhaseShift = 0.5f;
    public const float InversePi = 0.31830987f;
    public const float CubicCosineQuadraticCoefficient = 6f;
    public const float CubicCosineCubicCoefficient = 4f;
    public const float SunThetaRadians = 3.926991f;

    private static readonly float[] RingElevationValues =
        [0f, 0.17f, 0.20f, 0.23f, 0.24f, 0.25f, 1f];
    private static readonly Vector2[] AzimuthGlowValues =
    [
        new(0.125f, 1f),
        new(0.375f, 0f),
        new(0.5f, -0.5f),
        new(0.625f, -0.7f),
        new(0.75f, -0.5f),
        new(0.875f, 0f)
    ];
    private static readonly Vector2[] TimeGlowValues =
    [
        new(0.125f, 0f),
        new(0.270833343f, 1f),
        new(0.291666687f, 0f),
        new(0.854166627f, 0f),
        new(0.895833313f, 1f),
        new(0.999305546f, 0f)
    ];
    private static readonly Vector2[] SunPhiValues =
    [
        new(0f, 2.2165682f),
        new(0.25f, 1.9198623f),
        new(0.5f, 2.2165682f),
        new(0.75f, 1.9198623f)
    ];

    public static ReadOnlySpan<float> RingElevations => RingElevationValues;
    public static ReadOnlySpan<Vector2> AzimuthGlowCurve => AzimuthGlowValues;
    public static ReadOnlySpan<Vector2> TimeGlowCurve => TimeGlowValues;
    public static ReadOnlySpan<Vector2> SunPhiCurve => SunPhiValues;

    /// <summary>
    /// Client polynomial for cosine, with the angle expressed in units of pi.
    /// Used by both DNSky::Build and DayNight::SetDirection.
    /// </summary>
    public static float CubicCosine(float phase)
    {
        var wholeCycles = (int)MathF.Floor(phase);
        var fraction = phase - wholeCycles;
        var value = 1f - (CubicCosineQuadraticCoefficient -
            CubicCosineCubicCoefficient * fraction) * fraction * fraction;
        return (wholeCycles & 1) != 0 ? -value : value;
    }
}
