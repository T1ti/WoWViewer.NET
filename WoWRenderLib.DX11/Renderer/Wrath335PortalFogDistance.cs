using System.Numerics;
using GroupFlags = WoWLib.Formats.WMO.Group.Chunks.GroupFlags;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Renderer;

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
    internal const uint ExteriorGroupFlags =
        (uint)(GroupFlags.Exterior | GroupFlags.ExteriorLit);
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
                    viewerLocal, wmo.portals[link.PortalIndex], wmo.wrath335);
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

    internal static float DistanceToPolygon(Vector3 point, in WmoPortal portal, bool wrath335 = true)
    {
        var vertices = portal.Vertices;
        if (vertices == null || vertices.Length < 3)
            return float.PositiveInfinity;
        var normalLengthSquared = portal.Normal.LengthSquared();
        if (normalLengthSquared <= DegenerateEdgeLengthSquared)
            return float.PositiveInfinity;

        var signedPlaneDistance = Vector3.Dot(portal.Normal, point) + portal.Distance;
        var projected = wrath335
            ? Wrath335PortalPolygon.ProjectForCrossing(point, portal.Normal, signedPlaneDistance)
            : point - portal.Normal * (signedPlaneDistance / normalLengthSquared);
        var inside = wrath335 ? Wrath335PortalPolygon.Contains(projected, vertices, portal.Normal) : true;
        if (!wrath335)
        {
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
        }
        if (inside)
            // 0x984F3B returns the absolute source-plane evaluation without
            // normalizing it. Other clients retain the previous metric.
            return wrath335 ? MathF.Abs(signedPlaneDistance)
                : MathF.Abs(signedPlaneDistance) / MathF.Sqrt(normalLengthSquared);

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
