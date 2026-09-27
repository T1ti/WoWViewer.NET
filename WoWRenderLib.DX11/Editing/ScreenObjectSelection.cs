using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Editing;

internal static class ScreenObjectSelection
{
    public static void Apply(IReadOnlyList<Container3D> sceneObjects, ObjectSelection selection,
        ScreenSelectionRequest request, Camera camera, Vector2 viewport, bool renderM2, bool renderWmo, float renderDistance,
        M2EffectRenderer? effects = null)
    {
        var viewProjection = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        var volume = new ScreenSelectionVolume(request.Start, request.End, viewport, viewProjection);
        var matches = new List<Container3D>();
        for (var index = 0; index < sceneObjects.Count; index++)
        {
            var item = sceneObjects[index];
            if (item is M2Container m2 ? !renderM2 || m2.ParentWMO != null : item is not WMOContainer || !renderWmo)
                continue;
            if (item.GetBoundingSphere() is not { } sphere ||
                !ScreenSpaceCulling.IntersectsRenderDistance(camera.Position, sphere.Center, sphere.Radius, renderDistance))
                continue;
            if (item.GetBoundingBox() is not { } box || !volume.Intersects(box)) continue;
            var localVolume = new ScreenSelectionVolume(request.Start, request.End, viewport,
                item.GetModelMatrix() * viewProjection);
            if (item.IntersectsScreenSelection(localVolume) || IntersectsParticles(item, localVolume, effects))
                matches.Add(item);
        }
        var toggle = (request.Modifiers & InputModifiers.Control) != 0;
        if (!toggle && (request.Modifiers & InputModifiers.Shift) == 0)
            selection.Clear();
        // Scene objects are normally unique, but duplicate references must never toggle twice.
        foreach (var item in matches.Distinct())
            selection.Select(item, additive: true, toggle: toggle);
    }

    private static bool IntersectsParticles(Container3D item, in ScreenSelectionVolume volume, M2EffectRenderer? effects)
    {
        if (item is not M2Container m2 || effects == null ||
            !effects.TryGetRenderedParticleMeshes(m2, out var animation, out var meshes) || animation == null ||
            !ReferenceEquals(m2.GetM2().animation, animation)) return false;
        foreach (var particleIndex in animation.RenderableParticleIndices)
        {
            if ((uint)particleIndex >= meshes.Length) continue;
            var mesh = meshes[particleIndex];
            if (mesh.Vertices == null || mesh.Indices == null) continue;
            for (var index = 0; index + 2 < mesh.Indices.Length; index += 3)
            {
                var a = mesh.Indices[index]; var b = mesh.Indices[index + 1]; var c = mesh.Indices[index + 2];
                if (a < mesh.Vertices.Length && b < mesh.Vertices.Length && c < mesh.Vertices.Length &&
                    volume.IntersectsTriangle(mesh.Vertices[a].Position, mesh.Vertices[b].Position, mesh.Vertices[c].Position))
                    return true;
            }
        }
        return false;
    }
}
