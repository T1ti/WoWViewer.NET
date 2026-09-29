using System.Numerics;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Raycasting;

internal readonly record struct WmoBspRayHit(
    float WorldDistance, int FaceIndex, int TestedFaces, bool FaceLimitReached);

internal readonly record struct WmoBspRayWork(
    ushort NodeIndex, Vector3 Start, Vector3 End, BoundingBox ClipBounds);

/// <summary>Placement-owned scratch reused across all its camera queries.</summary>
internal sealed class WmoBspRayScratch
{
    internal uint[] FaceStamps = [];
    internal WmoBspRayWork[] Work = [];
    internal uint Epoch;

    internal void Prepare(int faceCount, int nodeCount)
    {
        if (FaceStamps.Length < faceCount)
            Array.Resize(ref FaceStamps, faceCount);
        if (Work.Length < nodeCount + 1)
            Array.Resize(ref Work, nodeCount + 1);
        if (++Epoch == 0)
        {
            Array.Clear(FaceStamps);
            Epoch = 1;
        }
    }
}

/// <summary>
/// CPU equivalent of the 12340 BspWalkRay/EmitLeafFaces camera-query path.
/// Consumes decoded data only; no mesh/resource creation occurs during a query.
/// </summary>
internal static class Wrath335WmoBspRaycaster
{
    internal const float PlaneTolerance = 0.01f;
    internal const float FaceEdgeTolerance = 0.002f;
    internal const int FaceLimit = 8192;

    public static bool TryIntersect(
        in TriangleRaycastContext context,
        ReadOnlySpan<Vector3> vertices,
        WmoBspTree tree,
        BoundingBox rootBounds,
        WmoBspRayScratch scratch,
        float maximumWorldDistance,
        float fullWorldRayLength,
        out WmoBspRayHit hit,
        bool useDigestCache = true)
    {
        hit = new(maximumWorldDistance, -1, 0, false);
        if (tree.Nodes.Length == 0 || tree.Indices.Length < 3 ||
            !float.IsFinite(maximumWorldDistance) || maximumWorldDistance < 0f ||
            !float.IsFinite(fullWorldRayLength) || fullWorldRayLength <= 0f)
            return false;
        var worldUnitsPerLocalUnit = Vector3.TransformNormal(
            context.LocalRay.Direction, context.ModelMatrix).Length();
        if (!float.IsFinite(worldUnitsPerLocalUnit) || worldUnitsPerLocalUnit <= 0f)
            return false;
        var start = context.LocalRay.Origin;
        var end = context.LocalRay.GetPoint(fullWorldRayLength / worldUnitsPerLocalUnit);
        var rayBounds = new BoundingBox(
            Vector3.Min(start, end) - new Vector3(PlaneTolerance),
            Vector3.Max(start, end) + new Vector3(PlaneTolerance));
        var bestLocalDistance = maximumWorldDistance / worldUnitsPerLocalUnit;
        var bestFace = -1;
        var testedFaces = 0;
        var limitReached = false;
        scratch.Prepare(tree.Indices.Length / 3, tree.Nodes.Length);
        var count = 1;
        scratch.Work[0] = new(0, start, end, rootBounds);
        // Bounded traversal also protects synthetic/invalid trees from cycles.
        var workBudget = (long)tree.Nodes.Length * 4 + 1;
        while (count > 0 && --workBudget >= 0)
        {
            var work = scratch.Work[--count];
            if (work.NodeIndex == ushort.MaxValue || work.NodeIndex >= tree.Nodes.Length)
                continue;
            var node = tree.Nodes[work.NodeIndex];
            if ((node.Flags & 4) != 0)
            {
                if (node.FaceStart > tree.FaceReferences.Length ||
                    node.FaceCount > tree.FaceReferences.Length - node.FaceStart)
                    continue;
                for (var index = 0; index < node.FaceCount; index++)
                {
                    var face = tree.FaceReferences[(int)node.FaceStart + index];
                    if (face >= scratch.FaceStamps.Length ||
                        face >= tree.Indices.Length / 3 ||
                        face >= tree.FaceFlags.Length ||
                        (tree.FaceFlags[face] & 0x80) != 0 ||
                        scratch.FaceStamps[face] == scratch.Epoch)
                        continue;
                    if (testedFaces == FaceLimit)
                    {
                        limitReached = true;
                        count = 0;
                        break;
                    }
                    scratch.FaceStamps[face] = scratch.Epoch;
                    testedFaces++;
                    var first = face * 3;
                    var i0 = tree.Indices[first];
                    var i1 = tree.Indices[first + 1];
                    var i2 = tree.Indices[first + 2];
                    if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length ||
                        (useDigestCache && tree.DigestEligibleLeaves[work.NodeIndex] &&
                         (Outcode(vertices[i0], rayBounds) & Outcode(vertices[i1], rayBounds) &
                          Outcode(vertices[i2], rayBounds)) != 0) ||
                        !IntersectFace(context.LocalRay, vertices[i0], vertices[i1], vertices[i2],
                            out var distance) || distance > bestLocalDistance)
                        continue;
                    // TestRayFace_FlagGated replaces the previous hit on t <= best.
                    bestLocalDistance = distance;
                    bestFace = face;
                }
                continue;
            }

            var axis = node.Flags & 3;
            if (axis > 2 || !float.IsFinite(node.PlaneDistance))
                continue;
            var startAxis = Component(work.Start, axis);
            var endAxis = Component(work.End, axis);
            var lower = Component(work.ClipBounds.Min, axis);
            var upper = Component(work.ClipBounds.Max, axis);
            if ((startAxis - lower < -PlaneTolerance && endAxis - lower < -PlaneTolerance) ||
                (upper - startAxis < PlaneTolerance && upper - endAxis < PlaneTolerance))
                continue;
            var positiveBounds = new BoundingBox(
                WithComponent(work.ClipBounds.Min, axis, node.PlaneDistance),
                work.ClipBounds.Max);
            var negativeBounds = new BoundingBox(work.ClipBounds.Min,
                WithComponent(work.ClipBounds.Max, axis, node.PlaneDistance));
            var startSide = startAxis - node.PlaneDistance;
            var endSide = endAxis - node.PlaneDistance;
            if (MathF.Abs(startSide) <= PlaneTolerance || MathF.Abs(endSide) <= PlaneTolerance)
            {
                // On-plane endpoint: client visits positive before negative,
                // independent of which side contains the other endpoint.
                Push(node.NegativeChild, work.Start, work.End, negativeBounds);
                Push(node.PositiveChild, work.Start, work.End, positiveBounds);
            }
            else if (startSide > PlaneTolerance && endSide > PlaneTolerance)
                Push(node.PositiveChild, work.Start, work.End, positiveBounds);
            else if (startSide < -PlaneTolerance && endSide < -PlaneTolerance)
                Push(node.NegativeChild, work.Start, work.End, negativeBounds);
            else
            {
                var crossing = work.Start + (work.End - work.Start) *
                    (startSide / (startSide - endSide));
                if (startSide > 0f)
                {
                    Push(node.NegativeChild, crossing, work.End, negativeBounds);
                    Push(node.PositiveChild, work.Start, crossing, positiveBounds);
                }
                else
                {
                    Push(node.PositiveChild, crossing, work.End, positiveBounds);
                    Push(node.NegativeChild, work.Start, crossing, negativeBounds);
                }
            }
        }

