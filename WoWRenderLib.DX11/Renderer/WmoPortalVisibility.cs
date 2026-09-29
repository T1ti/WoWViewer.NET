using System.Numerics;
using GroupFlags = WoWLib.Formats.WMO.Group.Chunks.GroupFlags;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Computes placement-specific WMO group and render-batch visibility through
/// projected portals. Invalid graphs leave the caller's ordinary render path intact.
/// </summary>
public static class WmoPortalVisibility
{
    private const uint ExteriorFlag = (uint)GroupFlags.exterior;
    private const uint ExteriorLightingFlag = (uint)GroupFlags.exterior_lit;
    private const uint AlwaysDrawFlag = (uint)GroupFlags.always_draw;
    private const int ClientPortalMaxDepth = 10;
    private const int ClientPortalMaxVertices = 12;
    private const float ClientPortalPlaneProximity = 0.01f;
    private const float ClientProjectedWMinimum = 0.0001f;
    internal const float ClientRectDegeneracy = 0.001f;
    private const float BoundsTolerance = 0.0001f;
    internal const float ClientViewerRayLength = 1760f;
    private const float ClientViewerHitTie = 0.0001f;
    private const float ClientViewerFaceEdgeTolerance = 0.002f;
    private const uint ClientViewerExcludedFlags = 0x410080;

    /// <summary>Resolve the camera's WMO group without running portal culling.</summary>
    public static bool TryLocateViewerGroup(
        in WorldModel wmo,
        in Matrix4x4 modelMatrix,
        Vector3 eyeWorld,
        ReadOnlySpan<bool> enabledGroups,
        WmoPortalVisibilityScratch scratch,
        out int viewerGroupIndex,
        float maximumViewerDistance = ClientViewerRayLength)
    {
        viewerGroupIndex = -1;
        if (wmo.groupBatches == null ||
            enabledGroups.Length != wmo.groupBatches.Length)
            return false;
        scratch.PrepareViewer(wmo.groupBatches.Length);
        LocateViewerGroups(wmo, modelMatrix, eyeWorld,
            enabledGroups, scratch, maximumViewerDistance,
            allowBoundsFallback: false, out _, out _);
        viewerGroupIndex = scratch.PrimaryViewerGroupIndex;
        return viewerGroupIndex >= 0;
    }

    public static bool TryCompute(
        in WorldModel wmo,
        in Matrix4x4 modelMatrix,
        in Matrix4x4 viewProjection,
        Vector3 eyeWorld,
        ReadOnlySpan<bool> enabledGroups,
        Span<bool> visibleGroups,
        Span<bool> visibleDoodads,
        WmoPortalVisibilityScratch scratch,
        out int traversedPortalReferences,
        float maximumViewerDistance = ClientViewerRayLength) =>
        TryCompute(wmo, modelMatrix, viewProjection, eyeWorld, enabledGroups,
            visibleGroups, visibleDoodads, Span<bool>.Empty, scratch,
            out traversedPortalReferences, maximumViewerDistance);

