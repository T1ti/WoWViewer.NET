using System.Numerics;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Computes a conservative, placement-specific WMO group mask. Portal data is
/// immutable and shared by the WMO resource; camera and transforms are supplied
/// per placement. Invalid or pathological graphs return false so callers retain
/// the persistent all-enabled behavior.
/// </summary>
public static class WmoPortalVisibility
{
    private const uint ExteriorFlag = 0x8;
    private const uint InteriorFlag = 0x2000;
    private const uint AlwaysDrawFlag = 0x10000;
    private const float PlaneEpsilon = 0.001f;

    public static bool TryCompute(
        in WorldModel wmo,
        in Matrix4x4 modelMatrix,
        in Matrix4x4 viewProjection,
        Vector3 eyeWorld,
        ReadOnlySpan<bool> enabledGroups,
        Span<bool> visibleGroups,
        Span<bool> visibleDoodads,
        WmoPortalVisibilityScratch scratch,
        out int traversedPortalReferences)
    {
        traversedPortalReferences = 0;
        if (!wmo.portalGraphValid ||
            wmo.groupBatches == null ||
            wmo.portals == null ||
            enabledGroups.Length != wmo.groupBatches.Length ||
            visibleGroups.Length < wmo.groupBatches.Length ||
            !Matrix4x4.Invert(modelMatrix, out var inverseModel))
        {
            return false;
        }

        visibleGroups[..wmo.groupBatches.Length].Clear();
        var eyeLocal = Vector3.Transform(eyeWorld, inverseModel);
        scratch.Prepare(wmo.groupBatches.Length, modelMatrix * viewProjection);
        var hull = new PortalHull(scratch.Planes);
        var path = scratch.Path;
        var traversalBudget = Math.Max(128, wmo.portals.Length * 16 + wmo.groupBatches.Length * 4);
        var exhaustedBudget = false;

        var interiorSeedCount = 0;
        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            var group = wmo.groupBatches[groupIndex];
            if (!enabledGroups[groupIndex] ||
                (group.flags & InteriorFlag) == 0 ||
                !Contains(group.boundingBox, eyeLocal))
            {
                continue;
            }

            interiorSeedCount++;
            Traverse(
                groupIndex,
                wmo,
                eyeLocal,
                enabledGroups,
                visibleGroups,
                hull,
                path,
                traversalBudget,
                ref traversedPortalReferences,
                ref exhaustedBudget);
        }

        if (interiorSeedCount == 0)
        {
            var exteriorSeedCount = 0;
            for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
            {
                var group = wmo.groupBatches[groupIndex];
                if (!enabledGroups[groupIndex] ||
                    IsPortalCullableInterior(group.flags) ||
                    !hull.Intersects(group.boundingBox))
                {
                    continue;
                }

                exteriorSeedCount++;
                Traverse(
                    groupIndex,
                    wmo,
                    eyeLocal,
                    enabledGroups,
                    visibleGroups,
                    hull,
                    path,
                    traversalBudget,
                    ref traversedPortalReferences,
                    ref exhaustedBudget);
            }

            // A group without the interior bit is exterior even if the explicit
            // exterior bit is absent. If none of those groups intersect the view,
            // retain the old path rather than applying an empty portal mask.
            if (exteriorSeedCount == 0)
                return false;
        }

        if (exhaustedBudget)
            return false;

        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            if (!enabledGroups[groupIndex])
                continue;

