using CommunityToolkit.Mvvm.ComponentModel;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.ViewModels;

public enum BuiltInBrushPreset
{
    HardRound,
    LinearRound,
    SmoothRound,
    GaussianRound,
    HardSquare,
    SmoothSquare
}

public static class BuiltInBrushPresets
{
    public static BuiltInBrushPreset[] All { get; } = Enum.GetValues<BuiltInBrushPreset>();
}

public sealed record BrushPresetViewModel(
    BuiltInBrushPreset Id,
    string DisplayName,
    string Description,
    BrushShape Shape,
    BrushFalloffProfile FalloffProfile,
    bool SupportsFalloff);

/// <summary>
/// Tool-independent brush state. Each tool owns an instance and declares which
/// presets it supports. Falloff availability is derived from tool and preset
/// capabilities rather than exposed as a user toggle.
/// </summary>
public partial class BrushSettingsViewModel : ViewModelBase
{
    private static readonly IReadOnlyDictionary<BuiltInBrushPreset, BrushPresetViewModel> KnownBrushes =
        new Dictionary<BuiltInBrushPreset, BrushPresetViewModel>
        {
            [BuiltInBrushPreset.HardRound] = new(
                BuiltInBrushPreset.HardRound, "Hard round", "White circular mask with a hard edge.",
                BrushShape.Circle, BrushFalloffProfile.Hard, false),
            [BuiltInBrushPreset.LinearRound] = new(
                BuiltInBrushPreset.LinearRound, "Linear round", "Circular mask with a constant-rate edge fade.",
                BrushShape.Circle, BrushFalloffProfile.Linear, true),
            [BuiltInBrushPreset.SmoothRound] = new(
                BuiltInBrushPreset.SmoothRound, "Smooth round", "Circular mask with a smoothstep edge fade.",
                BrushShape.Circle, BrushFalloffProfile.Smooth, true),
            [BuiltInBrushPreset.GaussianRound] = new(
                BuiltInBrushPreset.GaussianRound, "Gaussian round", "Circular mask with a soft bell-shaped edge fade.",
                BrushShape.Circle, BrushFalloffProfile.Gaussian, true),
            [BuiltInBrushPreset.HardSquare] = new(
                BuiltInBrushPreset.HardSquare, "Hard square", "White axis-aligned square mask with a hard edge.",
                BrushShape.Square, BrushFalloffProfile.Hard, false),
            [BuiltInBrushPreset.SmoothSquare] = new(
                BuiltInBrushPreset.SmoothSquare, "Smooth square", "Square mask with a smoothstep edge fade.",
                BrushShape.Square, BrushFalloffProfile.Smooth, true)
        };

    private bool _toolUsesFalloff;

    public BrushSettingsViewModel(params BuiltInBrushPreset[] supportedBrushes)
    {
        var brushes = supportedBrushes.Length == 0
            ? [BuiltInBrushPreset.SmoothRound]
            : supportedBrushes;
        AvailableBrushes = brushes.Distinct().Select(brush => KnownBrushes[brush]).ToArray();
        _selectedBrush = AvailableBrushes.FirstOrDefault(
            brush => brush.Id == BuiltInBrushPreset.SmoothRound) ?? AvailableBrushes[0];
    }

    public IReadOnlyList<BrushPresetViewModel> AvailableBrushes { get; }
    public BrushShape Shape => SelectedBrush.Shape;
    public BrushFalloffProfile FalloffProfile => SelectedBrush.FalloffProfile;
    public bool HasFalloff => _toolUsesFalloff && SelectedBrush.SupportsFalloff;
    public bool IsFalloffVisible => HasFalloff;
    public double SizeMinimum => 1d;
    public double SizeMaximum => 1000d;
    public double SizeCurve => 3.5d;

    [ObservableProperty]
    private BrushPresetViewModel _selectedBrush;

    [ObservableProperty]
    private double _size = 10d;

    [ObservableProperty]
    private double _falloff = 0.35d;

    internal void SetToolUsesFalloff(bool value)
    {
        if (_toolUsesFalloff == value)
            return;

        _toolUsesFalloff = value;
        OnPropertyChanged(nameof(HasFalloff));
        OnPropertyChanged(nameof(IsFalloffVisible));
    }

    partial void OnSelectedBrushChanged(BrushPresetViewModel value)
    {
        OnPropertyChanged(nameof(Shape));
        OnPropertyChanged(nameof(FalloffProfile));
        OnPropertyChanged(nameof(HasFalloff));
        OnPropertyChanged(nameof(IsFalloffVisible));
    }

    partial void OnSizeChanged(double value) => Size = Math.Clamp(value, SizeMinimum, SizeMaximum);
    partial void OnFalloffChanged(double value) => Falloff = Math.Clamp(value, 0d, 1d);
}
