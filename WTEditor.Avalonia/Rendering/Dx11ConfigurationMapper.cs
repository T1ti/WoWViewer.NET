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
        AmbientColor = configuration.AmbientColor,
        DiffuseColor = configuration.DiffuseColor,
        TerrainRenderDistance = configuration.TerrainRenderDistance,
        ModelRenderDistance = configuration.ModelRenderDistance,
        MinimumModelScreenSizePixels = configuration.MinimumModelScreenSizePixels,
        TerrainLodTransitionPixels = configuration.TerrainLodTransitionPixels,
        TileLoadingDistance = configuration.TileLoadingDistance,
        MovementSpeed = configuration.MovementSpeed,
        MouseSensitivity = configuration.MouseSensitivity,
        RenderADT = configuration.RenderADT,
        RenderWMO = configuration.RenderWMO,
        RenderM2 = configuration.RenderM2,
        EnableWmoPortalCulling = configuration.EnableWmoPortalCulling,
        ShowBoundingBoxes = configuration.ShowBoundingBoxes,
        ShowBoundingSpheres = configuration.ShowBoundingSpheres
    };
}
