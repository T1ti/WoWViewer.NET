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
    [ObservableProperty] private float _terrainRenderDistance;
    [ObservableProperty] private float _modelRenderDistance;
    [ObservableProperty] private float _minimumModelScreenSizePixels;
    [ObservableProperty] private float _terrainLodTransitionPixels;
    [ObservableProperty] private int _tileLoadingDistance;
    [ObservableProperty] private float _movementSpeed;
    [ObservableProperty] private float _mouseSensitivity;
    [ObservableProperty] private float _ambientColorR;
    [ObservableProperty] private float _ambientColorG;
    [ObservableProperty] private float _ambientColorB;
    [ObservableProperty] private float _diffuseColorR;
    [ObservableProperty] private float _diffuseColorG;
    [ObservableProperty] private float _diffuseColorB;
    [ObservableProperty] private bool _showBoundingBoxes;
    [ObservableProperty] private bool _showBoundingSpheres;
    [ObservableProperty] private bool _showTerrainGrid;
    [ObservableProperty] private bool _showTerrainWireframe;

    public ClientSettingsViewModel(EditorSettingsSnapshot settings)
    {
        var rendererSettings = settings.Rendering;
        _keyboardLayout = ToDisplayName(settings.KeyboardLayout);
        _isForegroundFrameRateLimitEnabled = rendererSettings.IsForegroundFrameRateLimitEnabled;
        _viewportFrameRateLimit = rendererSettings.ViewportFrameRateLimit;
        _terrainRenderDistance = rendererSettings.TerrainRenderDistance;
        _modelRenderDistance = rendererSettings.ModelRenderDistance;
        _minimumModelScreenSizePixels = rendererSettings.MinimumModelScreenSizePixels;
        _terrainLodTransitionPixels = rendererSettings.TerrainLodTransitionPixels;
        _tileLoadingDistance = rendererSettings.TileLoadingDistance;
        _movementSpeed = rendererSettings.MovementSpeed;
        _mouseSensitivity = rendererSettings.MouseSensitivity;
        _ambientColorR = rendererSettings.AmbientColor.X;
        _ambientColorG = rendererSettings.AmbientColor.Y;
        _ambientColorB = rendererSettings.AmbientColor.Z;
        _diffuseColorR = rendererSettings.DiffuseColor.X;
        _diffuseColorG = rendererSettings.DiffuseColor.Y;
        _diffuseColorB = rendererSettings.DiffuseColor.Z;
        _showBoundingBoxes = rendererSettings.ShowBoundingBoxes;
        _showBoundingSpheres = rendererSettings.ShowBoundingSpheres;
        _showTerrainGrid = rendererSettings.ShowTerrainGrid;
        _showTerrainWireframe = rendererSettings.ShowTerrainWireframe;
    }

    public EditorSettingsSnapshot ApplyTo(EditorSettingsSnapshot original) => original with
    {
        Client = original.Client,
        Rendering = new RenderingConfiguration
        {
            IsForegroundFrameRateLimitEnabled = IsForegroundFrameRateLimitEnabled,
            IsForegroundFrameRateLimitInitialized = original.Rendering.IsForegroundFrameRateLimitInitialized,
            ViewportFrameRateLimit = ViewportFrameRateLimit,
            AmbientColor = new Vector3(AmbientColorR, AmbientColorG, AmbientColorB),
            DiffuseColor = new Vector3(DiffuseColorR, DiffuseColorG, DiffuseColorB),
            TerrainRenderDistance = TerrainRenderDistance,
            ModelRenderDistance = ModelRenderDistance,
            MinimumModelScreenSizePixels = MinimumModelScreenSizePixels,
            TerrainLodTransitionPixels = TerrainLodTransitionPixels,
            TileLoadingDistance = TileLoadingDistance,
            MovementSpeed = MovementSpeed,
            MouseSensitivity = MouseSensitivity,
            RenderADT = original.Rendering.RenderADT,
            RenderWMO = original.Rendering.RenderWMO,
            RenderM2 = original.Rendering.RenderM2,
            EnableWmoPortalCulling = original.Rendering.EnableWmoPortalCulling,
            ShowBoundingBoxes = ShowBoundingBoxes,
            ShowBoundingSpheres = ShowBoundingSpheres,
            ShowTerrainGrid = ShowTerrainGrid,
            ShowTerrainWireframe = ShowTerrainWireframe
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
