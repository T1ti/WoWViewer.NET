using WTEditor.Application.Models;
using WoWRenderLib.DX11.Structs;

namespace WTEditor.Avalonia.Presentation;

internal static class LightingSettingsProjection
{
    public static LightingSettingsSnapshot ToDisplay(WorldLightingSettings lighting) => new(
        lighting.LightParamId,
        lighting.Time,
        lighting.LightDirection,
        lighting.AmbientColor,
        lighting.DiffuseColor,
        lighting.OceanCloseColor,
        lighting.OceanFarColor,
        lighting.RiverCloseColor,
        lighting.RiverFarColor,
        lighting.WaterShallowAlpha,
        lighting.WaterDeepAlpha,
        lighting.OceanShallowAlpha,
        lighting.OceanDeepAlpha,
        lighting.HasLiquidColorData,
        lighting.HasLiquidAlphaData);

    public static WorldLightingSettings ToRenderer(LightingSettingsSnapshot lighting) => new(
        lighting.LightParamId,
        lighting.Time,
        lighting.LightDirection,
        lighting.AmbientColor,
        lighting.DiffuseColor,
        lighting.OceanCloseColor,
        lighting.OceanFarColor,
        lighting.RiverCloseColor,
        lighting.RiverFarColor,
        lighting.WaterShallowAlpha,
        lighting.WaterDeepAlpha,
        lighting.OceanShallowAlpha,
        lighting.OceanDeepAlpha,
        lighting.HasLiquidColorData,
        lighting.HasLiquidAlphaData);
}
