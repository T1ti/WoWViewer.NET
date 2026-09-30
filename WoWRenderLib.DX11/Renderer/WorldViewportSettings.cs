using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Separates saved editor overrides from the effective world renderer inputs.</summary>
internal static class WorldViewportSettings
{
    // Other client profiles retain the renderer's existing baseline until their
    // native distance and streaming policies are recovered; these are not CVars.
    public const float FallbackRenderDistance = 20_000f;
    public const int FallbackTileLoadingDistance = 4;

    public static RendererSettings Resolve(RendererSettings saved)
    {
        var effective = saved.Clone();
        if (!saved.UseClientRenderingRules)
        {
            effective.DisableScreenGlow = saved.EditorDisableScreenGlow;
            return effective;
        }

        effective.RenderADT = true;
        effective.RenderLiquid = true;
        effective.RenderWMO = true;
        effective.RenderM2 = true;
        effective.RenderParticles = true;
        effective.RenderFog = true;
        effective.AnimateModels = true;
        effective.ShowWmoCollisionMesh = false;
        effective.ShowBoundingBoxes = false;
        effective.ShowBoundingSpheres = false;
        effective.ShowTerrainGrid = false;
        effective.ShowTerrainWireframe = false;
        effective.ShowTerrainTexture = true;
        effective.ShowTerrainVertexColor = true;
        effective.UseConfiguredLighting = false;
        effective.EnableWmoPortalCulling = false;
        effective.MinimumModelScreenSizePixels = 0f;
        effective.TerrainLodTransitionPixels = 0f;
        effective.TerrainRenderDistance = FallbackRenderDistance;
        effective.ModelRenderDistance = FallbackRenderDistance;
        effective.TileLoadingDistance = FallbackTileLoadingDistance;
        effective.AnimationRenderDistancePercent = 100f;
        effective.ParticleRenderDistancePercent = 100f;
        return effective;
    }

    public static WorldLightingSettings ResolveLighting(
        bool clientMode, WorldLightingSettings editorLighting) =>
        clientMode ? WorldLightingSettings.Defaults with { IsDynamic = true } : editorLighting;
}
