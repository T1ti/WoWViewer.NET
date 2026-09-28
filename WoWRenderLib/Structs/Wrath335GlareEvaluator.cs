using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>Build 12340 DNGlare time, cloud, and visibility response.</summary>
public sealed class Wrath335GlareEvaluator(bool moon)
{
    public const string SunTexturePath = @"Textures\sunGlare.blp";
    public const string MoonTexturePath = @"Textures\moonGlare.blp";
    private const float SunRiseRate = 4f;
    private const float MoonRiseRate = 3.030303f;
    private const float FallRate = 1.5151515f;
    private const float SunSizeMinimum = 3f;
    private const float SunSizeMaximum = 20f;
    private const float MoonSize = 2f;
    private const float AlignmentFloor = 0.69999999f;
    private const float SunAlphaMinimum = 0.5f;
    private const float MoonAlphaMinimum = 0.1f;

    private static readonly Vector2[] SunTimeCurve =
    [
        new(0.27083334f, 0f),
        new(0.3125f, 1f),
        new(0.8125f, 1f),
        new(0.875f, 0f)
    ];
    private static readonly Vector2[] MoonTimeCurve =
    [
        new(0.083333336f, 1f),
        new(0.13541667f, 0f),
        new(0.94791669f, 0f),
        new(0.99930561f, 1f)
    ];

    public float SmoothedVisibility { get; private set; }

    public Wrath335GlareFrame Evaluate(
        long lightTime, float elapsedSeconds, float geometricVisibility,
        float sampledCloudDensity, float skyboxWeight, float alignment,
        float tintAlpha)
    {
        var phase = DayNight.NormalizeTime(
            (int)(lightTime % DayNight.GameDayLength)) /
            (float)DayNight.GameDayLength;
        var timeWeight = DayNight.InterpolateCircularTable(
            moon ? MoonTimeCurve : SunTimeCurve, phase);
        var cloudWeight = moon
            ? 1f - MathF.Abs(sampledCloudDensity * 2f - 1f)
            : 1f - sampledCloudDensity;
        var target = Math.Clamp(geometricVisibility, 0f, 1f) *
            Math.Clamp(cloudWeight, 0f, 1f) *
            (1f - Math.Clamp(skyboxWeight, 0f, 1f)) *
            Math.Clamp(timeWeight, 0f, 1f);
        var rate = target > SmoothedVisibility
            ? moon ? MoonRiseRate : SunRiseRate
            : FallRate;
        var maximumChange = Math.Max(0f, elapsedSeconds) * rate;
        SmoothedVisibility += Math.Clamp(
            target - SmoothedVisibility, -maximumChange, maximumChange);

        var normalizedAlignment =
            (Math.Clamp(alignment, AlignmentFloor, 1f) - AlignmentFloor) /
            (1f - AlignmentFloor);
        var size = moon
            ? MoonSize
            : SunSizeMinimum +
                (SunSizeMaximum - SunSizeMinimum) * normalizedAlignment;
        var alphaFactor = (moon ? MoonAlphaMinimum : SunAlphaMinimum) +
            (1f - (moon ? MoonAlphaMinimum : SunAlphaMinimum)) *
            normalizedAlignment;
        var alphaByte = (byte)(int)(byte.MaxValue *
            Math.Clamp(tintAlpha, 0f, 1f) *
            alphaFactor * SmoothedVisibility);
        return new Wrath335GlareFrame(size, alphaByte / (float)byte.MaxValue);
    }
}

public readonly record struct Wrath335GlareFrame(float Size, float Opacity);
