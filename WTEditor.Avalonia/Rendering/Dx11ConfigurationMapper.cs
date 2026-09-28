using WTEditor.Application.Models;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.Rendering;

internal static class Dx11ConfigurationMapper
{
    public static WowClientConfig ToDx11(this ClientConfiguration configuration) => new()
    {
        wowDir = configuration.WowDirectory,
        wowProduct = configuration.WowProduct,
        buildConfig = configuration.BuildConfig,
        cdnConfig = configuration.CdnConfig
    };

    public static RendererSettings ToDx11(this RenderingConfiguration configuration) => new()
    {
        TerrainRenderDistance = configuration.TerrainRenderDistance,
        ModelRenderDistance = configuration.ModelRenderDistance,
        AnimationRenderDistancePercent = configuration.AnimationRenderDistancePercent,
        ParticleRenderDistancePercent = configuration.ParticleRenderDistancePercent,
        MinimumModelScreenSizePixels = configuration.MinimumModelScreenSizePixels,
        TerrainLodTransitionPixels = configuration.TerrainLodTransitionPixels,
        TileLoadingDistance = configuration.TileLoadingDistance,
        MovementSpeed = configuration.MovementSpeed,
        MouseSensitivity = configuration.MouseSensitivity,
        UseConfiguredLighting = false,
        RenderADT = configuration.RenderADT,
        RenderLiquid = configuration.RenderLiquid,
        RenderWMO = configuration.RenderWMO,
        ShowWmoCollisionMesh = configuration.ShowWmoCollisionMesh,
        RenderM2 = configuration.RenderM2,
        RenderParticles = configuration.RenderParticles,
        DisableScreenGlow = configuration.DisableScreenGlow,
        SkyCloudLod = configuration.SkyCloudLod,
        WrathFarClip = configuration.WrathFarClip,
        WrathFarClipOverride = configuration.WrathFarClipOverride,
        AnimateModels = configuration.AnimateModels,
        EnableWmoPortalCulling = configuration.EnableWmoPortalCulling,
        ShowBoundingBoxes = configuration.ShowBoundingBoxes,
        ShowBoundingSpheres = configuration.ShowBoundingSpheres,
        ShowTerrainGrid = configuration.ShowTerrainGrid,
        ShowTerrainWireframe = configuration.ShowTerrainWireframe,
        ShowTerrainTexture = configuration.ShowTerrainTexture,
        ShowTerrainVertexColor = configuration.ShowTerrainVertexColor
    };
}
