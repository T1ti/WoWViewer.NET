using System.Numerics;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Raycasting;

internal static class M2SelectionRaycaster
{
    public static bool TryIntersect(
        Ray ray,
        in ParsedDoodadBatch model,
        Matrix4x4 modelMatrix,
        float maximumDistance,
        out float distance)
        => TryIntersect(ray, model, modelMatrix, maximumDistance,
            ReadOnlySpan<M2RibbonMesh>.Empty, ReadOnlySpan<int>.Empty,
            out distance);

    public static bool TryIntersect(
        Ray ray,
        in ParsedDoodadBatch model,
        Matrix4x4 modelMatrix,
        float maximumDistance,
        ReadOnlySpan<M2RibbonMesh> particleMeshes,
        ReadOnlySpan<int> renderableParticleIndices,
        out float distance)
    {
        distance = maximumDistance;
        if (!TriangleMeshRaycaster.TryCreateContext(ray, modelMatrix, out var context))
            return false;

        if (model.particleEmitterCount > 0)
        {
            // Particle quads are reconstructed by the effect renderer. Compute
            // their individual bounds only while raycasting a selection click.
            // A single emitter-wide box can include a large amount of empty space.
            var hasParticleGeometry = false;
            var hit = false;
            foreach (var particleIndex in renderableParticleIndices)
            {
                if ((uint)particleIndex >= particleMeshes.Length)
                    continue;
                var mesh = particleMeshes[particleIndex];
                if (mesh.Indices is not { Length: > 0 } ||
                    mesh.Vertices is not { Length: >= 4 } vertices)
                    continue;

                hasParticleGeometry = true;
                for (var vertexIndex = 0; vertexIndex + 3 < vertices.Length; vertexIndex += 4)
                {
                    var min = vertices[vertexIndex].Position;
                    var max = min;
                    for (var corner = 1; corner < 4; corner++)
                    {
                        var position = vertices[vertexIndex + corner].Position;
                        min = Vector3.Min(min, position);
                        max = Vector3.Max(max, position);
                    }

                    if (!IsFinite(min) || !IsFinite(max))
                        continue;

                    // Camera-facing quads can have zero thickness on one axis.
                    // A small local-space margin makes those bounds pickable.
                    var margin = new Vector3(0.05f);
                    var bounds = new BoundingBox(min - margin, max + margin);
                    if (TryIntersectBox(context, bounds, distance, out var candidate))
                    {
                        distance = candidate;
                        hit = true;
                    }
                }
            }

            if (hasParticleGeometry)
            {
                if (IntersectionTests.RayIntersectsBox(
                        context.LocalRay, model.boundingBox, out _) &&
                    TryIntersectTriangles(context, model, distance, out var triangleDistance))
                {
                    distance = triangleDistance;
                    hit = true;
                }
                return hit;
            }

            // Formats without decoded particle geometry keep the prior box
            // selection behavior, including particle-only models at startup.
            return TryIntersectBox(context, model.boundingBox, maximumDistance,
                out distance);
        }

        return TryIntersectTriangles(context, model, maximumDistance, out distance);
    }

    private static bool TryIntersectTriangles(
        in TriangleRaycastContext context,
        in ParsedDoodadBatch model,
        float maximumDistance,
        out float distance)
    {
        distance = maximumDistance;
        return model.raycastVertices is { Length: > 0 } vertices &&
            model.raycastIndices is { Length: > 2 } indices &&
            TriangleMeshRaycaster.TryIntersectTriangles(
                context, vertices, indices, maximumDistance, out distance);
    }

    private static bool TryIntersectBox(
        in TriangleRaycastContext context,
        BoundingBox bounds,
        float maximumDistance,
        out float distance)
    {
        distance = maximumDistance;
        if (!IntersectionTests.RayIntersectsBox(
                context.LocalRay, bounds, out var localDistance))
            return false;

        var worldHit = Vector3.Transform(
            context.LocalRay.GetPoint(localDistance), context.ModelMatrix);
        var worldDistance = Vector3.Distance(context.WorldRay.Origin, worldHit);
        if (!float.IsFinite(worldDistance) || worldDistance >= maximumDistance)
            return false;

        distance = worldDistance;
        return true;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
