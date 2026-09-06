namespace WoWRenderLib.DX11.Editing;

/// <summary>
/// Shared brush math. Geometry traversal and tool operations should use this
/// instead of implementing their own falloff curves.
/// </summary>
public static class BrushMath
{
    public static float CalculateDistance(float deltaX, float deltaY, BrushShape shape) =>
        shape switch
        {
            BrushShape.Circle => MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY)),
            BrushShape.Square => MathF.Max(MathF.Abs(deltaX), MathF.Abs(deltaY)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unsupported brush shape.")
        };

    public static float CalculateInfluence(float deltaX, float deltaY, in BrushInput brush)
    {
        var radius = MathF.Max(0.001f, brush.Radius);
        var distance = CalculateDistance(deltaX, deltaY, brush.Shape);
        if (distance > radius)
            return 0f;

        if (!brush.HasFalloff)
            return 1f;

        var fullStrengthRadius = radius * Math.Clamp(brush.Falloff, 0f, 1f);
        return CalculateFalloff(distance, radius, fullStrengthRadius, brush.FalloffProfile);
    }

    public static float CalculateFalloff(
        float distance,
        float radius,
        float fullStrengthRadius,
        BrushFalloffProfile profile = BrushFalloffProfile.Smooth)
    {
        if (distance <= fullStrengthRadius)
            return 1f;

        var width = MathF.Max(0.001f, radius - fullStrengthRadius);
        var t = Math.Clamp((distance - fullStrengthRadius) / width, 0f, 1f);
        return profile switch
        {
            BrushFalloffProfile.Hard => 1f,
            BrushFalloffProfile.Linear => 1f - t,
            BrushFalloffProfile.Smooth => 1f - (t * t * (3f - 2f * t)),
            BrushFalloffProfile.Gaussian => CalculateNormalizedGaussian(t),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported falloff profile.")
        };
    }

    private static float CalculateNormalizedGaussian(float t)
    {
        const float Sigma = 1f / 3f;
        var edge = MathF.Exp(-0.5f / (Sigma * Sigma));
        var value = MathF.Exp(-0.5f * t * t / (Sigma * Sigma));
        return Math.Clamp((value - edge) / (1f - edge), 0f, 1f);
    }
}
