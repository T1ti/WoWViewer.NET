using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Editing;

/// <summary>Six planes of a screen rectangle's perspective volume, including the near/far clip planes.</summary>
internal readonly struct ScreenSelectionVolume
{
    private readonly Vector4 _left, _right, _bottom, _top, _near, _far;

    public ScreenSelectionVolume(Vector2 start, Vector2 end, Vector2 viewport, Matrix4x4 viewProjection)
    {
        var minimum = Vector2.Min(start, end);
        var maximum = Vector2.Max(start, end);
        // A long horizontal or vertical drag still gets a one-pixel selection area.
        maximum = Vector2.Max(maximum, minimum + Vector2.One);
        var left = minimum.X * 2 / viewport.X - 1;
        var right = maximum.X * 2 / viewport.X - 1;
        var bottom = 1 - maximum.Y * 2 / viewport.Y;
        var top = 1 - minimum.Y * 2 / viewport.Y;
        var x = new Vector4(viewProjection.M11, viewProjection.M21, viewProjection.M31, viewProjection.M41);
        var y = new Vector4(viewProjection.M12, viewProjection.M22, viewProjection.M32, viewProjection.M42);
        var z = new Vector4(viewProjection.M13, viewProjection.M23, viewProjection.M33, viewProjection.M43);
        var w = new Vector4(viewProjection.M14, viewProjection.M24, viewProjection.M34, viewProjection.M44);
        _left = x - left * w;
        _right = right * w - x;
        _bottom = y - bottom * w;
        _top = top * w - y;
        _near = z;
        _far = w - z;
    }

    public bool Intersects(BoundingBox box) =>
        Finite(box.Min) && Finite(box.Max) &&
        Inside(_left, box) && Inside(_right, box) && Inside(_bottom, box) &&
        Inside(_top, box) && Inside(_near, box) && Inside(_far, box);

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Inside(Vector4 plane, BoundingBox box)
    {
        var positive = new Vector4(plane.X >= 0 ? box.Max.X : box.Min.X,
            plane.Y >= 0 ? box.Max.Y : box.Min.Y, plane.Z >= 0 ? box.Max.Z : box.Min.Z, 1);
        return Vector4.Dot(plane, positive) >= 0;
    }
}
