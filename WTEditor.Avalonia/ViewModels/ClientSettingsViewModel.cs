using System;
using System.Collections.Generic;
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
    [ObservableProperty] private float _renderDistance;
    [ObservableProperty] private int _tileLoadingDistance;
    [ObservableProperty] private float _movementSpeed;
    [ObservableProperty] private float _mouseSensitivity;
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
        _renderDistance = rendererSettings.RenderDistance;
        _tileLoadingDistance = rendererSettings.TileLoadingDistance;
        _movementSpeed = rendererSettings.MovementSpeed;
        _mouseSensitivity = rendererSettings.MouseSensitivity;
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
        RenderDistance = Math.Clamp(RenderDistance, 100f, 1_000_000f),
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
