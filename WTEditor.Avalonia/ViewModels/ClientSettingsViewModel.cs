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
    }

    public EditorSettingsSnapshot ApplyTo(EditorSettingsSnapshot original) => original with
    {
        Rendering = original.Rendering with
        {
            IsForegroundFrameRateLimitEnabled = IsForegroundFrameRateLimitEnabled,
            ViewportFrameRateLimit = ViewportFrameRateLimit,
            AmbientColor = new Vector3(AmbientColorR, AmbientColorG, AmbientColorB),
            DiffuseColor = new Vector3(DiffuseColorR, DiffuseColorG, DiffuseColorB),
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
