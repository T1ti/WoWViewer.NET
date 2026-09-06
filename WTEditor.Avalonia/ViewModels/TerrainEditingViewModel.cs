using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WTEditor.Avalonia.ViewModels;

public enum TerrainToolMode
{
    Sculpt,
    Smooth,
    Flatten
}

/// <summary>
/// Settings for the terrain editing tool. These values are forwarded to the
/// renderer as a stable brush contract for projection and stroke application.
/// </summary>
public partial class TerrainEditingViewModel : ViewModelBase
{
    [ObservableProperty]
    private TerrainToolMode _toolMode = TerrainToolMode.Sculpt;

    [ObservableProperty]
    private double _brushSize = 10;

    public double BrushSizeMinimum => 1d;
    public double BrushSizeMaximum => 1000d;
    public double BrushSizeCurve => 3.5d;

    [ObservableProperty]
    private double _speed = 5;

    public double SpeedMinimum => 0.1d;
    public double SpeedMaximum => 50d;
    public double SpeedCurve => 2.5d;

    [ObservableProperty]
    private double _innerRadius = 0.35;

    [ObservableProperty]
    private double _flattenHeight;

    [ObservableProperty]
    private int _smoothIterations = 1;

    [ObservableProperty]
    private bool _isPanelVisible = true;

    public bool IsSculptSelected => ToolMode == TerrainToolMode.Sculpt;
    public bool IsSmoothSelected => ToolMode == TerrainToolMode.Smooth;
    public bool IsFlattenSelected => ToolMode == TerrainToolMode.Flatten;
    public string ToolDescription => ToolMode switch
    {
        TerrainToolMode.Sculpt => "Sculpt terrain with the brush. Shift raises; Control lowers.",
        TerrainToolMode.Smooth => "Relax sharp height changes inside the brush.",
        TerrainToolMode.Flatten => "Move terrain toward a fixed target height.",
        _ => string.Empty
    };

    public string SpeedLabel => "Speed";

    [RelayCommand]
    private void TogglePanel() => IsPanelVisible = !IsPanelVisible;

    [RelayCommand]
    private void SelectTool(string? tool)
    {
        if (Enum.TryParse<TerrainToolMode>(tool, ignoreCase: true, out var parsed))
            ToolMode = parsed;
    }

    partial void OnToolModeChanged(TerrainToolMode value)
    {
        OnPropertyChanged(nameof(IsSculptSelected));
        OnPropertyChanged(nameof(IsSmoothSelected));
        OnPropertyChanged(nameof(IsFlattenSelected));
        OnPropertyChanged(nameof(ToolDescription));
    }

    partial void OnBrushSizeChanged(double value) => BrushSize = Math.Clamp(value, BrushSizeMinimum, BrushSizeMaximum);
    partial void OnSpeedChanged(double value) => Speed = Math.Clamp(value, SpeedMinimum, SpeedMaximum);
    partial void OnInnerRadiusChanged(double value) => InnerRadius = Math.Clamp(value, 0, 1);
    partial void OnSmoothIterationsChanged(int value) => SmoothIterations = Math.Clamp(value, 1, 8);
}
