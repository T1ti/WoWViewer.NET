namespace WoWRenderLib.Structs;

/// <summary>Base outdoor fog after the 3.3.5 band and far-clip rules.</summary>
public readonly record struct Wrath335OutdoorFog(
    float StartDistance,
    float EndDistance,
    float Rate);

/// <summary>
/// Pure outdoor subset of DayNight::FillColorArrayFromLightParams,
/// UpdateColorsAndFogRate, CalcFogRate, and UpdateFog. Portal, underwater,
/// weather, and override fog are applied after this stage by the client.
/// </summary>
public static class Wrath335OutdoorFogEvaluator
{
    public const float MinimumBandEndDistance = 10f;
    public const float ExpansionBandThreshold = 27.777779f;
    public const float RateFarClipCap = 700f;
    public const float RateFarClipOffset = 200f;
    public const float RateScale = 5.5f;
    public const float MinimumExpansionRate = 1.5f;
    public const float DefaultRate = 1f;

    /// <param name="bandEndDistance">Sampled float band 0, already converted to world units.</param>
    /// <param name="bandStartScalar">Sampled float band 1.</param>
    /// <param name="farClipDistance">Validated client far clip in world units.</param>
    /// <param name="expansionMode">Client shader-capable mode on maps 530 and later.</param>
    public static Wrath335OutdoorFog Evaluate(
        float bandEndDistance,
        float bandStartScalar,
        float farClipDistance,
        bool expansionMode)
    {
        var endDistance = MathF.Max(bandEndDistance, MinimumBandEndDistance);
        var startScalar = Math.Clamp(bandStartScalar, -1f, 1f);
        var rate = DefaultRate;

        if (expansionMode)
        {
            if (endDistance >= ExpansionBandThreshold)
            {
                rate = CalculateExpansionRate(startScalar * endDistance,
                    endDistance, farClipDistance);
                endDistance = farClipDistance;
            }

            // The client calculates the rate before the mode-specific
            // nonnegative clamp on the start scalar.
            startScalar = MathF.Max(startScalar, 0f);
        }

        endDistance = MathF.Min(endDistance, farClipDistance);
        return new Wrath335OutdoorFog(
            endDistance * startScalar, endDistance, rate);
    }

    public static float CalculateExpansionRate(
        float startDistance,
        float endDistance,
        float farClipDistance)
    {
        var rateRange = MathF.Min(farClipDistance, RateFarClipCap) - RateFarClipOffset;
        var fogWidth = endDistance - startDistance;
        return fogWidth <= rateRange
            ? (1f - fogWidth / rateRange) * RateScale + MinimumExpansionRate
            : MinimumExpansionRate;
    }
}
