using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Avalonia.Presentation;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.ViewModels;

/// <summary>Terrain-specific operation state composed with shared brush settings.</summary>
public partial class TerrainEditingViewModel : BrushToolViewModelBase
{
    private static readonly IReadOnlyDictionary<TerrainBrushMode, ToolSubModeViewModel> ModeDefinitions =
        new Dictionary<TerrainBrushMode, ToolSubModeViewModel>
        {
            [TerrainBrushMode.Sculpt] = new("sculpt", "Sculpt", "Sculpt terrain with the brush. Shift raises; Control lowers.", EditorIcons.Sculpt, true),
            [TerrainBrushMode.Smooth] = new("smooth", "Smooth", "Relax sharp height changes inside the brush.", EditorIcons.Smooth, true),
            [TerrainBrushMode.Flatten] = new("flatten", "Flatten", "Move terrain toward a fixed target height.", EditorIcons.Flatten, true)
        };

    public TerrainEditingViewModel()
        : base(ModeDefinitions.Values, BuiltInBrushPresets.All)
    {
    }

    public TerrainBrushMode ToolMode => ModeDefinitions.First(pair => pair.Value == SelectedSubMode).Key;
    public bool IsSmoothSelected => ToolMode == TerrainBrushMode.Smooth;
    public bool IsFlattenSelected => ToolMode == TerrainBrushMode.Flatten;
    public double SpeedMinimum => 0.1d;
    public double SpeedMaximum => 50d;
    public double SpeedCurve => 2.5d;

    [ObservableProperty]
    private double _speed = 5d;

    [ObservableProperty]
    private double _flattenHeight;

    public IReadOnlyList<string> FlattenTargets { get; } = ["Fixed height", "Brush centre (stroke start)"];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedFlattenHeight))]
    private int _flattenTarget;
    public bool IsFixedFlattenHeight => FlattenTarget == 0;

    [ObservableProperty]
    private int _smoothIterations = 1;

    protected override void OnSubModeChanged()
    {
        OnPropertyChanged(nameof(ToolMode));
        OnPropertyChanged(nameof(IsSmoothSelected));
        OnPropertyChanged(nameof(IsFlattenSelected));
    }

    partial void OnSpeedChanged(double value) => Speed = Math.Clamp(value, SpeedMinimum, SpeedMaximum);
    partial void OnSmoothIterationsChanged(int value) => SmoothIterations = Math.Clamp(value, 1, 8);
}