    public static bool TryCompute(
        in WorldModel wmo,
        in Matrix4x4 modelMatrix,
        in Matrix4x4 viewProjection,
        Vector3 eyeWorld,
        ReadOnlySpan<bool> enabledGroups,
        Span<bool> visibleGroups,
        Span<bool> visibleDoodads,
        Span<bool> visibleBatches,
        WmoPortalVisibilityScratch scratch,
        out int traversedPortalReferences,
        float maximumViewerDistance = ClientViewerRayLength)
    {
        traversedPortalReferences = 0;
        if (!wmo.portalGraphValid ||
            wmo.groupBatches == null ||
            wmo.portals == null ||
            enabledGroups.Length != wmo.groupBatches.Length ||
            visibleGroups.Length < wmo.groupBatches.Length ||
            (!visibleBatches.IsEmpty &&
             (wmo.wmoRenderBatches == null || visibleBatches.Length != wmo.wmoRenderBatches.Length)) ||
            !Matrix4x4.Invert(modelMatrix, out var inverseModel))
        {
            return false;
        }

        visibleGroups[..wmo.groupBatches.Length].Clear();
        visibleBatches.Clear();
        var eyeLocal = Vector3.Transform(eyeWorld, inverseModel);
        scratch.Prepare(wmo.groupBatches.Length, wmo.portals.Length,
            modelMatrix * viewProjection, wmo.legacyLighting);
        var budget = Math.Max(128, wmo.portals.Length * 16 + wmo.groupBatches.Length * 4);
        var exhaustedBudget = false;
        var fullView = WmoPortalRect.Full;

        // The client limits the downward WMO query to the nearest terrain hit.
        // Legacy groups require a triangle hit; bounds alone never locate a
        // viewer in the 3.3.5 client. Keep the previous fallback for modern WMOs.
        var viewerInInterior = false;
        LocateViewerGroups(wmo, modelMatrix, eyeWorld,
            enabledGroups, scratch, maximumViewerDistance,
            allowBoundsFallback: !wmo.legacyLighting,
            out var nearestViewerHit,
            out var foundViewerHit);

        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            var distance = scratch.ViewerHitDistances[groupIndex];
            if ((!foundViewerHit && distance < 0f) ||
                (foundViewerHit && distance >= 0f &&
                 distance <= nearestViewerHit + ClientViewerHitTie))
            {
                var flags = wmo.groupBatches[groupIndex].flags;
                if ((flags & ExteriorFlag) != 0)
                    continue;
                if ((flags & (ExteriorFlag | ExteriorLightingFlag)) == 0)
                    viewerInInterior = true;
                Traverse(groupIndex, -1, 0, fullView, true, wmo, eyeLocal,
                    enabledGroups, visibleGroups, visibleBatches, scratch,
                    budget, ref traversedPortalReferences, ref exhaustedBudget);
            }
        }

        // From outdoors the client seeds only explicit exterior groups whose
        // bounds intersect the camera frustum. Unclassified groups are reachable
        // through portals but do not start a traversal.
        if (!viewerInInterior)
        {
            for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
            {
                var group = wmo.groupBatches[groupIndex];
                if (!enabledGroups[groupIndex] ||
                    (group.mogiFlags & ExteriorFlag) == 0 ||
                    (group.mogiFlags & AlwaysDrawFlag) != 0 ||
                    !IntersectsRect(group.mogiBoundingBox, scratch.LocalToClip, fullView))
                    continue;

                Traverse(groupIndex, -1, 0, fullView, false, wmo, eyeLocal, enabledGroups,
                    visibleGroups, visibleBatches, scratch, budget,
                    ref traversedPortalReferences, ref exhaustedBudget);
            }
        }

        // Always-draw groups bypass the portal walk in the client, but still
        // need the ordinary camera-frustum test.
        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            var group = wmo.groupBatches[groupIndex];
            if (!enabledGroups[groupIndex] ||
                (group.mogiFlags & AlwaysDrawFlag) == 0 ||
                !IntersectsRect(group.mogiBoundingBox, scratch.LocalToClip, fullView))
                continue;

