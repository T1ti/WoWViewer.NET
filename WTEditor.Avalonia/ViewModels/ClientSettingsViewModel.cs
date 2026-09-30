using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.ViewModels;

public partial class ClientSettingsViewModel : ViewModelBase
{
    public IReadOnlyList<string> KeyboardLayouts { get; } = ["Auto", "QWERTY", "AZERTY"];

    [ObservableProperty] private string _keyboardLayout;
    [ObservableProperty] private bool _isForegroundFrameRateLimitEnabled;
    [ObservableProperty] private int _viewportFrameRateLimit;
    [ObservableProperty] private float _mouseSensitivity;
    [ObservableProperty] private float _ambientColorR;
    [ObservableProperty] private float _ambientColorG;
    [ObservableProperty] private float _ambientColorB;
    [ObservableProperty] private float _diffuseColorR;
    [ObservableProperty] private float _diffuseColorG;
    [ObservableProperty] private float _diffuseColorB;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private bool _disableScreenGlow;
    public bool FullScreenGlow
    {
        get => !DisableScreenGlow;
        set => DisableScreenGlow = !value;
    }

    partial void OnDisableScreenGlowChanged(bool value) => OnPropertyChanged(nameof(FullScreenGlow));

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _clientRenderingQuality;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _terrainMip;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private float _particleDensity;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private float _environmentDetail;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _groundEffectDensity;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _groundEffectDist;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _baseMip;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _extShadowQuality;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _textureFilteringMode;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _weatherDensity;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private int _componentTextureLevel;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private bool _specular;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private bool _ffxDeath;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private bool _projectedTextures;
    [ObservableProperty] private int _skyCloudLod;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ClientQualityLabel))] private float _wrathFarClip;
    [ObservableProperty] private bool _wrathFarClipOverride;

    public string ClientQualityLabel => WrathFarClip == RenderingConfiguration.DefaultWrathFarClip &&
        !DisableScreenGlow && ClientRenderingQuality == 6 &&
        TerrainMip == 1 &&
        ParticleDensity == 1f &&
        EnvironmentDetail == 1.5f &&
        GroundEffectDensity == 64 &&
        GroundEffectDist == 140 &&
        BaseMip == 1 &&
        ExtShadowQuality == 4 &&
        TextureFilteringMode == 5 &&
        WeatherDensity == 3 &&
        ComponentTextureLevel == 9 &&
        Specular == true &&
        FfxDeath == true &&
        ProjectedTextures == true
            ? "Ultra" : "Custom";

    public ClientSettingsViewModel(EditorSettingsSnapshot settings)
    {
        var rendererSettings = settings.Rendering;
        _keyboardLayout = ToDisplayName(settings.KeyboardLayout);
        _isForegroundFrameRateLimitEnabled = rendererSettings.IsForegroundFrameRateLimitEnabled;
        _viewportFrameRateLimit = rendererSettings.ViewportFrameRateLimit;
        _mouseSensitivity = rendererSettings.MouseSensitivity;
        _ambientColorR = rendererSettings.AmbientColor.X;
        _ambientColorG = rendererSettings.AmbientColor.Y;
        _ambientColorB = rendererSettings.AmbientColor.Z;
        _diffuseColorR = rendererSettings.DiffuseColor.X;
        _diffuseColorG = rendererSettings.DiffuseColor.Y;
        _diffuseColorB = rendererSettings.DiffuseColor.Z;
        _disableScreenGlow = rendererSettings.DisableScreenGlow;
        _clientRenderingQuality = rendererSettings.ClientRenderingQuality;
        _terrainMip = rendererSettings.TerrainMip;
        _particleDensity = rendererSettings.ParticleDensity;
        _environmentDetail = rendererSettings.EnvironmentDetail;
        _groundEffectDensity = rendererSettings.GroundEffectDensity;
        _groundEffectDist = rendererSettings.GroundEffectDist;
        _baseMip = rendererSettings.BaseMip;
        _extShadowQuality = rendererSettings.ExtShadowQuality;
        _textureFilteringMode = rendererSettings.TextureFilteringMode;
        _weatherDensity = rendererSettings.WeatherDensity;
        _componentTextureLevel = rendererSettings.ComponentTextureLevel;
        _specular = rendererSettings.Specular;
        _ffxDeath = rendererSettings.FfxDeath;
        _projectedTextures = rendererSettings.ProjectedTextures;
        _skyCloudLod = rendererSettings.SkyCloudLod;
        _wrathFarClip = rendererSettings.WrathFarClip;
        _wrathFarClipOverride = rendererSettings.WrathFarClipOverride;
    }

    public EditorSettingsSnapshot ApplyTo(EditorSettingsSnapshot original) => original with
    {
        Rendering = original.Rendering with
        {
            IsForegroundFrameRateLimitEnabled = IsForegroundFrameRateLimitEnabled,
            ViewportFrameRateLimit = ViewportFrameRateLimit,
            AmbientColor = new Vector3(AmbientColorR, AmbientColorG, AmbientColorB),
            DiffuseColor = new Vector3(DiffuseColorR, DiffuseColorG, DiffuseColorB),
            DisableScreenGlow = DisableScreenGlow,
            ClientRenderingQuality = ClientRenderingQuality,
            TerrainMip = TerrainMip,
            ParticleDensity = ParticleDensity,
            EnvironmentDetail = EnvironmentDetail,
            GroundEffectDensity = GroundEffectDensity,
            GroundEffectDist = GroundEffectDist,
            BaseMip = BaseMip,
            ExtShadowQuality = ExtShadowQuality,
            TextureFilteringMode = TextureFilteringMode,
            WeatherDensity = WeatherDensity,
            ComponentTextureLevel = ComponentTextureLevel,
            Specular = Specular,
            FfxDeath = FfxDeath,
            ProjectedTextures = ProjectedTextures,
            SkyCloudLod = SkyCloudLod,
            WrathFarClip = WrathFarClip,
            WrathFarClipOverride = WrathFarClipOverride,
            MouseSensitivity = MouseSensitivity,
        },
        KeyboardLayout = ParseKeyboardLayout(KeyboardLayout)
    };

    private static KeyboardLayoutMode ParseKeyboardLayout(string? value) => value?.ToUpperInvariant() switch
    {
        "QWERTY" => KeyboardLayoutMode.Qwerty,
        "AZERTY" => KeyboardLayoutMode.Azerty,
        _ => KeyboardLayoutMode.Auto
    };

    private static string ToDisplayName(KeyboardLayoutMode value) => value switch
    {
        KeyboardLayoutMode.Qwerty => "QWERTY",
        KeyboardLayoutMode.Azerty => "AZERTY",
        _ => "Auto"
    };
}
