using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.ViewModels;

/// <summary>
/// Shared state and panel lifecycle for brush-driven tools. Derived view models
/// retain only domain-specific operation mapping and settings.
/// </summary>
public abstract partial class BrushToolViewModelBase : ViewModelBase
{
    protected BrushToolViewModelBase(
        IEnumerable<ToolSubModeViewModel> subModes,
        params BuiltInBrushPreset[] supportedBrushes)
    {
        SubModes = subModes.ToArray();
        if (SubModes.Count == 0)
            throw new ArgumentException("A brush tool must define at least one submode.", nameof(subModes));

        Brush = new BrushSettingsViewModel(supportedBrushes);
        _selectedSubMode = SubModes[0];
        Brush.SetToolUsesFalloff(_selectedSubMode.UsesFalloff);
    }

    public BrushSettingsViewModel Brush { get; }
    public IReadOnlyList<ToolSubModeViewModel> SubModes { get; }
    public string ToolDescription => SelectedSubMode.Description;

    [ObservableProperty]
    private ToolSubModeViewModel _selectedSubMode;

    [ObservableProperty]
    private bool _isPanelVisible = true;

    [RelayCommand]
    private void TogglePanel() => IsPanelVisible = !IsPanelVisible;

    partial void OnSelectedSubModeChanged(ToolSubModeViewModel value)
    {
        Brush.SetToolUsesFalloff(value.UsesFalloff);
        OnPropertyChanged(nameof(ToolDescription));
        OnSubModeChanged();
    }

    protected virtual void OnSubModeChanged()
    {
    }
}
