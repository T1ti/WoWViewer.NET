using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Resolves recovered client rules without overwriting saved editor controls.
/// Additional client CVars and native LOD/fade rules require their own audited policies.
/// </summary>
internal readonly record struct WorldRenderingRules(
    bool UseClientRules, float NearPlane, float FarPlane,
    float TerrainDistance, float ModelDistance, bool PortalCulling,
    float MinimumModelPixels, float TerrainLodPixels)
{
    public static WorldRenderingRules Resolve(RendererSettings settings,
        bool isWrath335ReferenceClient, int mapId, bool expandedMemoryAvailable)
    {
        if (settings.UseClientRenderingRules && isWrath335ReferenceClient)
        {
            var far = Wrath335FarClip.Validate(settings.WrathFarClip,
                mapId, settings.WrathFarClipOverride, expandedMemoryAvailable);
            return new(true, Wrath335FarClip.FixedNearClip, far, far, far,
                true, 0f, 0f);
        }

        return new(false, 1f,
            MathF.Max(settings.TerrainRenderDistance, settings.ModelRenderDistance),
            settings.TerrainRenderDistance, settings.ModelRenderDistance,
            settings.EnableWmoPortalCulling,
            Math.Clamp(settings.MinimumModelScreenSizePixels, 0f, 16f),
            Math.Clamp(settings.TerrainLodTransitionPixels, 0f, 256f));
    }
}