            var flags = wmo.groupBatches[groupIndex].flags;
            if (!IsPortalCullableInterior(flags) ||
                (flags & AlwaysDrawFlag) != 0)
            {
                // Portals determine which interior groups can be seen through
                // openings. Only unambiguously interior groups may be removed.
                // Exterior, unclassified and conflicting classifications stay
                // on the ordinary WMO path with their MODR-owned doodads.
                visibleGroups[groupIndex] = true;
            }
        }

        BuildDoodadMask(wmo, visibleGroups, visibleDoodads);
        return true;
    }

    private static void Traverse(
        int groupIndex,
        in WorldModel wmo,
        Vector3 eyeLocal,
        ReadOnlySpan<bool> enabledGroups,
        Span<bool> visibleGroups,
        PortalHull hull,
        bool[] path,
        int traversalBudget,
        ref int traversedPortalReferences,
        ref bool exhaustedBudget)
    {
        if (exhaustedBudget ||
            (uint)groupIndex >= (uint)wmo.groupBatches.Length ||
            !enabledGroups[groupIndex] ||
            path[groupIndex])
        {
            return;
        }

        visibleGroups[groupIndex] = true;
        path[groupIndex] = true;
        var group = wmo.groupBatches[groupIndex];
        foreach (var link in group.portalLinks)
        {
            if (++traversedPortalReferences > traversalBudget)
            {
                exhaustedBudget = true;
                break;
            }

            if (link.PortalIndex >= wmo.portals.Length ||
                link.TargetGroupIndex >= wmo.groupBatches.Length ||
                !enabledGroups[link.TargetGroupIndex] ||
                path[link.TargetGroupIndex])
            {
                continue;
            }

            var portal = wmo.portals[link.PortalIndex];
            if (!FacesEye(portal, eyeLocal, link.Side) ||
                (!hull.Intersects(portal.Bounds) && !Contains(portal.Bounds, eyeLocal)))
            {
                continue;
            }

            var addedPlanes = hull.PushPortalClipPlanes(eyeLocal, portal.Vertices);
            Traverse(
                link.TargetGroupIndex,
                wmo,
                eyeLocal,
                enabledGroups,
                visibleGroups,
                hull,
                path,
                traversalBudget,
                ref traversedPortalReferences,
                ref exhaustedBudget);
            hull.PopPlanes(addedPlanes);
        }
        path[groupIndex] = false;
    }

    private static void BuildDoodadMask(
        in WorldModel wmo,
        ReadOnlySpan<bool> visibleGroups,
        Span<bool> visibleDoodads)
    {
        if (visibleDoodads.IsEmpty || wmo.doodads == null)
            return;

        var count = Math.Min(visibleDoodads.Length, wmo.doodads.Length);
        visibleDoodads[..count].Fill(true);
        if (wmo.doodadsReferencedByGroups == null ||
            wmo.doodadsReferencedByGroups.Length != wmo.doodads.Length)
        {
            return;
        }

        for (var doodadIndex = 0; doodadIndex < count; doodadIndex++)
            visibleDoodads[doodadIndex] = !wmo.doodadsReferencedByGroups[doodadIndex];

        for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
        {
            if (!visibleGroups[groupIndex])
                continue;
            foreach (var doodadIndex in wmo.groupBatches[groupIndex].doodadReferences)
            {
                if (doodadIndex < count)
                    visibleDoodads[doodadIndex] = true;
            }
        }
    }

    private static bool FacesEye(in WmoPortal portal, Vector3 eye, short side)
    {
        var distance = Vector3.Dot(portal.Normal, eye) + portal.Distance;
        return side switch
        {
            < 0 => distance <= PlaneEpsilon,
            > 0 => distance >= -PlaneEpsilon,
            _ => true
        };
    }

    private static bool Contains(in BoundingBox bounds, Vector3 point) =>
        point.X >= bounds.Min.X && point.X <= bounds.Max.X &&
        point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y &&
        point.Z >= bounds.Min.Z && point.Z <= bounds.Max.Z;

    private static bool IsPortalCullableInterior(uint flags) =>
        (flags & InteriorFlag) != 0 &&
        (flags & ExteriorFlag) == 0;

    private readonly struct PortalHull
    {
        private readonly List<Plane> _planes;

        public PortalHull(List<Plane> planes)
        {
            _planes = planes;
        }

        public bool Intersects(in BoundingBox bounds)
        {
            foreach (var plane in _planes)
            {
                var positiveVertex = new Vector3(
                    plane.Normal.X >= 0 ? bounds.Max.X : bounds.Min.X,
                    plane.Normal.Y >= 0 ? bounds.Max.Y : bounds.Min.Y,
                    plane.Normal.Z >= 0 ? bounds.Max.Z : bounds.Min.Z);
                if (Plane.DotCoordinate(plane, positiveVertex) < 0)
                    return false;
            }
            return true;
        }

        public int PushPortalClipPlanes(Vector3 eye, ReadOnlySpan<Vector3> vertices)
        {
            if (vertices.Length < 3)
                return 0;

            var added = 0;
            for (var index = 0; index < vertices.Length; index++)
            {
                var next = (index + 1) % vertices.Length;
                var previous = (index + vertices.Length - 1) % vertices.Length;
                var plane = Plane.CreateFromVertices(eye, vertices[index], vertices[next]);
                if (plane.Normal.LengthSquared() < 0.000001f)
                    continue;
                plane = Normalize(plane);
                if (Plane.DotCoordinate(plane, vertices[previous]) < 0)
                    plane = new Plane(-plane.Normal, -plane.D);
                _planes.Add(plane);
                added++;
            }
            return added;
        }

        public void PopPlanes(int count)
        {
            if (count > 0)
                _planes.RemoveRange(_planes.Count - count, count);
        }

        internal static Plane Normalize(Plane plane)
        {
            var length = plane.Normal.Length();
            return length > 0.000001f
                ? new Plane(plane.Normal / length, plane.D / length)
                : plane;
        }
    }
}

/// <summary>
/// Reusable placement-owned storage for portal traversal. Keeping this separate
/// from immutable WMO resources avoids sharing mutable camera state between
/// placements while eliminating per-frame traversal allocations.
/// </summary>
public sealed class WmoPortalVisibilityScratch
{
    internal bool[] Path { get; private set; } = [];
    internal List<Plane> Planes { get; } = new(24);

    internal void Prepare(int groupCount, in Matrix4x4 matrix)
    {
        if (Path.Length != groupCount)
            Path = new bool[groupCount];
        else
            Path.AsSpan().Clear();

        Planes.Clear();
        Planes.Add(PortalHullPlane(matrix.M14 + matrix.M11, matrix.M24 + matrix.M21,
            matrix.M34 + matrix.M31, matrix.M44 + matrix.M41));
        Planes.Add(PortalHullPlane(matrix.M14 - matrix.M11, matrix.M24 - matrix.M21,
            matrix.M34 - matrix.M31, matrix.M44 - matrix.M41));
        Planes.Add(PortalHullPlane(matrix.M14 - matrix.M12, matrix.M24 - matrix.M22,
            matrix.M34 - matrix.M32, matrix.M44 - matrix.M42));
        Planes.Add(PortalHullPlane(matrix.M14 + matrix.M12, matrix.M24 + matrix.M22,
            matrix.M34 + matrix.M32, matrix.M44 + matrix.M42));
        Planes.Add(PortalHullPlane(matrix.M14 + matrix.M13, matrix.M24 + matrix.M23,
            matrix.M34 + matrix.M33, matrix.M44 + matrix.M43));
        Planes.Add(PortalHullPlane(matrix.M14 - matrix.M13, matrix.M24 - matrix.M23,
            matrix.M34 - matrix.M33, matrix.M44 - matrix.M43));
    }

    private static Plane PortalHullPlane(float x, float y, float z, float d) =>
        Normalize(new Plane(x, y, z, d));

    private static Plane Normalize(Plane plane)
    {
        var length = plane.Normal.Length();
        return length > 0.000001f
            ? new Plane(plane.Normal / length, plane.D / length)
            : plane;
    }
}
