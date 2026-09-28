using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>3.3.5 DNStars model and time fade (build 12340).</summary>
public static class Wrath335StarEvaluator
{
    // DNStars::Init requests "stars.mdl"; the client MPQ stores this M2 path.
    public const string ModelPath = @"Environments\Stars\Stars.m2";
    public const byte MinimumVisibleAlpha = 2;
    private const float AlphaScale = 254f;
    private const float AlphaOffset = 1f;
    private const float ByteToOpacity = 1f / byte.MaxValue;

    private static readonly Vector2[] AlphaCurve =
    [
        new(0.125f, 1f),
        new(0.1875f, 0f),
        new(0.9375f, 0f),
        new(1f, 1f)
    ];

    public static byte EvaluateAlphaByte(long worldTime)
    {
        var phase = DayNight.NormalizeTime(
            (int)(worldTime % DayNight.GameDayLength)) / (float)DayNight.GameDayLength;
        var intensity = DayNight.InterpolateCircularTable(AlphaCurve, phase);
        return (byte)(int)(intensity * AlphaScale + AlphaOffset);
    }

    public static float EvaluateOpacity(long worldTime)
    {
        var alpha = EvaluateAlphaByte(worldTime);
        return alpha < MinimumVisibleAlpha ? 0f : alpha * ByteToOpacity;
    }
}
