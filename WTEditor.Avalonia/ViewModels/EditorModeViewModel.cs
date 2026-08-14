using CommunityToolkit.Mvvm.ComponentModel;

namespace WTEditor.Avalonia.ViewModels;

/// <summary>
/// Describes an editor interaction mode. Additional modes can be registered by
/// MainViewModel without changing the editor shell.
/// </summary>
public partial class EditorModeViewModel(
    string id,
    string displayName,
    string description,
    string shortcut) : ViewModelBase
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public string Description { get; } = description;
    public string Shortcut { get; } = shortcut;

    [ObservableProperty]
    private bool _isActive;
}
