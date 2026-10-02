using System.Numerics;

namespace WoWRenderLib.Renderer;

/// <summary>12340's major-axis polygon crossing rule (0x9829B0 / 0x9830D0).</summary>
public static class Wrath335PortalPolygon
{
    // 0x984E50 / 0x7D78C0 project along the source plane normal through
    // 0x982FB0. Within its 0.01 tolerance, crossing uses the original point.
    public static Vector3 ProjectForCrossing(Vector3 point, Vector3 normal, float signedPlaneDistance)
    {
        var normalLengthSquared = normal.LengthSquared();
        return MathF.Abs(signedPlaneDistance) < 0.01f || normalLengthSquared == 0f
            ? point : point - normal * (signedPlaneDistance / normalLengthSquared);
    }

    public static bool Contains(Vector3 point, ReadOnlySpan<Vector3> vertices, Vector3 normal)
    {
        if (vertices.Length < 3)
            return false;
        var dropped = MajorAxis(normal);
        var horizontal = dropped == 0 ? 1 : dropped == 1 ? 2 : 0;
        var vertical = dropped == 0 ? 2 : dropped == 1 ? 0 : 1;
        var x = Component(point, horizontal);
        var y = Component(point, vertical);
        var previous = vertices[^1];
        var previousAbove = y <= Component(previous, vertical);
        var inside = false;
        foreach (var current in vertices)
        {
            var currentAbove = y <= Component(current, vertical);
            if (previousAbove != currentAbove)
            {
                var left = ((double)Component(current, horizontal) - x) *
                    ((double)Component(previous, vertical) - Component(current, vertical));
                var right = ((double)Component(previous, horizontal) - Component(current, horizontal)) *
                    ((double)Component(current, vertical) - y);
                if ((left <= right) == currentAbove)
                    inside = !inside;
            }
            previous = current;
            previousAbove = currentAbove;
        }
        return inside;
    }

    public static int MajorAxis(Vector3 normal)
    {
        var n = Vector3.Abs(normal);
        return n.X <= n.Y ? n.Y > n.Z ? 1 : 2 : n.X > n.Z ? 0 : 2;
    }

    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
