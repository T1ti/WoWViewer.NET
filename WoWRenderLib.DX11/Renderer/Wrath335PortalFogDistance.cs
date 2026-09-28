using System.Numerics;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// The geometry/range stage of CMapObj::DistFromClosestExtPortal. It follows
/// at most three interior links and uses the closest point on each exterior
/// portal polygon rather than its center or its infinite plane.
/// </summary>
internal static class Wrath335PortalFogDistance
{
    internal const float MaximumDistance = 25f;
    internal const int MaximumDepth = 3;
    private const uint ExteriorGroupFlags = 0x48;
    private const float DegenerateEdgeLengthSquared = 0.000001f;

    public static bool TryFind(in WorldModel wmo, int viewerGroupIndex,
        Vector3 viewerLocal, out float distance)
    {
        distance = MaximumDistance;
        if (wmo.groupBatches == null || wmo.portals == null ||
            (uint)viewerGroupIndex >= (uint)wmo.groupBatches.Length)
            return false;

        Search(wmo, viewerGroupIndex, -1, 0, viewerLocal, ref distance);
        return distance < MaximumDistance;
    }

    private static void Search(in WorldModel wmo, int groupIndex,
        int previousGroupIndex, int depth, Vector3 viewerLocal,
        ref float closest)
    {
        if (depth > MaximumDepth)
            return;
        foreach (var link in wmo.groupBatches[groupIndex].portalLinks)
        {
            var targetIndex = link.TargetGroupIndex;
            if (targetIndex >= wmo.groupBatches.Length ||
                targetIndex == previousGroupIndex ||
                link.PortalIndex >= wmo.portals.Length)
                continue;

            if ((wmo.groupBatches[targetIndex].mogiFlags & ExteriorGroupFlags) != 0)
            {
                var candidate = DistanceToPolygon(
                    viewerLocal, wmo.portals[link.PortalIndex]);
                if (candidate < closest)
                    closest = candidate;
            }
            else
            {
                Search(wmo, targetIndex, groupIndex, depth + 1,
                    viewerLocal, ref closest);
            }
        }
    }

    internal static float DistanceToPolygon(Vector3 point, in WmoPortal portal)
    {
        var vertices = portal.Vertices;
        if (vertices == null || vertices.Length < 3)
            return float.PositiveInfinity;
        var normalLengthSquared = portal.Normal.LengthSquared();
        if (normalLengthSquared <= DegenerateEdgeLengthSquared)
            return float.PositiveInfinity;

        var signedPlaneDistance = Vector3.Dot(portal.Normal, point) + portal.Distance;
        var projected = point - portal.Normal * (signedPlaneDistance / normalLengthSquared);
        var inside = true;
        var winding = 0f;
        for (var i = 0; i < vertices.Length; i++)
        {
            var edge = vertices[(i + 1) % vertices.Length] - vertices[i];
            var side = Vector3.Dot(Vector3.Cross(edge, projected - vertices[i]), portal.Normal);
            if (MathF.Abs(side) <= DegenerateEdgeLengthSquared)
                continue;
            if (winding == 0f)
                winding = MathF.Sign(side);
            else if (side * winding < 0f)
            {
                inside = false;
                break;
            }
        }
        if (inside)
            return MathF.Abs(signedPlaneDistance) / MathF.Sqrt(normalLengthSquared);

        var closestSquared = float.PositiveInfinity;
        for (var i = 0; i < vertices.Length; i++)
        {
            var start = vertices[i];
            var edge = vertices[(i + 1) % vertices.Length] - start;
            var lengthSquared = edge.LengthSquared();
            var along = lengthSquared > DegenerateEdgeLengthSquared
                ? Math.Clamp(Vector3.Dot(point - start, edge) / lengthSquared, 0f, 1f)
                : 0f;
            closestSquared = MathF.Min(closestSquared,
                Vector3.DistanceSquared(point, start + edge * along));
        }
        return MathF.Sqrt(closestSquared);
    }
}
