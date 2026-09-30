namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct SkyScissor(int Left, int Top, int Right, int Bottom);

internal static class Wrath335SkyScissor
{
    // 0x7F09B0 intersects normalized sky/view rectangles without cropping the
    // projection. 0x6A38D0's window branch flips Y, rounds low edges with +0.5
    // and high edges with +1, then clamps to the window dimensions.
    public static bool TryCreate(WmoPortalRect ndc, uint width, uint height,
        out SkyScissor result)
    {
        result = default;
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue ||
            !float.IsFinite(ndc.MinX) || !float.IsFinite(ndc.MinY) ||
            !float.IsFinite(ndc.MaxX) || !float.IsFinite(ndc.MaxY))
            return false;
        var minX = Math.Clamp((ndc.MinX + 1f) * 0.5f, 0f, 1f);
        var minY = Math.Clamp((ndc.MinY + 1f) * 0.5f, 0f, 1f);
        var maxX = Math.Clamp((ndc.MaxX + 1f) * 0.5f, 0f, 1f);
        var maxY = Math.Clamp((ndc.MaxY + 1f) * 0.5f, 0f, 1f);
        if (maxX <= minX || maxY <= minY)
            return false;
        result = new((int)((double)minX * width + 0.5),
            (int)((1.0 - maxY) * height + 0.5),
            (int)Math.Min((double)maxX * width + 1.0, width),
            (int)Math.Min((1.0 - minY) * height + 1.0, height));
        return result.Right > result.Left && result.Bottom > result.Top;
    }
}
