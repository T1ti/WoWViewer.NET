using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>Client start/end/rate and original packed RGB fog color.</summary>
public readonly record struct Wrath335FogState(
    float StartDistance, float EndDistance, float Rate, uint Color);

/// <summary>
/// Outdoor-to-MFOG stage of DayNight::UpdateFog. Liquid and weather overrides
/// are separate client stages and are not represented here.
/// </summary>
public static class Wrath335InteriorFog
{
    public const float PortalDistanceScale = 0.04f;
    public const float MinimumMfogEnd = 30f;
    public const float DefaultRate = 1f;
    private const float ColorAlphaScale = 255f;

    public static Wrath335FogState FromOutdoor(
        Wrath335OutdoorFog fog, Vector3 color) => new(
            fog.StartDistance, fog.EndDistance, fog.Rate, PackColor(color));

    public static Wrath335FogState EvaluateTarget(
        WmoFogBand band, float farClipDistance, bool expansionMode)
    {
        var end = MathF.Min(farClipDistance, band.End);
        var start = band.StartScalar * end;
        var rate = DefaultRate;
        end = MathF.Max(end, MinimumMfogEnd);
        if (expansionMode)
        {
            rate = Wrath335OutdoorFogEvaluator.CalculateExpansionRate(
                start, end, farClipDistance);
            end = farClipDistance;
            start = MathF.Max(start, 0f);
        }
        return new Wrath335FogState(start, end, rate, band.Color);
    }

    public static Wrath335FogState BlendPortal(
        Wrath335FogState outdoor, Wrath335FogState target,
        float distanceToExteriorPortal)
    {
        var weight = Math.Clamp(distanceToExteriorPortal * PortalDistanceScale, 0f, 1f);
        var alpha = (int)(weight * ColorAlphaScale);
        return new Wrath335FogState(
            outdoor.StartDistance + (target.StartDistance - outdoor.StartDistance) * weight,
            outdoor.EndDistance + (target.EndDistance - outdoor.EndDistance) * weight,
            outdoor.Rate + (target.Rate - outdoor.Rate) * weight,
            Wrath335WmoFogVolumes.LerpPackedRgb(outdoor.Color, target.Color, alpha));
    }

    /// <summary>
    /// The client publishes the blended distances and rate to both fog banks.
    /// Terrain and exterior WMO batches retain the outdoor color in the staged bank.
    /// </summary>
    public static Wrath335FogState StageOutdoorColor(
        Wrath335FogState outdoor, Wrath335FogState current) =>
        current with { Color = outdoor.Color };

    public static Vector3 UnpackColor(uint color) => new(
        ((color >> 16) & 0xFF) / ColorAlphaScale,
        ((color >> 8) & 0xFF) / ColorAlphaScale,
        (color & 0xFF) / ColorAlphaScale);

    private static uint PackColor(Vector3 color)
    {
        static uint Component(float channel) =>
            (uint)Math.Clamp((int)(channel * ColorAlphaScale), 0, 255);
        return 0xFF000000u | Component(color.X) << 16 |
               Component(color.Y) << 8 | Component(color.Z);
    }
}