        hit = new(bestLocalDistance * worldUnitsPerLocalUnit, bestFace,
            testedFaces, limitReached);
        // Exhausted traversal cannot establish a complete query on a malformed tree.
        return workBudget >= 0 && bestFace >= 0;

        void Push(ushort child, Vector3 segmentStart, Vector3 segmentEnd, BoundingBox bounds)
        {
            if (child == ushort.MaxValue || child >= tree.Nodes.Length)
                return;
            if (count == scratch.Work.Length)
            {
                workBudget = -1;
                return;
            }
            scratch.Work[count++] = new(child, segmentStart, segmentEnd, bounds);
        }
    }

    private static float Component(Vector3 value, int axis) =>
        axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

    private static byte Outcode(Vector3 point, BoundingBox bounds) => (byte)(
        (point.X < bounds.Min.X ? 32 : 0) | (point.X > bounds.Max.X ? 16 : 0) |
        (point.Y < bounds.Min.Y ? 8 : 0) | (point.Y > bounds.Max.Y ? 4 : 0) |
        (point.Z < bounds.Min.Z ? 2 : 0) | (point.Z > bounds.Max.Z ? 1 : 0));

    private static Vector3 WithComponent(Vector3 value, int axis, float component) =>
        axis == 0 ? new(component, value.Y, value.Z) :
        axis == 1 ? new(value.X, component, value.Z) : new(value.X, value.Y, component);

    private static bool IntersectFace(
        Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
    {
        distance = 0f;
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var perpendicular = Vector3.Cross(ray.Direction, edge2);
        var determinant = Vector3.Dot(edge1, perpendicular);
        // NTempest::Intersect uses strict comparisons at the two endpoints.
        if (determinant > -0.000001f && determinant < 0.000001f)
            return false;
        var inverse = 1f / determinant;
        var offset = ray.Origin - v0;
        var u = Vector3.Dot(offset, perpendicular) * inverse;
        if (u < -FaceEdgeTolerance || u > 1f + FaceEdgeTolerance)
            return false;
        var crossed = Vector3.Cross(offset, edge1);
        var v = Vector3.Dot(ray.Direction, crossed) * inverse;
        if (v < -FaceEdgeTolerance || u + v > 1f + FaceEdgeTolerance)
            return false;
        distance = Vector3.Dot(edge2, crossed) * inverse;
        return float.IsFinite(distance) && distance >= 0f;
    }
}
