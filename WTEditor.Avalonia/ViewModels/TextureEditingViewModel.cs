using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Avalonia.Presentation;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.ViewModels;

/// <summary>Texture-specific operation state composed with shared brush settings.</summary>
public partial class TextureEditingViewModel : BrushToolViewModelBase
{
    private static readonly IReadOnlyDictionary<TextureBrushMode, ToolSubModeViewModel> ModeDefinitions =
        new Dictionary<TextureBrushMode, ToolSubModeViewModel>
        {
            [TextureBrushMode.Paint] = new("paint", "Paint", "Paint the selected terrain texture inside the brush.", EditorIcons.Paint, true),
            [TextureBrushMode.Colour] = new("colour", "Colour", "Tint terrain colour inside the brush.", EditorIcons.Colour, true)
        };

    public TextureEditingViewModel()
        : base(ModeDefinitions.Values, BuiltInBrushPresets.All)
    {
    }

    public TextureBrushMode ToolMode => ModeDefinitions.First(pair => pair.Value == SelectedSubMode).Key;
    public double OpacityMinimum => 0d;
    public double OpacityMaximum => 255d;

    [ObservableProperty]
    private double _strength = 1d;

    [ObservableProperty]
    private double _opacity = 255d;

    protected override void OnSubModeChanged()
    {
        OnPropertyChanged(nameof(ToolMode));
    }

    partial void OnOpacityChanged(double value) => Opacity = Math.Clamp(value, OpacityMinimum, OpacityMaximum);
    partial void OnStrengthChanged(double value) => Strength = Math.Clamp(value, 0d, 1d);
}
