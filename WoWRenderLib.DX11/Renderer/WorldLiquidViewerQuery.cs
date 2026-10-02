using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Per-frame 12340 viewer query. It reads loaded CPU grids regardless of draw
/// culling; a missing primary group is retried next frame, without terrain fallback.
/// </summary>
internal sealed class WorldLiquidViewerQuery(Func<Vector3, float> terrainHeight)
{
    internal Wrath335ViewerLiquid Locate(in WmoSceneViewerResult viewer,
        IReadOnlyList<Container3D> objects, IReadOnlyList<ADTContainer> terrain, Vector3 eye)
    {
        if (viewer.Primary.Instance is { } primary)
        {
            if (!viewer.Primary.Model.wrath335 || !Matrix4x4.Invert(primary.GetModelMatrix(), out var inverse))
                return default;
            // 0x79093E..0x790991: selected primary group only; depth is local.
            return QueryPrimary(viewer.Primary.Model, viewer.Primary.PrimaryGroupIndex,
                Vector3.Transform(eye, inverse));
        }
        // 0x7A09D0/0x7AEB90: first eligible placement/group, then terrain.
        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            var item = objects[objectIndex];
            if (item is not WMOContainer { IsLoaded: true } instance ||
                (instance.ViewerRuntimeFlags & 0x100) != 0) continue;
            var model = instance.GetWMO();
            if (!model.wrath335 || !Contains(instance.GetViewerQueryBounds(), eye) ||
                !Matrix4x4.Invert(instance.GetModelMatrix(), out var inverse)) continue;
            var localEye = Vector3.Transform(eye, inverse);
            if (!Contains(model.boundingBox, localEye)) continue;
            if (TryExterior(model, localEye, eye, instance.GetModelMatrix(), out var liquid)) return liquid;
        }
        for (var tileIndex = 0; tileIndex < terrain.Count; tileIndex++)
        {
            var tile = terrain[tileIndex];
            if (!tile.IsLoaded || tile.Terrain.worldLiquid.queryGrids is not { Length: > 0 } grids) continue;
            if (Wrath335LiquidQuery.TryQuery(grids, eye, out var liquid) &&
                (double)eye.Z + .009999999776482582f > terrainHeight(eye)) return liquid;
        }
        return default;
    }

    internal static Wrath335ViewerLiquid QueryPrimary(in WorldModel model, int groupIndex, Vector3 localEye) =>
        model.groupBatches != null && (uint)groupIndex < (uint)model.groupBatches.Length &&
        Wrath335LiquidQuery.TryQuery(model.groupBatches[groupIndex].liquid.queryGrids, localEye, out var liquid)
            ? liquid : default;

    internal static bool TryExterior(in WorldModel model, Vector3 localEye, Vector3 worldEye,
        in Matrix4x4 placement, out Wrath335ViewerLiquid liquid)
    {
        foreach (var group in model.groupBatches)
        {
            if ((group.mogiFlags & 0x2000) != 0 || !Contains(group.mogiBoundingBox, localEye)) continue;
            if (!Wrath335LiquidQuery.TryQuery(group.liquid.queryGrids, localEye, out var localLiquid)) continue;
            // 0x4C2353..0x4C235A modifies the input point in place. Its caller
            // 0x7A0AE5 then reads the transformed Z, even though the copy is unused.
            var surface = Vector3.Transform(new Vector3(localEye.X, localEye.Y,
                localEye.Z + localLiquid.Depth), placement);
            liquid = new(localLiquid.TypeId, surface.Z - worldEye.Z);
            return true;
        }
        liquid = default;
        return false;
    }

    private static bool Contains(BoundingBox? box, Vector3 point) => box is { } bounds &&
        point.X >= bounds.Min.X && point.X <= bounds.Max.X &&
        point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y &&
        point.Z >= bounds.Min.Z && point.Z <= bounds.Max.Z;
}
