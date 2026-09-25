namespace WoWRenderLib.DX11.Renderer;

/// <summary>Effect cutoff using the same bounding-sphere distance rule as M2 visibility.</summary>
internal static class M2EffectDistancePolicy
{
    public static bool IsWithin(float distanceSquared, float radius,
        float modelRenderDistance, float percent)
    {
        if (percent <= 0f || modelRenderDistance <= 0f ||
            !float.IsFinite(distanceSquared) || !float.IsFinite(radius))
            return false;

        var limit = modelRenderDistance * Math.Clamp(percent, 0f, 100f) / 100f
                    + Math.Max(0f, radius);
        return distanceSquared <= limit * limit;
    }
}
