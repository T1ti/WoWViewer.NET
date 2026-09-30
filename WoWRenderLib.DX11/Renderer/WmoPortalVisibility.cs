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
    private const uint ExteriorFlag = (uint)GroupFlags.Exterior;
    private const uint ExteriorLightingFlag = (uint)GroupFlags.ExteriorLit;
    private const uint AlwaysDrawFlag = (uint)GroupFlags.AlwaysDraw;
    private const int ClientPortalMaxDepth = 10;
    private const int ClientPortalMaxVertices = 12;
    private const float ClientPortalPlaneProximity = 0.01f;
    private const float ClientProjectedWMinimum = 0.0001f;
    internal const float ClientRectDegeneracy = 0.001f;
    private const float BoundsTolerance = 0.0001f;
    internal const float ClientViewerRayLength = 1760f;
    private const float ClientViewerHitTie = 0.0001f;
    private const float ClientViewerFaceEdgeTolerance = 0.002f;
    private const float ClientViewerPortalParallelTolerance = 0.0001f;
    private const float ClientViewerPortalPlaneTolerance = 0.1f;
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
        => TryComputeCore(wmo, modelMatrix, viewProjection, eyeWorld, enabledGroups,
            visibleGroups, visibleDoodads, visibleBatches, scratch,
            out traversedPortalReferences, maximumViewerDistance, null);

    internal static bool TryCompute(
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
        float maximumViewerDistance,
        WmoViewerGroups? viewerGroups,
        Vector3 cameraForwardWorld = default,
        bool depthSortedExteriorSeeds = true)
        => TryComputeCore(wmo, modelMatrix, viewProjection, eyeWorld, enabledGroups,
            visibleGroups, visibleDoodads, visibleBatches, scratch,
            out traversedPortalReferences, maximumViewerDistance, viewerGroups,
            cameraForwardWorld: cameraForwardWorld, depthSortedExteriorSeeds: depthSortedExteriorSeeds);

    internal static bool TryComputeViewerScene(
        in WorldModel wmo, in Matrix4x4 modelMatrix, in Matrix4x4 viewProjection,
        Vector3 eyeWorld, Vector3 cameraForwardWorld, ReadOnlySpan<bool> enabledGroups,
        Span<bool> visibleGroups, Span<bool> visibleDoodads, Span<bool> visibleBatches,
        WmoPortalVisibilityScratch scratch, out int traversedPortalReferences,
        WmoViewerGroups viewerGroups, Wrath335PortalSceneViews sceneViews,
        bool depthSortedExteriorSeeds = true)
        => TryComputeCore(wmo, modelMatrix, viewProjection, eyeWorld, enabledGroups,
            visibleGroups, visibleDoodads, visibleBatches, scratch,
            out traversedPortalReferences, ClientViewerRayLength, viewerGroups,
            sceneViews, cameraForwardWorld, depthSortedExteriorSeeds);

    private static bool TryComputeCore(
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
        float maximumViewerDistance,
        WmoViewerGroups? viewerGroups,
        Wrath335PortalSceneViews? sceneViews = null,
        Vector3 cameraForwardWorld = default,
        bool depthSortedExteriorSeeds = true)
    {
        traversedPortalReferences = 0;
        if (!TryBegin(wmo, modelMatrix, viewProjection, eyeWorld, cameraForwardWorld,
            enabledGroups, visibleGroups, visibleBatches, scratch))
            return false;
        var eyeLocal = scratch.EyeLocal;
        var directionLocal = scratch.DirectionLocal;
        var budget = Math.Max(128, wmo.portals.Length * 16 + wmo.groupBatches.Length * 4);
        var exhaustedBudget = false;
        var fullView = WmoPortalRect.Full;

        // The client limits the downward WMO query to the nearest terrain hit.
        // Legacy groups require a triangle or portal hit; bounds alone never
        // locate a viewer in the 3.3.5 client. Keep the modern fallback.
        var viewerInInterior = false;
        float nearestViewerHit;
        bool foundViewerHit;
        var wrath335 = wmo.wrath335 && wmo.legacyLighting;
        if (wrath335 && viewerGroups is { } selected)
        {
            // 0x795D40 registers both group pairs; 0x7AD1F0 consumes that
            // registry. Other placements must not select their own floor again.
            scratch.PrimaryViewerGroupIndex = selected.PrimaryGroupIndex;
            scratch.SecondaryViewerGroupIndex = selected.SecondaryGroupIndex;
            nearestViewerHit = float.PositiveInfinity;
            foundViewerHit = false;
        }
        else
            LocateViewerGroups(wmo, modelMatrix, eyeWorld,
                enabledGroups, scratch, maximumViewerDistance,
                allowBoundsFallback: !wmo.legacyLighting,
                out nearestViewerHit, out foundViewerHit);

        if (scratch.PortalViewerOverride || wrath335)
        {
            for (var viewer = 0; viewer < 2; viewer++)
            {
                var groupIndex = viewer == 0
                    ? scratch.PrimaryViewerGroupIndex
                    : scratch.SecondaryViewerGroupIndex;
                if ((uint)groupIndex >= (uint)wmo.groupBatches.Length || !enabledGroups[groupIndex] ||
                    (wrath335 && viewer == 1 && groupIndex == scratch.PrimaryViewerGroupIndex))
                    continue;
                var flags = !wrath335 && scratch.PortalViewerOverride
                    ? wmo.groupBatches[groupIndex].mogiFlags
                    : wmo.groupBatches[groupIndex].flags;
                if (!wrath335 && (flags & ExteriorFlag) != 0)
                    continue;
                // 0x7AD27E tests loaded MOGP mask 0x48 for the strict-interior
                // marker but still traverses each registered group either way.
                if ((flags & (ExteriorFlag | ExteriorLightingFlag)) == 0)
                    viewerInInterior = true;
                Traverse(groupIndex, -1, 0, fullView, true, true, wmo, eyeLocal,
                    enabledGroups, visibleGroups, visibleBatches, scratch,
                    budget, ref traversedPortalReferences, ref exhaustedBudget,
                    sceneViews, directionLocal);
            }
        }
        else
        {
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
                    Traverse(groupIndex, -1, 0, fullView, true, true, wmo, eyeLocal,
                        enabledGroups, visibleGroups, visibleBatches, scratch,
                        budget, ref traversedPortalReferences, ref exhaustedBudget);
                }
            }
        }

        // From outdoors the client seeds only explicit exterior groups whose
        // bounds intersect the camera frustum. Unclassified groups are reachable
        // through portals but do not start a traversal.
        var exteriorView = sceneViews is { HasExteriorView: true }
            ? sceneViews.ExteriorRect : fullView;
        if (sceneViews != null ? sceneViews.HasExteriorView : !viewerInInterior)
        {
            if (wrath335)
                scratch.ExteriorGroupOrder.Build(wmo.groupBatches, enabledGroups,
                    modelMatrix, eyeWorld, cameraForwardWorld, depthSortedExteriorSeeds);
            var seedCount = wrath335 ? scratch.ExteriorGroupOrder.Seeds.Length : wmo.groupBatches.Length;
            for (var seedIndex = 0; seedIndex < seedCount; seedIndex++)
            {
                var groupIndex = wrath335 ? scratch.ExteriorGroupOrder.Seeds[seedIndex].GroupIndex : seedIndex;
                var group = wmo.groupBatches[groupIndex];
                if (!enabledGroups[groupIndex] ||
                    (group.mogiFlags & ExteriorFlag) == 0 ||
                    (group.mogiFlags & AlwaysDrawFlag) != 0 ||
                    !IntersectsRect(group.mogiBoundingBox, scratch.LocalToClip, exteriorView))
                    continue;

                Traverse(groupIndex, -1, 0, exteriorView, false, false, wmo, eyeLocal, enabledGroups,
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

            MarkGroup(groupIndex, fullView, wmo, visibleGroups, visibleBatches, scratch.LocalToClip, scratch);
        }

        if (exhaustedBudget)
            return false;

        if (wrath335 && sceneViews != null)
            sceneViews.RenderViews.Append(scratch.ExteriorPortalViews.Forwarded);
        BuildDoodadMask(wmo, visibleGroups, visibleDoodads);
        return true;
    }

    private static bool TryBegin(in WorldModel wmo, in Matrix4x4 modelMatrix,
        in Matrix4x4 viewProjection, Vector3 eyeWorld, Vector3 cameraForwardWorld,
        ReadOnlySpan<bool> enabledGroups, Span<bool> visibleGroups, Span<bool> visibleBatches,
        WmoPortalVisibilityScratch scratch)
    {
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
        scratch.Prepare(wmo.groupBatches.Length, wmo.portals.Length,
            modelMatrix * viewProjection, wmo.legacyLighting);
        scratch.EyeLocal = Vector3.Transform(eyeWorld, inverseModel);
        scratch.DirectionLocal = Vector3.TransformNormal(cameraForwardWorld, inverseModel);
        scratch.ModelToWorld = modelMatrix;
        scratch.SceneViewProjection = viewProjection;
        scratch.Wrath335 = wmo.wrath335 && wmo.legacyLighting;
        if (scratch.Wrath335)
            scratch.WrathProjection.Prepare(modelMatrix, viewProjection);
        return true;
    }

    internal static bool TryBeginWrath335Scene(in WorldModel wmo, in Matrix4x4 modelMatrix,
        in Matrix4x4 viewProjection, Vector3 eyeWorld, Vector3 forward,
        ReadOnlySpan<bool> enabled, Span<bool> groups, Span<bool> batches,
        WmoPortalVisibilityScratch scratch, List<BoundingBox> visibleBounds,
        Wrath335ClipVolumes? clipVolumes = null)
    {
        if (!wmo.wrath335 || !wmo.legacyLighting ||
            !TryBegin(wmo, modelMatrix, viewProjection, eyeWorld, forward, enabled, groups, batches, scratch))
            return false;
        scratch.SceneVisibleBounds = visibleBounds;
        scratch.WrathProjection.ClipVolumes = clipVolumes;
        return true;
    }

    internal static void VisitWrath335Interior(in WorldModel wmo, WmoViewerGroups viewer,
        ReadOnlySpan<bool> enabled, Span<bool> groups, Span<bool> batches,
        WmoPortalVisibilityScratch scratch, Wrath335PortalSceneViews? views, ref int references)
    {
        scratch.PrimaryViewerGroupIndex = viewer.PrimaryGroupIndex;
        scratch.SecondaryViewerGroupIndex = viewer.SecondaryGroupIndex;
        var exhausted = false;
        for (var index = 0; index < 2; index++)
        {
            var group = index == 0 ? viewer.PrimaryGroupIndex : viewer.SecondaryGroupIndex;
            if (index == 1 && group == viewer.PrimaryGroupIndex)
                continue;
            Traverse(group, -1, 0, WmoPortalRect.Full, true, true, wmo, scratch.EyeLocal,
                enabled, groups, batches, scratch, int.MaxValue, ref references, ref exhausted,
                views, scratch.DirectionLocal);
        }
        // 0x7AD2EE..0x7AD32F: selected placements also callback their always-draw groups.
        for (var index = 0; index < wmo.groupBatches.Length; index++)
        {
            if (!enabled[index] || (wmo.groupBatches[index].mogiFlags & AlwaysDrawFlag) == 0)
                continue;
            var bounds = Wrath335ExteriorGroupOrder.WorldBounds(wmo.groupBatches[index].mogiBoundingBox,
                scratch.ModelToWorld);
            if (IntersectsRect(bounds, scratch.SceneViewProjection, WmoPortalRect.Full))
                MarkGroup(index, WmoPortalRect.Full, wmo, groups, batches, scratch.LocalToClip, scratch);
        }
    }

    internal static void VisitWrath335Exterior(in WorldModel wmo, int groupIndex,
        in BoundingBox worldBounds, WmoPortalRect view, ReadOnlySpan<bool> enabled,
        Span<bool> groups, Span<bool> batches, WmoPortalVisibilityScratch scratch,
        Wrath335PortalPlacementCache cache, ref int references)
    {
        scratch.ExteriorPortalViews.Reset(); // Forward only this seed's newly emitted polygons.
        if (!enabled[groupIndex] || !IntersectsRect(worldBounds, scratch.SceneViewProjection, view))
            return;
        var flags = wmo.groupBatches[groupIndex].mogiFlags;
        if ((flags & AlwaysDrawFlag) != 0)
        {
            // 0x7B3A10 bypasses 0x7AD350 for always-draw: no placement stamp switch.
            MarkGroup(groupIndex, WmoPortalRect.Full, wmo, groups, batches, scratch.LocalToClip, scratch);
            return;
        }
        if ((flags & ExteriorFlag) == 0)
            return;
        cache.Enter(scratch);
        var exhausted = false;
        Traverse(groupIndex, -1, 0, view, false, false, wmo, scratch.EyeLocal,
            enabled, groups, batches, scratch, int.MaxValue, ref references, ref exhausted);
    }

    internal static void FinishWrath335Scene(in WorldModel wmo, ReadOnlySpan<bool> groups,
        Span<bool> doodads) => BuildDoodadMask(wmo, groups, doodads);

    private static void LocateViewerGroups(in WorldModel wmo,
        in Matrix4x4 modelMatrix, Vector3 eyeWorld,
        ReadOnlySpan<bool> enabledGroups, WmoPortalVisibilityScratch scratch,
        float maximumViewerDistance, bool allowBoundsFallback,
        out float nearestViewerHit, out bool foundViewerHit)
    {
        if (wmo.wrath335 && wmo.legacyLighting)
        {
            Wrath335WmoViewerQuery.Locate(wmo, modelMatrix, eyeWorld,
                enabledGroups, scratch, maximumViewerDistance,
                out nearestViewerHit, out foundViewerHit);
            return;
        }
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

            if (group.raycastVertices is { Length: > 0 } vertices &&
                (group.viewerBsp is not null || group.raycastIndices is { Length: > 2 }))
            {
                float hitDistance;
                bool foundHit;
                if (wmo.legacyLighting && group.viewerBsp is { } bsp)
                {
                    foundHit = Wrath335WmoBspRaycaster.TryIntersect(viewerRay,
                        vertices, bsp, group.boundingBox, scratch.ViewerBsp,
                        nearestViewerHit + ClientViewerHitTie, ClientViewerRayLength,
                        out var bspHit);
                    hitDistance = bspHit.WorldDistance;
                }
                else
                    foundHit = TriangleMeshRaycaster.TryIntersectTriangles(viewerRay,
                        vertices, group.raycastIndices ?? [],
                        nearestViewerHit + ClientViewerHitTie, out hitDistance,
                        wmo.legacyLighting ? ClientViewerFaceEdgeTolerance : 0f);
                if (foundHit)
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

        if (wmo.legacyLighting && canRaycast && wmo.portals is { Length: > 0 } &&
            TryFindViewerPortal(wmo, modelMatrix, viewerRay,
                enabledGroups, nearestViewerHit + ClientViewerHitTie,
                out var portalDistance, out var nearGroup, out var farGroup))
        {
            // LocateViewerMapObjs lets a portal at or before the best geometry
            // hit choose the near-side group, even with no group triangle hit.
            scratch.PortalViewerOverride = true;
            scratch.PrimaryViewerGroupIndex =
                (wmo.groupBatches[nearGroup].mogiFlags & ExteriorFlag) == 0
                    ? nearGroup : -1;
            scratch.SecondaryViewerGroupIndex =
                scratch.PrimaryViewerGroupIndex >= 0 &&
                (wmo.groupBatches[farGroup].mogiFlags & ExteriorFlag) == 0
                    ? farGroup : -1;
            scratch.PrimaryViewerHitDistance = portalDistance;
            nearestViewerHit = portalDistance;
            foundViewerHit = true;
        }
    }

    private static bool TryFindViewerPortal(in WorldModel wmo,
        in Matrix4x4 modelMatrix, in TriangleRaycastContext viewerRay,
        ReadOnlySpan<bool> enabledGroups, float maximumDistance,
        out float hitDistance, out int nearGroup, out int farGroup)
    {
        hitDistance = maximumDistance;
        nearGroup = -1;
        farGroup = -1;
        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            var group = wmo.groupBatches[groupIndex];
            if (!enabledGroups[groupIndex] ||
                !IntersectionTests.RayIntersectsBox(viewerRay.LocalRay,
                    group.mogiBoundingBox, out var boundsDistance) ||
                Vector3.Distance(viewerRay.WorldRay.Origin,
                    Vector3.Transform(viewerRay.LocalRay.GetPoint(boundsDistance),
                        modelMatrix)) > maximumDistance)
                continue;

            foreach (var link in group.portalLinks ?? [])
            {
                if (link.PortalIndex >= wmo.portals.Length ||
                    link.TargetGroupIndex >= wmo.groupBatches.Length ||
                    !enabledGroups[link.TargetGroupIndex])
                    continue;
                var portal = wmo.portals[link.PortalIndex];
                if (portal.Vertices is not { Length: >= 3 } vertices)
                    continue;
                var denominator = Vector3.Dot(portal.Normal,
                    viewerRay.LocalRay.Direction);
                var signedDistance = Vector3.Dot(portal.Normal,
                    viewerRay.LocalRay.Origin) + portal.Distance;
                if (MathF.Abs(denominator) < ClientViewerPortalParallelTolerance &&
                    MathF.Abs(signedDistance) >= ClientViewerPortalPlaneTolerance)
                    continue;
                var alongRay = MathF.Abs(signedDistance) < ClientViewerPortalPlaneTolerance
                    ? 0f : -signedDistance / denominator;
                if (alongRay < 0f)
                    continue;
                var point = viewerRay.LocalRay.GetPoint(alongRay);
                if (!PointInPortal(point, vertices, portal.Normal))
                    continue;
                var distance = Vector3.Distance(viewerRay.WorldRay.Origin,
                    Vector3.Transform(point, modelMatrix));
                if (distance > hitDistance || distance > maximumDistance)
                    continue;

                var nearIsNeighbor = signedDistance >= 0f
                    ? link.Side <= 0 : link.Side > 0;
                nearGroup = nearIsNeighbor
                    ? link.TargetGroupIndex : groupIndex;
                farGroup = nearIsNeighbor
                    ? groupIndex : link.TargetGroupIndex;
                hitDistance = distance;
            }
        }

        return nearGroup >= 0;
    }

    private static void Traverse(
        int groupIndex,
        int previousGroupIndex,
        int depth,
        WmoPortalRect view,
        bool propagatedFromInterior,
        bool interiorPass,
        in WorldModel wmo,
        Vector3 eyeLocal,
        ReadOnlySpan<bool> enabledGroups,
        Span<bool> visibleGroups,
        Span<bool> visibleBatches,
        WmoPortalVisibilityScratch scratch,
        int budget,
        ref int traversedPortalReferences,
        ref bool exhaustedBudget,
        Wrath335PortalSceneViews? sceneViews = null,
        Vector3 directionLocal = default)
    {
        if (exhaustedBudget || (scratch.Wrath335 && depth > ClientPortalMaxDepth) ||
            (uint)groupIndex >= (uint)wmo.groupBatches.Length ||
            !enabledGroups[groupIndex] ||
            (!scratch.Wrath335 && scratch.Path[groupIndex]) ||
            (scratch.Wrath335 && (wmo.groupBatches[groupIndex].flags & AlwaysDrawFlag) != 0))
            return;

        // The client clears propagation when a portal route reaches an
        // exterior or exterior-lit MOGP group. The group callback ORs the
        // surviving bit across all visits before its render node is drawn.
        if ((wmo.groupBatches[groupIndex].flags &
             Wrath335PortalFogDistance.ExteriorGroupFlags) != 0)
            propagatedFromInterior = false;
        MarkGroup(groupIndex, view, wmo, visibleGroups, visibleBatches, scratch.LocalToClip, scratch);
        if (wmo.legacyLighting && propagatedFromInterior)
            scratch.PropagatedGroups[groupIndex] = true;
        if ((!scratch.Wrath335 && depth >= ClientPortalMaxDepth) ||
            (wmo.groupBatches[groupIndex].flags & AlwaysDrawFlag) != 0)
            return;

        var exteriorSeed = scratch.Wrath335 && !interiorPass && depth == 0;
        if (exteriorSeed)
            scratch.ExteriorPortalViews.BeginSeed();
        scratch.Path[groupIndex] = true;
        foreach (var link in wmo.groupBatches[groupIndex].portalLinks)
        {
            if (++traversedPortalReferences > budget && !scratch.Wrath335)
            {
                exhaustedBudget = true;
                break;
            }

            if (link.PortalIndex >= wmo.portals.Length ||
                link.TargetGroupIndex >= wmo.groupBatches.Length ||
                link.TargetGroupIndex == previousGroupIndex ||
                (!scratch.Wrath335 && !enabledGroups[link.TargetGroupIndex]) ||
                (!scratch.Wrath335 && scratch.Path[link.TargetGroupIndex]))
                continue;

            var portal = wmo.portals[link.PortalIndex];
            // The native cache is initialized before the side test, including
            // back-facing depth-zero links whose projection can be empty.
            var portalView = scratch.Wrath335
                ? GetPortalRect(link.PortalIndex, portal, eyeLocal, scratch,
                    (wmo.groupBatches[link.TargetGroupIndex].mogiFlags & ExteriorFlag) == 0 &&
                    (wmo.groupBatches[groupIndex].flags & ExteriorFlag) == 0) : default;
            if (!FacesEye(portal, eyeLocal, link.Side, scratch.Wrath335))
            {
                if (exteriorSeed)
                    scratch.ExteriorPortalViews.AddBlocker();
                continue;
            }

            if (!scratch.Wrath335)
                portalView = GetPortalRect(link.PortalIndex, portal, eyeLocal, scratch);
            WmoPortalRect clipped;
            if (scratch.Wrath335
                ? !view.TryIntersectWrath335(portalView, out clipped)
                : !portalView.IsValid || !view.TryIntersect(portalView, wmo.legacyLighting, out clipped))
                continue;

            var targetGroup = wmo.groupBatches[link.TargetGroupIndex];
            if (scratch.Wrath335)
            {
                if (interiorPass)
                {
                    if ((targetGroup.mogiFlags & Wrath335PortalSceneViews.PortalViewFlags) != 0)
                    {
                        EmitInteriorPortal(link, portal, eyeLocal, directionLocal,
                            targetGroup.mogiFlags, scratch, sceneViews);
                        if ((targetGroup.mogiFlags & Wrath335PortalSceneViews.ExteriorViewFlags) != 0)
                            continue;
                    }
                }
                else
                {
                    if ((targetGroup.mogiFlags & Wrath335PortalSceneViews.ExteriorViewFlags) != 0)
                        continue;
                    // 0x7A70B3 unconditionally enables the top-level emission
                    // gate. 0x7AC409 excludes only root mask 0x140 here.
                    if (exteriorSeed && (targetGroup.mogiFlags & 0x140) == 0)
                        EmitExteriorPortal(link, portal, eyeLocal, scratch);
                }
            }
            if (!scratch.Wrath335 && (targetGroup.mogiFlags & AlwaysDrawFlag) != 0)
                continue;
            if ((targetGroup.mogiFlags & ExteriorFlag) != 0)
            {
                if (scratch.Wrath335)
                    continue; // Exterior callbacks come from the exterior seed pass.
                // Exterior scenery is reached through a portal view from
                // indoors, but does not recurse back through that group.
                // The exterior group still passes a view-frustum bounds test.
                if (IntersectsRect(targetGroup.mogiBoundingBox, scratch.LocalToClip, clipped))
                    MarkGroup(link.TargetGroupIndex, clipped, wmo, visibleGroups,
                        visibleBatches, scratch.LocalToClip);
                continue;
            }

            Traverse(link.TargetGroupIndex, groupIndex, depth + 1, clipped,
                propagatedFromInterior, interiorPass,
                wmo, eyeLocal, enabledGroups, visibleGroups, visibleBatches,
                scratch, budget, ref traversedPortalReferences, ref exhaustedBudget,
                sceneViews, directionLocal);
        }
        scratch.Path[groupIndex] = false;
        if (exteriorSeed)
            scratch.ExteriorPortalViews.EndSeed();
    }

    private static void EmitInteriorPortal(in WmoPortalLink link, in WmoPortal portal,
        Vector3 eyeLocal, Vector3 directionLocal, uint destinationFlags,
        WmoPortalVisibilityScratch scratch, Wrath335PortalSceneViews? sceneViews)
    {
        ref var flags = ref scratch.EmittedPortalViews[link.PortalIndex];
        if ((flags & 4) != 0)
            return;
        // 0x7A8F20 sets bit 4 even when the offset polygon clips to nothing.
        flags |= 4;
        Span<Vector3> polygon = stackalloc Vector3[32];
        var offset = portal.Normal * (link.Side > 0 ? -0.01f : 0.01f);
        var count = scratch.WrathProjection.ProjectPolygon(portal, eyeLocal,
            polygon, out var rect, offset, (flags & 0x10) != 0);
        if (count < 3)
        {
            flags |= 1;
            return;
        }
        sceneViews?.AddProjectedPortal(rect,
            Wrath335PortalSceneViews.MaximumDistance(portal.Vertices, eyeLocal, directionLocal),
            destinationFlags);
    }

    private static void EmitExteriorPortal(in WmoPortalLink link, in WmoPortal portal,
        Vector3 eyeLocal, WmoPortalVisibilityScratch scratch)
    {
        ref var flags = ref scratch.EmittedPortalViews[link.PortalIndex];
        if ((flags & 12) != 0)
            return; // 0x7A920D: either emission bit suppresses exterior emission.
        Span<Vector3> polygon = stackalloc Vector3[32];
        var offset = portal.Normal * (link.Side > 0 ? -0.01f : 0.01f);
        var count = scratch.WrathProjection.ProjectPolygon(portal, eyeLocal,
            polygon, out var rect, offset, (flags & 0x10) != 0);
        if (count < 3)
            flags |= 1;
        else
            scratch.ExteriorPortalViews.AddCandidate(rect, polygon[..count]);
        flags |= 8; // 0x7A936A also stamps failed projections and blocked candidates.
    }

    private static void MarkGroup(
        int groupIndex,
        WmoPortalRect view,
        in WorldModel wmo,
        Span<bool> visibleGroups,
        Span<bool> visibleBatches,
        in Matrix4x4 localToClip,
        WmoPortalVisibilityScratch? scratch = null)
    {
        if (!visibleGroups[groupIndex] && scratch?.SceneVisibleBounds is { } bounds)
            bounds.Add(Wrath335ExteriorGroupOrder.WorldBounds(wmo.groupBatches[groupIndex].mogiBoundingBox,
                scratch.ModelToWorld));
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
        Vector3 eyeLocal, WmoPortalVisibilityScratch scratch, bool bypassClipVolumes = false)
    {
        if (scratch.ProjectedPortals[index])
            return scratch.Wrath335 && (scratch.EmittedPortalViews[index] & 3) == 1
                ? default : scratch.PortalRects[index];

        scratch.ProjectedPortals[index] = true;
        if (scratch.Wrath335 && bypassClipVolumes)
            scratch.EmittedPortalViews[index] |= 0x10; // 0x7AC210..0x7AC21A, first cache visit only.
        var result = ProjectPortal(portal, eyeLocal, scratch, bypassClipVolumes: bypassClipVolumes);
        if (scratch.Wrath335 && Wrath335PortalProjection.ContainsEye(portal, eyeLocal))
            scratch.EmittedPortalViews[index] |= 2;
        scratch.PortalRects[index] = result;
        return result;
    }

    private static WmoPortalRect ProjectPortal(in WmoPortal portal, Vector3 eyeLocal,
        WmoPortalVisibilityScratch scratch, Vector3 offset = default,
        bool allowEyeContainment = true, bool bypassClipVolumes = false)
    {
        if (portal.Vertices == null || portal.Vertices.Length < 3)
            return default;

        if (scratch.Wrath335)
            return scratch.WrathProjection.Project(portal, eyeLocal, offset, allowEyeContainment, bypassClipVolumes);

        var planeDistance = Vector3.Dot(portal.Normal, eyeLocal) + portal.Distance;
        if (allowEyeContainment && MathF.Abs(planeDistance) < ClientPortalPlaneProximity &&
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
            source[i] = Vector4.Transform(new Vector4(portal.Vertices[i] + offset, 1f), scratch.LocalToClip);

        // Preserve the existing approximation for other legacy profiles.
        // Exact 12340 world-space clipping is handled above.
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

    internal static bool IntersectsRect(in BoundingBox bounds,
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

    private static bool FacesEye(in WmoPortal portal, Vector3 eye, short side, bool wrath335)
    {
        var distance = wrath335 ? Wrath335PortalProjection.SignedDistance(portal, eye)
            : Vector3.Dot(portal.Normal, eye) + portal.Distance;
        return side < 0 ? distance <= 0f : distance >= 0f;
    }

}

internal readonly record struct WmoPortalRect(float MinX, float MinY, float MaxX, float MaxY)
{
    public static WmoPortalRect Full => new(-1f, -1f, 1f, 1f);
    public bool IsValid => MaxX - MinX > WmoPortalVisibility.ClientRectDegeneracy &&
                           MaxY - MinY > WmoPortalVisibility.ClientRectDegeneracy;

    public bool TryIntersectWrath335(WmoPortalRect child, out WmoPortalRect result)
    {
        if (child.MaxX < MinX || child.MinX > MaxX || child.MaxY < MinY || child.MinY > MaxY)
        {
            result = default;
            return false;
        }
        // 0x7A6B90 proves CRect is YX/YX. 0x7AC354 clamps max-X
        // a second time; max-Y is deliberately not clamped to the parent.
        result = new(Math.Max(MinX, child.MinX), Math.Max(MinY, child.MinY),
            Math.Min(MaxX, child.MaxX), child.MaxY);
        // 0x482870 rejects only abs(a-b) < epsilon, including equality.
        return float.IsFinite(result.MinX) && float.IsFinite(result.MinY) &&
            float.IsFinite(result.MaxX) && float.IsFinite(result.MaxY) &&
            (double)result.MaxX - result.MinX >= (double)WmoPortalVisibility.ClientRectDegeneracy &&
            (double)result.MaxY - result.MinY >= (double)WmoPortalVisibility.ClientRectDegeneracy;
    }

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
    internal Wrath335PortalProjection WrathProjection { get; } = new();
    internal Wrath335ExteriorPortalViews ExteriorPortalViews { get; } = new();
    internal Wrath335ExteriorGroupOrder ExteriorGroupOrder { get; } = new();
    internal bool Wrath335 { get; set; }
    internal WmoBspRayScratch ViewerBsp { get; } = new();
    internal int PrimaryViewerGroupIndex { get; set; } = -1;
    internal int SecondaryViewerGroupIndex { get; set; } = -1;
    internal float PrimaryViewerHitDistance { get; set; } = float.PositiveInfinity;
    internal bool PortalViewerOverride { get; set; }
    internal bool[] Path { get; private set; } = [];
    internal bool[] PropagatedGroups { get; private set; } = [];
    internal bool[] ProjectedPortals { get; private set; } = [];
    internal byte[] EmittedPortalViews { get; private set; } = [];
    internal WmoPortalRect[] PortalRects { get; private set; } = [];
    internal Vector4[] PolygonA { get; private set; } = [];
    internal Vector4[] PolygonB { get; private set; } = [];
    internal float[] ViewerHitDistances { get; private set; } = [];
    internal Matrix4x4 LocalToClip { get; private set; }
    internal Matrix4x4 ModelToWorld { get; set; }
    internal Matrix4x4 SceneViewProjection { get; set; }
    internal Vector3 EyeLocal { get; set; }
    internal Vector3 DirectionLocal { get; set; }
    internal List<BoundingBox>? SceneVisibleBounds { get; set; }
    internal bool LegacyClient { get; private set; }

    internal void Prepare(int groupCount, int portalCount,
        in Matrix4x4 localToClip, bool legacyClient)
    {
        SceneVisibleBounds = null;
        ExteriorPortalViews.Reset();
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
            EmittedPortalViews = new byte[portalCount];
            PortalRects = new WmoPortalRect[portalCount];
        }
        else
        {
            ProjectedPortals.AsSpan().Clear();
            EmittedPortalViews.AsSpan().Clear();
        }
        LocalToClip = localToClip;
        LegacyClient = legacyClient;
    }

    internal void PrepareViewer(int groupCount)
    {
        PrimaryViewerGroupIndex = -1;
        SecondaryViewerGroupIndex = -1;
        PrimaryViewerHitDistance = float.PositiveInfinity;
        PortalViewerOverride = false;
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
