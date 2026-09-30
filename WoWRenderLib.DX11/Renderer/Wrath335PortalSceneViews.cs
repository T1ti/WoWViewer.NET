using System.Numerics;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Frame-local sky/exterior unions. Rectangles use NDC; distance -1 means no view.</summary>
internal sealed class Wrath335PortalSceneViews
{
    internal const uint ViewerSkyFlags = 0x40140;
    internal const uint PortalViewFlags = 0x50148;
    internal const uint ExteriorViewFlags = 0x10008;
    public WmoPortalRect SkyRect { get; private set; }
    public WmoPortalRect ExteriorRect { get; private set; }
    public float SkyDistance { get; private set; } = -1f;
    public float ExteriorDistance { get; private set; } = -1f;
    public bool HasSkyView => SkyDistance >= 0f;
    public bool HasExteriorView => ExteriorDistance >= 0f;

    public void Reset(bool viewerPlacement, uint viewerRootFlags = 0,
        bool secondaryPlacement = false)
    {
        // 0x795400 resets both unions. 0x795D40 can seed full sky from MOGI;
        // 0x79A99E..0x79AA1C discards that seed after rendering a secondary WMO.
        SkyRect = ExteriorRect = new(float.MaxValue, float.MaxValue,
            -float.MaxValue, -float.MaxValue);
        SkyDistance = ExteriorDistance = -1f;
        if (!viewerPlacement)
        {
            SkyRect = ExteriorRect = WmoPortalRect.Full;
            SkyDistance = ExteriorDistance = 0f;
        }
        else if (!secondaryPlacement && (viewerRootFlags & ViewerSkyFlags) != 0)
        {
            SkyRect = WmoPortalRect.Full;
            SkyDistance = 0f;
        }
    }

    public void AddPortal(WmoPortalRect rect, float distance, uint destinationRootFlags)
    {
        if ((destinationRootFlags & PortalViewFlags) == 0 ||
            !IsNonEmpty(rect) || !float.IsFinite(distance) || distance < 0f)
            return;
        SkyRect = Union(SkyRect, rect);
        SkyDistance = MathF.Max(SkyDistance, distance);
        if ((destinationRootFlags & ExteriorViewFlags) != 0)
        {
            ExteriorRect = Union(ExteriorRect, rect);
            ExteriorDistance = MathF.Max(ExteriorDistance, distance);
        }
    }

    // 0x7A70D0 uses every original vertex, starts at zero, and measures along
    // the normalized local viewer axis, independent of polygon clipping.
    public static float MaximumDistance(ReadOnlySpan<Vector3> vertices,
        Vector3 eyeLocal, Vector3 directionLocal)
    {
        var squared = directionLocal.LengthSquared();
        if (squared > 0.0001f)
            directionLocal /= MathF.Sqrt(squared);
        var maximum = 0f;
        foreach (var vertex in vertices)
            maximum = MathF.Max(maximum, Vector3.Dot(vertex - eyeLocal, directionLocal));
        return maximum;
    }

    private static bool IsNonEmpty(WmoPortalRect rect) =>
        float.IsFinite(rect.MinX) && float.IsFinite(rect.MinY) &&
        float.IsFinite(rect.MaxX) && float.IsFinite(rect.MaxY) &&
        rect.MaxX > rect.MinX && rect.MaxY > rect.MinY;

    private static WmoPortalRect Union(WmoPortalRect left, WmoPortalRect right) => new(
        MathF.Min(left.MinX, right.MinX), MathF.Min(left.MinY, right.MinY),
        MathF.Max(left.MaxX, right.MaxX), MathF.Max(left.MaxY, right.MaxY));
}
