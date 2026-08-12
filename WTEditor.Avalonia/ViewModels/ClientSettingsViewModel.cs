using System;
using System.Collections.Generic;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.ViewModels;

public partial class ClientSettingsViewModel : ViewModelBase
{
    public IReadOnlyList<string> KeyboardLayouts { get; } = ["Auto", "QWERTY", "AZERTY"];

    [ObservableProperty] private string _wowDirectory;
    [ObservableProperty] private string _wowProduct;
    [ObservableProperty] private string _buildConfig;
    [ObservableProperty] private string _cdnConfig;
    [ObservableProperty] private string _keyboardLayout;
    [ObservableProperty] private float _terrainRenderDistance;
    [ObservableProperty] private float _modelRenderDistance;
    [ObservableProperty] private int _tileLoadingDistance;
    [ObservableProperty] private float _movementSpeed;
    [ObservableProperty] private float _mouseSensitivity;
    [ObservableProperty] private float _ambientColorR;
    [ObservableProperty] private float _ambientColorG;
    [ObservableProperty] private float _ambientColorB;
    [ObservableProperty] private float _diffuseColorR;
    [ObservableProperty] private float _diffuseColorG;
    [ObservableProperty] private float _diffuseColorB;
    [ObservableProperty] private bool _renderAdt;
    [ObservableProperty] private bool _renderWmo;
    [ObservableProperty] private bool _renderM2;
    [ObservableProperty] private bool _showBoundingBoxes;
    [ObservableProperty] private bool _showBoundingSpheres;

    public ClientSettingsViewModel(WowClientConfig config, RendererSettings rendererSettings, string keyboardLayout)
    {
        _wowDirectory = config.wowDir;
        _wowProduct = config.wowProduct;
        _buildConfig = config.buildConfig;
        _cdnConfig = config.cdnConfig;
        _keyboardLayout = keyboardLayout;
        _terrainRenderDistance = rendererSettings.TerrainRenderDistance;
        _modelRenderDistance = rendererSettings.ModelRenderDistance;
        _tileLoadingDistance = rendererSettings.TileLoadingDistance;
        _movementSpeed = rendererSettings.MovementSpeed;
        _mouseSensitivity = rendererSettings.MouseSensitivity;
        _ambientColorR = rendererSettings.AmbientColor.X;
        _ambientColorG = rendererSettings.AmbientColor.Y;
        _ambientColorB = rendererSettings.AmbientColor.Z;
        _diffuseColorR = rendererSettings.DiffuseColor.X;
        _diffuseColorG = rendererSettings.DiffuseColor.Y;
        _diffuseColorB = rendererSettings.DiffuseColor.Z;
        _renderAdt = rendererSettings.RenderADT;
        _renderWmo = rendererSettings.RenderWMO;
        _renderM2 = rendererSettings.RenderM2;
        _showBoundingBoxes = rendererSettings.ShowBoundingBoxes;
        _showBoundingSpheres = rendererSettings.ShowBoundingSpheres;
    }

    public WowClientConfig ToConfig() => new()
    {
        wowDir = WowDirectory.Trim(), wowProduct = WowProduct.Trim(),
        buildConfig = BuildConfig.Trim(), cdnConfig = CdnConfig.Trim()
    };

    public RendererSettings ToRendererSettings() => new()
    {
        AmbientColor = new Vector3(
            Math.Clamp(AmbientColorR, 0f, 4f),
            Math.Clamp(AmbientColorG, 0f, 4f),
            Math.Clamp(AmbientColorB, 0f, 4f)),
        DiffuseColor = new Vector3(
            Math.Clamp(DiffuseColorR, 0f, 4f),
            Math.Clamp(DiffuseColorG, 0f, 4f),
            Math.Clamp(DiffuseColorB, 0f, 4f)),
        TerrainRenderDistance = Math.Clamp(TerrainRenderDistance, 100f, 1_000_000f),
        ModelRenderDistance = Math.Clamp(ModelRenderDistance, 100f, 1_000_000f),
        TileLoadingDistance = Math.Clamp(TileLoadingDistance, 0, 32),
        MovementSpeed = Math.Clamp(MovementSpeed, 1f, 10_000f),
        MouseSensitivity = Math.Clamp(MouseSensitivity, 0.001f, 2f),
        RenderADT = RenderAdt,
        RenderWMO = RenderWmo,
        RenderM2 = RenderM2,
        ShowBoundingBoxes = ShowBoundingBoxes,
        ShowBoundingSpheres = ShowBoundingSpheres
    };
}
