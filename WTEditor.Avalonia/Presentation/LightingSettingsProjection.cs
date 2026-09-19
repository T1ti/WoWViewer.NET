using WTEditor.Application.Models;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Presentation;

internal static class LightingSettingsProjection
{
    public static LightingSettingsSnapshot ToDisplay(
        WorldLightingSettings lighting,
        IReadOnlyList<WorldLightingContribution>? activeLights = null) =>
        ToDisplay(lighting, WorldSkyLighting.None, activeLights);

    public static LightingSettingsSnapshot ToDisplay(
        WorldLightingSettings lighting,
        WorldSkyLighting sky,
        IReadOnlyList<WorldLightingContribution>? activeLights = null) => new(
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
        lighting.HasLiquidAlphaData,
        lighting.IsDynamic,
            activeLights?
            .Select(static light => new ActiveLightingSnapshot(
                light.LightId,
                light.LightParamId,
                light.Kind switch
                {
                    WorldLightingSourceKind.Global => ActiveLightingSourceKind.Global,
                    WorldLightingSourceKind.Zone => ActiveLightingSourceKind.Zone,
                    _ => ActiveLightingSourceKind.Local
                },
                light.Weight,
                light.ZoneLightId,
                light.ZoneName))
            .ToArray(),
        new LightingRuntimeSnapshot(
            sky.TopColor,
            sky.MiddleColor,
            sky.Band1Color,
            sky.Band2Color,
            sky.SmogColor,
            sky.FogColor,
            sky.SunColor,
            sky.CloudSunColor,
            sky.CloudEmissiveColor,
            sky.CloudLayer1AmbientColor,
            sky.CloudLayer2AmbientColor,
            sky.HasColorData,
            sky.HasSunCloudData,
            sky.ShadowOpacity,
            sky.FogEnd,
            sky.FogScaler,
            sky.CloudDensity,
            sky.FogDensity,
            sky.FogHeight,
            sky.FogHeightScaler,
            sky.FogHeightDensity,
            sky.FogZScalar,
            sky.MainFogStartDistance,
            sky.MainFogEndDistance,
            sky.SunFogAngle,
            sky.EndFogColor,
            sky.EndFogColorDistance,
            sky.FogStartOffset,
            sky.SunFogColor,
            sky.SunFogStrength,
            sky.FogHeightColor,
            sky.EndFogHeightColor,
            sky.GroundAmbientColor,
            sky.HorizonAmbientColor,
            sky.FogHeightCoefficients,
            sky.MainFogCoefficients,
            sky.HeightDensityFogCoefficients,
            sky.ColorGradingFileDataId,
            sky.DarkerColorGradingFileDataId,
            sky.HasFogData,
            sky.HighlightSky,
            sky.Skyboxes?
                .Select(static layer => new LightingSkyboxSnapshot(
                    layer.FileDataId,
                    layer.Flags,
                    layer.Opacity))
                .ToArray()
                ?? Array.Empty<LightingSkyboxSnapshot>()));

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
        lighting.HasLiquidAlphaData,
        lighting.IsDynamic);
}
