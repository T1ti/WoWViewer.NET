using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct WmoViewerPlacement(
    WMOContainer? Instance, WorldModel Model, int PrimaryGroupIndex, int SecondaryGroupIndex);

internal readonly record struct WmoViewerGroups(int PrimaryGroupIndex, int SecondaryGroupIndex);

internal readonly record struct WmoSceneViewerResult(
    WmoViewerPlacement Primary, WmoViewerPlacement Secondary, bool UsesWrath335Rules = false)
{
    public WmoViewerGroups? GetViewerGroups(WMOContainer instance)
    {
        if (!UsesWrath335Rules)
            return null;
        if (ReferenceEquals(instance, Primary.Instance))
            return new(Primary.PrimaryGroupIndex, Primary.SecondaryGroupIndex);
        if (ReferenceEquals(instance, Secondary.Instance))
            return new(Secondary.PrimaryGroupIndex, Secondary.SecondaryGroupIndex);
        return new(-1, -1);
    }
}

/// <summary>Scene-level CPU viewer query. SceneManager supplies lifetime and terrain policy.</summary>
internal sealed class WmoSceneViewerQuery(Func<Vector3, float> terrainDistance)
{
    public WmoSceneViewerResult Locate(IReadOnlyList<Container3D> sceneObjects,
        Dictionary<(uint FileDataID, string GroupSignature), List<WMOContainer>> legacyInstances,
        Vector3 eye, ref float? terrainLimit)
    {
        foreach (var sceneObject in sceneObjects)
        {
            if (sceneObject is WMOContainer { IsLoaded: true } instance &&
                instance.GetWMO() is { wrath335: true, legacyLighting: true })
                return LocateWrath335(sceneObjects, eye, ref terrainLimit);
        }
        return LocateOtherLegacy(legacyInstances, eye, ref terrainLimit);
    }

    private WmoSceneViewerResult LocateWrath335(IReadOnlyList<Container3D> sceneObjects,
        Vector3 eye, ref float? terrainLimit)
    {
        var selection = new Wrath335ViewerPlacementSelection(
            terrainLimit ?? WmoPortalVisibility.ClientViewerRayLength);
        var initialized = false;
        // 0x6DED60 appends to the native list despite its guessed "Head" name.
        // GPU asset/group buckets must not determine viewer traversal order.
        for (var index = 0; index < sceneObjects.Count; index++)
        {
            if (sceneObjects[index] is not WMOContainer { IsLoaded: true } instance ||
                !Wrath335ViewerPlacementSelection.CanQuery(instance.ViewerRuntimeFlags))
                continue;
            var wmo = instance.GetWMO();
            if (!wmo.wrath335 || !wmo.legacyLighting ||
                !IntersectsViewerSegment(instance.GetViewerQueryBounds(), eye))
                continue;

            if (!initialized)
            {
                terrainLimit ??= terrainDistance(eye);
                selection = new(terrainLimit.Value);
                initialized = true;
            }
            instance.GetPortalVisibilityBuffers(wmo, out _, out _, out _, out var scratch);
            scratch.PrepareViewer(wmo.groupBatches.Length);
            Wrath335WmoViewerQuery.Locate(wmo, instance.GetModelMatrix(), eye,
                instance.EnabledGroups, scratch,
                selection.GetMaximumDistance(instance.ViewerRuntimeFlags),
                out var distance, out var hit);
            if (hit)
                selection.AcceptQueryHit(instance.ViewerRuntimeFlags, index, distance,
                    scratch.PrimaryViewerGroupIndex, scratch.SecondaryViewerGroupIndex);
        }
        selection.GetResults(out var primary, out var secondary);
        return new(Resolve(primary, sceneObjects), Resolve(secondary, sceneObjects), true);
    }

    private WmoSceneViewerResult LocateOtherLegacy(
        Dictionary<(uint FileDataID, string GroupSignature), List<WMOContainer>> instancesByAsset,
        Vector3 eye, ref float? terrainLimit)
    {
        var primary = default(WmoViewerPlacement);
        var nearestHit = float.PositiveInfinity;
        // Preserve the existing behavior for clients whose placement caller has
        // not been recovered. The 12340 pool rules are not shared by assumption.
        foreach (var instances in instancesByAsset.Values)
        {
            if (instances.Count == 0 || !instances[0].IsLoaded)
                continue;
            var wmo = instances[0].GetWMO();
            if (!wmo.legacyLighting)
                continue;
            foreach (var instance in instances)
            {
                if (!instance.IsLoaded || !IntersectsViewerSegment(instance.GetBoundingBox(), eye))
                    continue;
                terrainLimit ??= terrainDistance(eye);
                instance.GetPortalVisibilityBuffers(wmo, out _, out _, out _, out var scratch);
                if (!WmoPortalVisibility.TryLocateViewerGroup(wmo, instance.GetModelMatrix(), eye,
                        instance.EnabledGroups, scratch, out var groupIndex, terrainLimit.Value) ||
                    scratch.PrimaryViewerHitDistance >= nearestHit)
                    continue;
                nearestHit = scratch.PrimaryViewerHitDistance;
                primary = new(instance, wmo, groupIndex, scratch.SecondaryViewerGroupIndex);
            }
        }
        return new(primary, default);
    }

    private static WmoViewerPlacement Resolve(WmoViewerSlot slot, IReadOnlyList<Container3D> sceneObjects)
    {
        if (!slot.HasPlacement)
            return new(null, default, -1, -1);
        var instance = (WMOContainer)sceneObjects[slot.PlacementIndex];
        return new(instance, instance.GetWMO(), slot.PrimaryGroupIndex, slot.SecondaryGroupIndex);
    }

    private static bool IntersectsViewerSegment(WoWRenderLib.Structs.BoundingBox? candidate, Vector3 eye) =>
        candidate is { } bounds &&
        eye.X >= bounds.Min.X && eye.X <= bounds.Max.X &&
        eye.Y >= bounds.Min.Y && eye.Y <= bounds.Max.Y && eye.Z >= bounds.Min.Z &&
        eye.Z - bounds.Max.Z <= WmoPortalVisibility.ClientViewerRayLength;
}
