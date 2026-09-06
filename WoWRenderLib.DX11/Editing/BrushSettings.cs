namespace WoWRenderLib.DX11.Editing;

/// <summary>Shapes supported by the shared world-space brush pipeline.</summary>
public enum BrushShape
{
    Circle,
    Square
}

/// <summary>The normalized edge curve applied between the full-strength core and brush boundary.</summary>
public enum BrushFalloffProfile
{
    Hard,
    Linear,
    Smooth,
    Gaussian
}

/// <summary>
/// Tool-independent brush input used for projection, hit testing, and falloff.
/// Tool-specific values such as terrain speed or texture opacity belong to the
/// corresponding tool input instead.
/// </summary>
public readonly record struct BrushInput(
    float Radius,
    float Falloff,
    bool HasFalloff,
    BrushShape Shape,
    BrushFalloffProfile FalloffProfile);
