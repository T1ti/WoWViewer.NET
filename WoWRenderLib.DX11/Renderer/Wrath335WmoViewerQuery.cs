using System.Numerics;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Per-placement query recovered from 0x7D59B0. Pre-existing IDB names are
/// hypotheses. Group order and MOPR order are retained; the scene query owns
/// placement order and the two running pools.
/// </summary>
internal static class Wrath335WmoViewerQuery
{
    private const uint Exterior = 8;
    private const uint Excluded = 0x410080;
    private const float RayLength = WmoPortalVisibility.ClientViewerRayLength;

    public static void Locate(in WorldModel wmo, in Matrix4x4 model,
        Vector3 eye, ReadOnlySpan<bool> enabledGroups,
        WmoPortalVisibilityScratch scratch, float maximumDistance,
        out float nearestDistance, out bool foundHit)
    {
        // An accepted portal can increase the pool's cap beyond 1.0. Retain
        // that cap for following placements while broad phases use the full ray.
        nearestDistance = float.IsFinite(maximumDistance)
            ? Math.Max(0f, maximumDistance) : RayLength;
        foundHit = false;
        if (!TriangleMeshRaycaster.TryCreateContext(new Ray(eye, -Vector3.UnitZ),
                model, out var ray))
            return;
        var worldPerLocal = Vector3.TransformNormal(ray.LocalRay.Direction, model).Length();
        if (!float.IsFinite(worldPerLocal) || worldPerLocal <= 0f)
            return;
        var fullLocalLength = RayLength / worldPerLocal;
        if (!IntersectsSegment(ray.LocalRay, wmo.boundingBox, fullLocalLength))
            return;

        for (var index = 0; index < wmo.groupBatches.Length; index++)
        {
            var group = wmo.groupBatches[index];
            if (!enabledGroups[index] || (group.flags & Excluded) != 0 ||
                !IntersectsSegment(ray.LocalRay, group.mogiBoundingBox, fullLocalLength) ||
                group.viewerBsp is not { } bsp ||
                group.raycastVertices is not { Length: > 0 } vertices ||
                !Wrath335WmoBspRaycaster.TryIntersect(ray, vertices, bsp,
                    group.boundingBox, scratch.ViewerBsp, nearestDistance, RayLength,
                    out var hit))
                continue;

            nearestDistance = hit.WorldDistance;
            foundHit = true;
            scratch.ViewerHitDistances[index] = nearestDistance;
            scratch.PrimaryViewerHitDistance = nearestDistance;
            scratch.PrimaryViewerGroupIndex = (group.flags & Exterior) == 0 ? index : -1;
            scratch.SecondaryViewerGroupIndex = -1;
        }

        // Portal broad phases use the original segment and an independent 1.05
        // extent. Only the final caller compares against the current geometry cap.
        if (TryFindPortal(wmo, ray.LocalRay, fullLocalLength, enabledGroups,
                out var portalFraction, out var near, out var far) &&
            PortalCanOverride(portalFraction, nearestDistance / RayLength))
        {
            nearestDistance = portalFraction * RayLength;
            foundHit = true;
            scratch.PortalViewerOverride = true;
            scratch.PrimaryViewerHitDistance = nearestDistance;
            scratch.PrimaryViewerGroupIndex = (wmo.groupBatches[near].mogiFlags & Exterior) == 0
                ? near : -1;
            scratch.SecondaryViewerGroupIndex = scratch.PrimaryViewerGroupIndex >= 0 &&
                (wmo.groupBatches[far].mogiFlags & Exterior) == 0 ? far : -1;
        }
    }

    internal static bool PortalCanOverride(float portalFraction, float geometryFraction) =>
        (double)portalFraction - geometryFraction < (double)0.0001f;

    private static bool TryFindPortal(in WorldModel wmo, Ray ray, float fullLength,
        ReadOnlySpan<bool> enabledGroups, out float fraction, out int near, out int far)
    {
        fraction = 1.05f;
        near = far = -1;
        if (wmo.portals == null)
            return false;
        var extent = fullLength * fraction;
        for (var index = 0; index < wmo.groupBatches.Length; index++)
        {
            var group = wmo.groupBatches[index];
            if (!enabledGroups[index] ||
                !IntersectsSegment(ray, group.mogiBoundingBox, fullLength))
                continue;
            foreach (var link in group.portalLinks ?? [])
            {
                if (link.PortalIndex >= wmo.portals.Length ||
                    link.TargetGroupIndex >= wmo.groupBatches.Length)
                    continue;
                var portal = wmo.portals[link.PortalIndex];
                if (portal.Vertices is not { Length: >= 3 } vertices)
                    continue;
                var signedDistance = Wrath335PortalProjection.SignedDistance(portal, ray.Origin);
                var denominator = (double)portal.Normal.X * ray.Direction.X +
                    (double)portal.Normal.Y * ray.Direction.Y + (double)portal.Normal.Z * ray.Direction.Z;
                if (Math.Abs(denominator) < (double)0.0001f && Math.Abs(signedDistance) >= (double)0.1f)
                    continue;
                var distance = Math.Abs(signedDistance) < (double)0.1f ? 0f : (float)(-signedDistance / denominator);
                if (!float.IsFinite(distance) || distance < 0f || distance > extent ||
                    !PointInPortal(ray.GetPoint(distance), vertices, portal.Normal))
                    continue;

                extent = distance;
                var nearIsNeighbor = signedDistance >= 0f ? link.Side <= 0 : link.Side > 0;
                near = nearIsNeighbor ? link.TargetGroupIndex : index;
                far = nearIsNeighbor ? index : link.TargetGroupIndex;
            }
        }
        if (near < 0)
            return false;
        fraction = extent * (1f / fullLength);
        return true;
    }

    private static bool IntersectsSegment(Ray ray, in BoundingBox bounds, float length) =>
        IntersectionTests.RayIntersectsBox(ray, bounds, out var distance) && distance <= length;

    // NTempest::Intersect 0x9830D0 uses these axis pairs from 0xB2D6F4:
    // drop X -> (Y,Z); drop Y -> (Z,X); drop Z -> (X,Y).
    // Its <= tests include one polygon boundary and exclude the opposite one.
    internal static bool PointInPortal(Vector3 point, ReadOnlySpan<Vector3> vertices, Vector3 normal)
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

    internal static int MajorAxis(Vector3 normal)
    {
        var n = Vector3.Abs(normal);
        return n.X <= n.Y ? n.Y > n.Z ? 1 : 2 : n.X > n.Z ? 0 : 2;
    }

    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