            MarkGroup(groupIndex, fullView, wmo, visibleGroups, visibleBatches, scratch.LocalToClip);
        }

        if (exhaustedBudget)
            return false;

        BuildDoodadMask(wmo, visibleGroups, visibleDoodads);
        return true;
    }

    private static void LocateViewerGroups(in WorldModel wmo,
        in Matrix4x4 modelMatrix, Vector3 eyeWorld,
        ReadOnlySpan<bool> enabledGroups, WmoPortalVisibilityScratch scratch,
        float maximumViewerDistance, bool allowBoundsFallback,
        out float nearestViewerHit, out bool foundViewerHit)
    {
        nearestViewerHit = float.IsNaN(maximumViewerDistance)
            ? ClientViewerRayLength
            : Math.Clamp(maximumViewerDistance, 0f, ClientViewerRayLength);
        foundViewerHit = false;
        var canRaycast = TriangleMeshRaycaster.TryCreateContext(
            new Ray(eyeWorld, -Vector3.UnitZ), modelMatrix, out var viewerRay);
        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            var group = wmo.groupBatches[groupIndex];
            if (!enabledGroups[groupIndex] ||
                (group.flags & AlwaysDrawFlag) != 0 ||
                (wmo.legacyLighting && (group.flags & ClientViewerExcludedFlags) != 0) ||
                !canRaycast ||
                !IntersectionTests.RayIntersectsBox(viewerRay.LocalRay,
                    wmo.legacyLighting ? group.mogiBoundingBox : group.boundingBox,
                    out var boundDistance) ||
                Vector3.Distance(eyeWorld,
                    Vector3.Transform(viewerRay.LocalRay.GetPoint(boundDistance), modelMatrix)) >
                    nearestViewerHit + ClientViewerHitTie)
                continue;

            if (canRaycast && group.raycastVertices is { Length: > 0 } vertices &&
                group.raycastIndices is { Length: > 2 } indices)
            {
                if (TriangleMeshRaycaster.TryIntersectTriangles(viewerRay,
                    vertices, indices, nearestViewerHit + ClientViewerHitTie,
                    out var hitDistance,
                    wmo.legacyLighting ? ClientViewerFaceEdgeTolerance : 0f))
                {
                    scratch.ViewerHitDistances[groupIndex] = hitDistance;
                    nearestViewerHit = MathF.Min(nearestViewerHit, hitDistance);
                    foundViewerHit = true;
                }
            }
            else if (allowBoundsFallback)
            {
                scratch.ViewerHitDistances[groupIndex] = -1f;
            }
        }

        var primaryDistance = float.PositiveInfinity;
        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            var distance = scratch.ViewerHitDistances[groupIndex];
            var group = wmo.groupBatches[groupIndex];
            if ((group.flags & (ExteriorFlag | ExteriorLightingFlag)) != 0 ||
                !((!foundViewerHit && distance < 0f) ||
                  (foundViewerHit && distance >= 0f &&
                   distance <= nearestViewerHit + ClientViewerHitTie)))
                continue;
            if (distance >= 0f && distance < primaryDistance)
            {
                primaryDistance = distance;
                scratch.PrimaryViewerGroupIndex = groupIndex;
                scratch.PrimaryViewerHitDistance = distance;
            }
            else if (allowBoundsFallback && !foundViewerHit &&
                     scratch.PrimaryViewerGroupIndex < 0)
            {
                scratch.PrimaryViewerGroupIndex = groupIndex;
                scratch.PrimaryViewerHitDistance = ClientViewerRayLength;
            }
        }
    }

    private static void Traverse(
        int groupIndex,
        int previousGroupIndex,
        int depth,
        WmoPortalRect view,
        bool propagatedFromInterior,
        in WorldModel wmo,
        Vector3 eyeLocal,
        ReadOnlySpan<bool> enabledGroups,
        Span<bool> visibleGroups,
        Span<bool> visibleBatches,
        WmoPortalVisibilityScratch scratch,
        int budget,
        ref int traversedPortalReferences,
        ref bool exhaustedBudget)
    {
        if (exhaustedBudget ||
            (uint)groupIndex >= (uint)wmo.groupBatches.Length ||
            !enabledGroups[groupIndex] ||
            scratch.Path[groupIndex])
            return;

        // The client clears propagation when a portal route reaches an
        // exterior or exterior-lit MOGP group. The group callback ORs the
        // surviving bit across all visits before its render node is drawn.
        if ((wmo.groupBatches[groupIndex].flags &
             Wrath335PortalFogDistance.ExteriorGroupFlags) != 0)
            propagatedFromInterior = false;
        MarkGroup(groupIndex, view, wmo, visibleGroups, visibleBatches, scratch.LocalToClip);
        if (wmo.legacyLighting && propagatedFromInterior)
            scratch.PropagatedGroups[groupIndex] = true;
        if (depth >= ClientPortalMaxDepth ||
            (wmo.groupBatches[groupIndex].flags & AlwaysDrawFlag) != 0)
            return;

        scratch.Path[groupIndex] = true;
        foreach (var link in wmo.groupBatches[groupIndex].portalLinks)
        {
            if (++traversedPortalReferences > budget)
            {
                exhaustedBudget = true;
                break;
            }

            if (link.PortalIndex >= wmo.portals.Length ||
                link.TargetGroupIndex >= wmo.groupBatches.Length ||
                link.TargetGroupIndex == previousGroupIndex ||
                !enabledGroups[link.TargetGroupIndex] ||
                scratch.Path[link.TargetGroupIndex])
                continue;

            var portal = wmo.portals[link.PortalIndex];
            if (!FacesEye(portal, eyeLocal, link.Side))
                continue;

            var portalView = GetPortalRect(link.PortalIndex, portal, eyeLocal, scratch);
            if (!portalView.IsValid ||
                !view.TryIntersect(portalView, wmo.legacyLighting, out var clipped))
                continue;

            var targetGroup = wmo.groupBatches[link.TargetGroupIndex];
            if ((targetGroup.mogiFlags & AlwaysDrawFlag) != 0)
                continue;
            if ((targetGroup.mogiFlags & ExteriorFlag) != 0)
            {
                // Exterior scenery is reached through a portal view from
                // indoors, but does not recurse back through that group.
                // The exterior group still passes a view-frustum bounds test.
                if (IntersectsRect(targetGroup.mogiBoundingBox, scratch.LocalToClip, clipped))
                    MarkGroup(link.TargetGroupIndex, clipped, wmo, visibleGroups,
                        visibleBatches, scratch.LocalToClip);
                continue;
            }

            Traverse(link.TargetGroupIndex, groupIndex, depth + 1, clipped,
                propagatedFromInterior,
                wmo, eyeLocal, enabledGroups, visibleGroups, visibleBatches,
                scratch, budget, ref traversedPortalReferences, ref exhaustedBudget);
        }
        scratch.Path[groupIndex] = false;
    }

    private static void MarkGroup(
        int groupIndex,
        WmoPortalRect view,
        in WorldModel wmo,
        Span<bool> visibleGroups,
        Span<bool> visibleBatches,
        in Matrix4x4 localToClip)
    {
        visibleGroups[groupIndex] = true;
        if (visibleBatches.IsEmpty || wmo.wmoRenderBatches == null)
            return;

        if (wmo.firstRenderBatchByGroup == null ||
            wmo.renderBatchCountByGroup == null ||
            wmo.firstRenderBatchByGroup.Length != wmo.groupBatches.Length ||
            wmo.renderBatchCountByGroup.Length != wmo.groupBatches.Length)
        {
            // Synthetic or older resources may not have the range index.
            for (var index = 0; index < wmo.wmoRenderBatches.Length; index++)
                if (wmo.wmoRenderBatches[index].groupID == groupIndex)
                    MarkBatch(index, view, wmo.wmoRenderBatches[index], visibleBatches, localToClip);
            return;
        }

        var first = wmo.firstRenderBatchByGroup[groupIndex];
        var count = wmo.renderBatchCountByGroup[groupIndex];
        if (first < 0 || count < 0 || first > wmo.wmoRenderBatches.Length ||
            count > wmo.wmoRenderBatches.Length - first)
            return;
        for (var index = first; index < first + count; index++)
            MarkBatch(index, view, wmo.wmoRenderBatches[index], visibleBatches, localToClip);
    }

    private static void MarkBatch(int index, WmoPortalRect view,
        in WMORenderBatch batch, Span<bool> visibleBatches, in Matrix4x4 localToClip)
    {
        if (!visibleBatches[index] &&
            (!batch.hasBounds || IntersectsRect(batch.bounds, localToClip, view)))
            visibleBatches[index] = true;
    }

    private static WmoPortalRect GetPortalRect(int index, in WmoPortal portal,
        Vector3 eyeLocal, WmoPortalVisibilityScratch scratch)
    {
        if (scratch.ProjectedPortals[index])
            return scratch.PortalRects[index];

        scratch.ProjectedPortals[index] = true;
        var result = ProjectPortal(portal, eyeLocal, scratch);
        scratch.PortalRects[index] = result;
        return result;
    }

    private static WmoPortalRect ProjectPortal(in WmoPortal portal, Vector3 eyeLocal,
        WmoPortalVisibilityScratch scratch)
    {
        if (portal.Vertices == null || portal.Vertices.Length < 3)
            return default;

        var planeDistance = Vector3.Dot(portal.Normal, eyeLocal) + portal.Distance;
        if (MathF.Abs(planeDistance) < ClientPortalPlaneProximity &&
            PointInPortal(eyeLocal, portal.Vertices, portal.Normal))
            return WmoPortalRect.Full;

        var inputCount = scratch.LegacyClient
            ? Math.Min(portal.Vertices.Length, ClientPortalMaxVertices)
            : portal.Vertices.Length;
        scratch.EnsurePolygonCapacity(inputCount + 8);
        var source = scratch.PolygonA;
        var destination = scratch.PolygonB;
        var count = inputCount;
        for (var i = 0; i < count; i++)
            source[i] = Vector4.Transform(new Vector4(portal.Vertices[i], 1f), scratch.LocalToClip);

        // Wrath clips against four sides and near. The far plane is omitted
        // by CWorldScene__ProjectAndClipWorldPolygon; later batch culling
        // still applies the camera's far plane.
        var clipPlaneCount = scratch.LegacyClient ? 5 : 6;
        for (var plane = 0; plane < clipPlaneCount && count >= 3; plane++)
        {
            var nextCount = 0;
            var previous = source[count - 1];
            var previousDistance = ClipDistance(previous, plane);
            for (var i = 0; i < count; i++)
            {
                var current = source[i];
                var currentDistance = ClipDistance(current, plane);
                var wasInside = previousDistance >= 0;
                var isInside = currentDistance >= 0;
                if (wasInside != isInside)
                {
                    var fraction = previousDistance / (previousDistance - currentDistance);
                    destination[nextCount++] = Vector4.Lerp(previous, current, fraction);
                }
                if (isInside)
                    destination[nextCount++] = current;
                previous = current;
                previousDistance = currentDistance;
            }
            (source, destination) = (destination, source);
            count = nextCount;
        }

        if (count < 3)
            return default;
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        for (var i = 0; i < count; i++)
        {
            var clip = source[i];
            if (!float.IsFinite(clip.W) || (!scratch.LegacyClient && clip.W <= 0))
                return default;
            var divisor = scratch.LegacyClient
                ? MathF.Max(clip.W, ClientProjectedWMinimum)
                : clip.W;
            var x = clip.X / divisor;
            var y = clip.Y / divisor;
            if (!float.IsFinite(x) || !float.IsFinite(y))
                return default;
            minX = MathF.Min(minX, x);
            minY = MathF.Min(minY, y);
            maxX = MathF.Max(maxX, x);
            maxY = MathF.Max(maxY, y);
        }
        return new WmoPortalRect(minX, minY, maxX, maxY);
    }

    private static float ClipDistance(Vector4 vertex, int plane) => plane switch
    {
        0 => vertex.W + vertex.X,
        1 => vertex.W - vertex.X,
        2 => vertex.W + vertex.Y,
        3 => vertex.W - vertex.Y,
        4 => vertex.Z,
        _ => vertex.W - vertex.Z
    };

    private static bool PointInPortal(Vector3 point, ReadOnlySpan<Vector3> vertices,
        Vector3 normal)
    {
        var axis = Vector3.Abs(normal);
        var droppedAxis = axis.X >= axis.Y && axis.X >= axis.Z ? 0
            : axis.Y >= axis.Z ? 1 : 2;
        var projectedPoint = DropAxis(point, droppedAxis);
        var inside = false;
        for (var index = 0; index < vertices.Length; index++)
        {
            var a = DropAxis(vertices[index], droppedAxis);
            var b = DropAxis(vertices[(index + 1) % vertices.Length], droppedAxis);
            if ((a.Y > projectedPoint.Y) != (b.Y > projectedPoint.Y) &&
                projectedPoint.X < (b.X - a.X) * (projectedPoint.Y - a.Y) /
                (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static Vector2 DropAxis(Vector3 value, int axis) => axis switch
    {
        0 => new Vector2(value.Y, value.Z),
        1 => new Vector2(value.X, value.Z),
        _ => new Vector2(value.X, value.Y)
    };

    private static bool IntersectsRect(in BoundingBox bounds,
        in Matrix4x4 matrix, WmoPortalRect rect)
    {
        var x = new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
        var y = new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
        var z = new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        var w = new Vector4(matrix.M14, matrix.M24, matrix.M34, matrix.M44);
        return IntersectsPlane(bounds, x - rect.MinX * w) &&
               IntersectsPlane(bounds, rect.MaxX * w - x) &&
               IntersectsPlane(bounds, y - rect.MinY * w) &&
               IntersectsPlane(bounds, rect.MaxY * w - y) &&
               IntersectsPlane(bounds, z) &&
               IntersectsPlane(bounds, w - z);
    }

    private static bool IntersectsPlane(in BoundingBox bounds, Vector4 plane)
    {
        var x = plane.X >= 0 ? bounds.Max.X : bounds.Min.X;
        var y = plane.Y >= 0 ? bounds.Max.Y : bounds.Min.Y;
        var z = plane.Z >= 0 ? bounds.Max.Z : bounds.Min.Z;
        return plane.X * x + plane.Y * y + plane.Z * z + plane.W >= -BoundsTolerance;
    }

    private static void BuildDoodadMask(in WorldModel wmo,
        ReadOnlySpan<bool> visibleGroups, Span<bool> visibleDoodads)
    {
        if (visibleDoodads.IsEmpty || wmo.doodads == null)
            return;

        var count = Math.Min(visibleDoodads.Length, wmo.doodads.Length);
        visibleDoodads[..count].Fill(true);
        if (wmo.doodadsReferencedByGroups == null ||
            wmo.doodadsReferencedByGroups.Length != wmo.doodads.Length)
            return;

        for (var doodadIndex = 0; doodadIndex < count; doodadIndex++)
            visibleDoodads[doodadIndex] = !wmo.doodadsReferencedByGroups[doodadIndex];

        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            if (!visibleGroups[groupIndex])
                continue;
            foreach (var doodadIndex in wmo.groupBatches[groupIndex].doodadReferences)
                if (doodadIndex < count)
                    visibleDoodads[doodadIndex] = true;
        }
    }

    private static bool FacesEye(in WmoPortal portal, Vector3 eye, short side)
    {
        var distance = Vector3.Dot(portal.Normal, eye) + portal.Distance;
        return side < 0 ? distance <= 0f : distance >= 0f;
    }

}

internal readonly record struct WmoPortalRect(float MinX, float MinY, float MaxX, float MaxY)
{
    public static WmoPortalRect Full => new(-1f, -1f, 1f, 1f);
    public bool IsValid => MaxX - MinX > WmoPortalVisibility.ClientRectDegeneracy &&
                           MaxY - MinY > WmoPortalVisibility.ClientRectDegeneracy;

    public bool TryIntersect(WmoPortalRect child, bool legacyClient,
        out WmoPortalRect result)
    {
        if (child.MaxX < MinX || child.MinX > MaxX ||
            child.MaxY < MinY || child.MinY > MaxY)
        {
            result = default;
            return false;
        }
        result = new WmoPortalRect(
            MathF.Max(MinX, child.MinX),
            MathF.Max(MinY, child.MinY),
            MathF.Min(MaxX, child.MaxX),
            legacyClient ? child.MaxY : MathF.Min(MaxY, child.MaxY));
        return result.IsValid;
    }
}

/// <summary>Reusable camera and portal projection storage owned by one WMO placement.</summary>
public sealed class WmoPortalVisibilityScratch
{
    internal int PrimaryViewerGroupIndex { get; set; } = -1;
    internal float PrimaryViewerHitDistance { get; set; } = float.PositiveInfinity;
    internal bool[] Path { get; private set; } = [];
    internal bool[] PropagatedGroups { get; private set; } = [];
    internal bool[] ProjectedPortals { get; private set; } = [];
    internal WmoPortalRect[] PortalRects { get; private set; } = [];
    internal Vector4[] PolygonA { get; private set; } = [];
    internal Vector4[] PolygonB { get; private set; } = [];
    internal float[] ViewerHitDistances { get; private set; } = [];
    internal Matrix4x4 LocalToClip { get; private set; }
    internal bool LegacyClient { get; private set; }

    internal void Prepare(int groupCount, int portalCount,
        in Matrix4x4 localToClip, bool legacyClient)
    {
        PrepareViewer(groupCount);
        if (Path.Length != groupCount)
            Path = new bool[groupCount];
        else
            Path.AsSpan().Clear();
        if (PropagatedGroups.Length != groupCount)
            PropagatedGroups = new bool[groupCount];
        else
            PropagatedGroups.AsSpan().Clear();
        if (ProjectedPortals.Length != portalCount)
        {
            ProjectedPortals = new bool[portalCount];
            PortalRects = new WmoPortalRect[portalCount];
        }
        else
            ProjectedPortals.AsSpan().Clear();
        LocalToClip = localToClip;
        LegacyClient = legacyClient;
    }

    internal void PrepareViewer(int groupCount)
    {
        PrimaryViewerGroupIndex = -1;
        PrimaryViewerHitDistance = float.PositiveInfinity;
        if (ViewerHitDistances.Length != groupCount)
            ViewerHitDistances = new float[groupCount];
        ViewerHitDistances.AsSpan().Fill(float.PositiveInfinity);
    }

    internal void EnsurePolygonCapacity(int count)
    {
        if (PolygonA.Length >= count)
            return;
        PolygonA = new Vector4[count];
        PolygonB = new Vector4[count];
    }
}
