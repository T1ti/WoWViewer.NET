using System.Numerics;
using WoWRenderLib.Raycasting;

namespace WoWRenderLib.DX11.Raycasting;

internal readonly record struct TriangleRaycastContext(
    Ray WorldRay,
    Ray LocalRay,
    Matrix4x4 ModelMatrix);

/// <summary>
/// Performs allocation-free ray tests against local-space indexed triangle meshes.
/// Bounds checks remain the caller's broad phase.
/// </summary>
internal static class TriangleMeshRaycaster
{
    private const float Epsilon = 0.000001f;

    public static bool TryCreateContext(
        Ray worldRay,
        Matrix4x4 modelMatrix,
        out TriangleRaycastContext context)
    {
        context = default;
        if (!Matrix4x4.Invert(modelMatrix, out var inverseModel))
            return false;

        var localDirection = Vector3.TransformNormal(worldRay.Direction, inverseModel);
        if (!float.IsFinite(localDirection.X) ||
            !float.IsFinite(localDirection.Y) ||
            !float.IsFinite(localDirection.Z) ||
            localDirection.LengthSquared() <= Epsilon)
        {
            return false;
        }

        context = new TriangleRaycastContext(
            worldRay,
            new Ray(
                Vector3.Transform(worldRay.Origin, inverseModel),
                Vector3.Normalize(localDirection)),
            modelMatrix);
        return true;
    }

    public static bool TryIntersectTriangles(
        in TriangleRaycastContext context,
        ReadOnlySpan<Vector3> vertices,
        ReadOnlySpan<ushort> indices,
        float maximumWorldDistance,
        out float worldDistance)
    {
        worldDistance = maximumWorldDistance;
        var hit = false;

        for (var index = 0; index + 2 < indices.Length; index += 3)
        {
            var i0 = indices[index];
            var i1 = indices[index + 1];
            var i2 = indices[index + 2];
            if (i0 == i1 || i1 == i2 || i0 == i2 ||
                i0 >= vertices.Length ||
                i1 >= vertices.Length ||
                i2 >= vertices.Length ||
                !TryIntersectTriangle(
                    context.LocalRay,
                    vertices[i0],
                    vertices[i1],
                    vertices[i2],
                    out var localDistance))
            {
                continue;
            }

            var localHit = context.LocalRay.GetPoint(localDistance);
            var worldHit = Vector3.Transform(localHit, context.ModelMatrix);
            var candidateDistance = Vector3.Distance(context.WorldRay.Origin, worldHit);
            if (!float.IsFinite(candidateDistance) || candidateDistance >= worldDistance)
                continue;

            worldDistance = candidateDistance;
            hit = true;
        }

        return hit;
    }

    private static bool TryIntersectTriangle(
        Ray ray,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        out float distance)
    {
        distance = 0f;
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var perpendicular = Vector3.Cross(ray.Direction, edge2);
        var determinant = Vector3.Dot(edge1, perpendicular);
        if (MathF.Abs(determinant) < Epsilon)
            return false;

        var inverseDeterminant = 1f / determinant;
        var originOffset = ray.Origin - v0;
        var u = Vector3.Dot(originOffset, perpendicular) * inverseDeterminant;
        if (u < 0f || u > 1f)
            return false;

        var perpendicularOffset = Vector3.Cross(originOffset, edge1);
        var v = Vector3.Dot(ray.Direction, perpendicularOffset) * inverseDeterminant;
        if (v < 0f || u + v > 1f)
            return false;

        distance = Vector3.Dot(edge2, perpendicularOffset) * inverseDeterminant;
        return distance >= 0f;
    }
}
