using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>Client texture path, camera-relative center, and full quad size.</summary>
public readonly record struct Wrath335CelestialBody(
    string ClientTexturePath,
    Vector3 CameraRelativeCenter,
    float Size);

public readonly record struct Wrath335CelestialFrame(
    Wrath335CelestialBody Sun,
    Wrath335CelestialBody Moon1,
    Wrath335CelestialBody Moon02,
    float DayNightProminence);

/// <summary>Fixed celestial curves from Wow.exe 3.3.5 build 12340.</summary>
public static class Wrath335CelestialEvaluator
{
    public const string SunTexturePath = @"Textures\sunCenter.blp";
    public const string Moon1TexturePath = @"Textures\moon.blp";
    public const string Moon02TexturePath = @"Textures\moon02.blp";
    public const float OrbitalDistance = 12f;
    public const float SunSizeScale = 1f;
    public const float Moon1SizeScale = 1.75f;
    public const float Moon02SizeScale = 1f;
    public const float Moon02Period = 1.7f;
    public const float Moon02PhaseResolution = 65536f;
    public const float Moon02PhaseFloorBias = 0.5f;
    public const float NightFadeEndPhase = 0.16666667f;
    public const float DawnRiseStartPhase = 0.22916667f;
    public const float DayPeakPhase = 0.5f;
    public const float DuskFadeEndPhase = 0.89583331f;
    public const float NightRiseStartPhase = 0.91666669f;
    public const float NightFallSlope = 6f;
    public const float DawnRiseSlope = 3.6923077f;
    public const float DuskFadeSlope = 2.5263159f;
    public const float NightRiseSlope = 12.000003f;

    private static readonly Vector2[] SunPolarCurve =
    [
        new(0.22916667f, 1.7453293f),
        new(0.49652779f, 0.087266468f),
        new(0.5f, 0.087266468f),
        new(0.50347221f, 0.087266468f),
        new(0.89583331f, 1.7453293f)
    ];
    private static readonly Vector2[] SunAzimuthCurve =
    [
        new(0.22916667f, 0.78539819f),
        new(0.5f, 0.78539819f),
        new(0.89583331f, 0.78539819f)
    ];
    private static readonly Vector2[] MoonPolarCurve =
    [
        new(0f, 0.61086524f),
        new(0.0034722222f, 0.61086524f),
        new(0.16666667f, 1.7453293f),
        new(0.91666669f, 1.7453293f),
        new(0.99652779f, 0.61086524f)
    ];
    private static readonly Vector2[] Moon1AzimuthCurve =
    [
        new(0f, 0.78539819f),
        new(0.16666667f, 0.78539819f),
        new(0.91666669f, 0.78539819f)
    ];
    private static readonly Vector2[] Moon02AzimuthCurve =
    [
        new(0f, 2.3561945f),
        new(0.16666667f, 2.6179938f),
        new(0.91666669f, 2.8797934f)
    ];
    private static readonly Vector2[] SunSizeCurve =
    [
        new(0.25f, 2f),
        new(0.28125f, 1f),
        new(0.84375f, 1f),
        new(0.875f, 2f)
    ];
    private static readonly Vector2[] MoonSizeCurve =
    [
        new(0.041666672f, 1f),
        new(0.16666667f, 1.5f),
        new(0.91666669f, 1.5f),
        new(0.99930561f, 1f)
    ];

    public static Wrath335CelestialFrame Evaluate(
        int time, float moon02PhaseOffset = 0f)
    {
        var phase = DayNight.NormalizeTime(time) / (float)DayNight.GameDayLength;
        var moon02Phase = CalculateMoon02Phase(phase, moon02PhaseOffset);
        return new Wrath335CelestialFrame(
            CreateBody(SunTexturePath, SunPolarCurve, SunAzimuthCurve,
                SunSizeCurve, phase, SunSizeScale),
            CreateBody(Moon1TexturePath, MoonPolarCurve, Moon1AzimuthCurve,
                MoonSizeCurve, phase, Moon1SizeScale),
            CreateBody(Moon02TexturePath, MoonPolarCurve, Moon02AzimuthCurve,
                MoonSizeCurve, moon02Phase, Moon02SizeScale),
            CalculateProminence(phase));
    }

    private static Wrath335CelestialBody CreateBody(
        string texturePath,
        ReadOnlySpan<Vector2> polarCurve,
        ReadOnlySpan<Vector2> azimuthCurve,
        ReadOnlySpan<Vector2> sizeCurve,
        float phase,
        float sizeScale)
    {
        var polar = DayNight.InterpolateCircularTable(polarCurve, phase) *
            Wrath335SkyReference.InversePi;
        var azimuth = DayNight.InterpolateCircularTable(azimuthCurve, phase) *
            Wrath335SkyReference.InversePi;
        var sinPolar = Wrath335SkyReference.CubicCosine(
            polar - Wrath335SkyReference.SinePhaseShift);
        var direction = new Vector3(
            Wrath335SkyReference.CubicCosine(azimuth) * sinPolar,
            Wrath335SkyReference.CubicCosine(
                azimuth - Wrath335SkyReference.SinePhaseShift) * sinPolar,
            Wrath335SkyReference.CubicCosine(polar));
        var distanceScale = OrbitalDistance / MathF.Sqrt(direction.LengthSquared());
        return new Wrath335CelestialBody(
            texturePath,
            direction * distanceScale,
            DayNight.InterpolateCircularTable(sizeCurve, phase) * sizeScale);
    }

    private static float CalculateMoon02Phase(float dayPhase, float phaseOffset)
    {
        // SetPlanets quantizes the two phase inputs to 16-bit fractions
        // before wrapping the moon02 orbit's 1.7-day period.
        var dayTicks = (int)(dayPhase * Moon02PhaseResolution - Moon02PhaseFloorBias);
        var offsetTicks = (int)(phaseOffset * Moon02PhaseResolution - Moon02PhaseFloorBias);
        var phaseTicks = dayTicks + offsetTicks;
        var periodStartTicks = (int)(MathF.Floor((phaseOffset + dayPhase) /
            Moon02Period) * Moon02Period * Moon02PhaseResolution - Moon02PhaseFloorBias);
        var wrappedTicks = phaseTicks - Math.Min(periodStartTicks, phaseTicks);
        return wrappedTicks / Moon02PhaseResolution / Moon02Period;
    }

    private static float CalculateProminence(float phase)
    {
        if (phase >= DawnRiseStartPhase && phase < DayPeakPhase)
            return (phase - DawnRiseStartPhase) * DawnRiseSlope;
        if (phase >= DayPeakPhase && phase < DuskFadeEndPhase)
            return 1f - (phase - DayPeakPhase) * DuskFadeSlope;
        if (phase >= NightRiseStartPhase)
            return (phase - NightRiseStartPhase) * NightRiseSlope;
        return phase < NightFadeEndPhase ? 1f - phase * NightFallSlope : 0f;
    }
}
