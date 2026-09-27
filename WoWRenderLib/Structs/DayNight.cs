using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>
/// Time-dependent world lighting shared by the lighting catalog and renderers.
/// The direction and SIDN curves follow the documented Mists of Pandaria client;
/// the DNSky glow curve comes from the Wrath 3.3.5a client. Client lighting
/// tables supply the colors and spatial light selection.
/// </summary>
public static class DayNight
{
    public const int GameDayLength = 2880;

    public static Vector3 DefaultNoonSpecularColor { get; } = new(1f, 0.969f, 0.871f);

    public static int NormalizeTime(int time)
    {
        var normalized = time % GameDayLength;
        return normalized < 0 ? normalized + GameDayLength : normalized;
    }

    /// <summary>
    /// Wrath DNSky::SetColors glow curve. HighlightSky is the LightParams float,
    /// not a switch; the caller retains its value through spatial light blends.
    /// </summary>
    public static float CalculateSkyGlowStrength(int time, float highlightSkyStrength)
    {
        ReadOnlySpan<Vector2> glowCurve =
        [
            new(0.125f, 0f),
            new(0.270833343f, 1f),
            new(0.291666687f, 0f),
            new(0.854166627f, 0f),
            new(0.895833313f, 1f),
            new(0.999305546f, 0f)
        ];
        return InterpolateCircularTable(glowCurve, NormalizeTime(time) / (float)GameDayLength) *
            highlightSkyStrength;
    }

    /// <summary>Maps local wall-clock time onto WoW's 0..2880 day.</summary>
    public static int FromLocalTime(TimeSpan timeOfDay) => NormalizeTime(
        (timeOfDay.Hours * 120) +
        (timeOfDay.Minutes * 2) +
        (timeOfDay.Seconds / 30));

    /// <summary>
    /// Calculates the reference client's directional-light curve. The client
    /// vector follows the light ray; the DX11 shaders use a vector toward the
    /// light, so all three components are inverted.
    /// </summary>
    public static Vector3 CalculateLightDirection(int time)
    {
        var dayProgress = NormalizeTime(time) / (float)GameDayLength;
        ReadOnlySpan<Vector2> phiTable =
        [
            new(0f, 2.2165682f),
            new(0.25f, 1.9198622f),
            new(0.5f, 2.2165682f),
            new(0.75f, 1.9198622f)
        ];

        // All four theta keys in the documented client are the same.
        const float theta = 3.9269907f;
        var phi = InterpolateCircularTable(phiTable, dayProgress);
        var sinPhi = MathF.Sin(phi);
        var clientDirection = new Vector3(
            sinPhi * MathF.Cos(theta),
            sinPhi * MathF.Sin(theta),
            MathF.Cos(phi));
        return Vector3.Normalize(-clientDirection);
    }

    /// <summary>Night glow for WMO SIDN materials on the 0..2880 world clock.</summary>
    public static float CalculateWmoSidnPulse(long worldTime)
    {
        var time = worldTime % GameDayLength;
        if (time < 0)
            time += GameDayLength;

        var hours = time / 120f;
        if (hours < 6f)
            return 1f;
        if (hours < 7f)
            return 7f - hours;
        if (hours < 20.5f)
            return 0f;
        if (hours < 21.5f)
            return hours - 20.5f;
        return 1f;
    }

    // Wisp receives a time-resolved specular color from its caller. LightData
    // supplies our sun tint; the inverse night-glow ramp keeps the noon fallback
    // and sparse/bright sun bands from illuminating WMO highlights at night.
    public static Vector3 ResolveWmoSpecularColor(WorldSkyLighting sky, long worldTime)
    {
        var sun = sky.HasSunCloudData ? sky.SunColor : DefaultNoonSpecularColor;
        return Vector3.Clamp(sun, Vector3.Zero, Vector3.One) *
            (1f - CalculateWmoSidnPulse(worldTime));
    }

    private static float InterpolateCircularTable(ReadOnlySpan<Vector2> table, float time)
    {
        var next = 0;
        while (next < table.Length && time > table[next].X)
            next++;
        if (next == table.Length)
            next = 0;
        var previous = next == 0 ? table.Length - 1 : next - 1;
        var startTime = table[previous].X;
        var endTime = table[next].X;
        if (next == 0)
            endTime += 1f;
        if (time < startTime)
            time += 1f;
        var alpha = (time - startTime) / (endTime - startTime);
        return table[previous].Y + ((table[next].Y - table[previous].Y) * alpha);
    }
}
