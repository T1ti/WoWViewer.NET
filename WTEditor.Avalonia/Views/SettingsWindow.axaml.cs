using Avalonia.Controls;
using static System.Net.Mime.MediaTypeNames;

namespace WTEditor.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void Apply_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(true);
    private void Cancel_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(false);

    // client UI code
    //EffectsPanelOptions = {
    //farclip = { text = "FARCLIP", minValue = OPTIONS_FARCLIP_MIN, maxValue = OPTIONS_FARCLIP_MAX, valueStep = (OPTIONS_FARCLIP_MAX - OPTIONS_FARCLIP_MIN)/10},
    //TerrainMip = { text = "TERRAIN_MIP", minValue = 0, maxValue = 1, valueStep = 1, logout = 1, tooltip = OPTION_TOOLTIP_TERRAIN_TEXTURE, tooltipRequirement = OPTION_LOGOUT_REQUIREMENT,},
    //particleDensity = { text = "PARTICLE_DENSITY", minValue = 0.1, maxValue = 1.0, valueStep = 0.1},
    //environmentDetail = { text = "ENVIRONMENT_DETAIL", minValue = 0.5, maxValue = 1.5, valueStep = .25},
    //groundEffectDensity = { text = "GROUND_DENSITY", minValue = 16, maxValue = 64, valueStep = 8},
    //groundEffectDist = { text = "GROUND_RADIUS", minValue = 70, maxValue = 140, valueStep = 10 },
    //BaseMip = { text = "TEXTURE_DETAIL", minValue = 0, maxValue = 1, valueStep = 1, tooltipOwnerPoint = "TOPLEFT", },
    //extShadowQuality = { text = "SHADOW_QUALITY", minValue = 0, maxValue = 4, valueStep = 1 },
    //textureFilteringMode = { text = "ANISOTROPIC", minValue = 0, maxValue = 5, valueStep = 1, gameRestart = 1, tooltipOwnerPoint = "TOPLEFT", tooltipRequirement = OPTION_RESTART_REQUIREMENT, },
    //weatherDensity = { text = "WEATHER_DETAIL", minValue = 0, maxValue = 3, valueStep = 1, tooltipOwnerPoint = "TOPLEFT", },
    //componentTextureLevel = { text = "PLAYER_DETAIL", minValue = 8, maxValue = 9, valueStep = 1, tooltipPoint = "BOTTOMRIGHT", tooltipOwnerPoint = "TOPLEFT", gameRestart = 1, tooltipRequirement = OPTION_RESTART_REQUIREMENT, },
    //specular = { text = "TERRAIN_HIGHLIGHTS", logout = 1, tooltipRequirement = OPTION_LOGOUT_REQUIREMENT, },
    //ffxGlow = { text = "FULL_SCREEN_GLOW", },
    //ffxDeath = { text = "DEATH_EFFECT", },
    //projectedTextures = { text = "PROJECTED_TEXTURES", },
    //quality = { text = "", minValue = 1, maxValue = 6, valueStep = 1 },
    // }

    // ultra(max) settings : 
    // farclip : 1277
    // TerrainMip : 1
    // particleDensity : 1.0
    // environmentDetail : 1.5
    // groundEffectDensity : 64
    // groundEffectDist : 140
    // BaseMip : 1
    // extShadowQuality : 4
    // textureFilteringMode : 5
    // weatherDensity : 3
    // componentTextureLevel : 9
    // specular : 1
    // ffxGlow : 1
    // ffxDeath : 1
    // projectedTextures : 1
    // quality : 6 // quality slider (low - ultra...))
}
