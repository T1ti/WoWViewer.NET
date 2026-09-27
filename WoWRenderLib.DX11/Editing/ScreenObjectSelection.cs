using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Editing;

internal static class ScreenObjectSelection
{
    public static void Apply(IReadOnlyList<Container3D> sceneObjects, ObjectSelection selection,
        ScreenSelectionRequest request, Camera camera, Vector2 viewport, bool renderM2, bool renderWmo, float renderDistance)
    {
        var volume = new ScreenSelectionVolume(request.Start, request.End, viewport,
            camera.GetViewMatrix() * camera.GetProjectionMatrix());
        var matches = new List<Container3D>();
        for (var index = 0; index < sceneObjects.Count; index++)
        {
            var item = sceneObjects[index];
            if (item is M2Container m2 ? !renderM2 || m2.ParentWMO != null : item is not WMOContainer || !renderWmo)
                continue;
            if (item.GetBoundingSphere() is not { } sphere ||
                !ScreenSpaceCulling.IntersectsRenderDistance(camera.Position, sphere.Center, sphere.Radius, renderDistance))
                continue;
            if (item.GetBoundingBox() is { } box && volume.Intersects(box))
                matches.Add(item);
        }
        var toggle = (request.Modifiers & InputModifiers.Control) != 0;
        if (!toggle && (request.Modifiers & InputModifiers.Shift) == 0)
            selection.Clear();
        // Scene objects are normally unique, but duplicate references must never toggle twice.
        foreach (var item in matches.Distinct())
            selection.Select(item, additive: true, toggle: toggle);
    }
}
