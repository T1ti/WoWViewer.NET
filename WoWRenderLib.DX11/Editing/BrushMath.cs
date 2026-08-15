namespace WoWRenderLib.DX11.Editing;

/// <summary>
/// Shared brush math. Geometry traversal and tool operations should use this
/// instead of implementing their own falloff curves.
/// </summary>
public static class BrushMath
{
    public static float CalculateFalloff(float distance, float radius, float innerRadius)
    {
        if (distance <= innerRadius)
            return 1f;

        var width = MathF.Max(0.001f, radius - innerRadius);
        var t = Math.Clamp((distance - innerRadius) / width, 0f, 1f);
        return 1f - (t * t * (3f - 2f * t));
    }
}
