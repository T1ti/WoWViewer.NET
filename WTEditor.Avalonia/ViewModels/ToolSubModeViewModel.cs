using Avalonia.Media;

namespace WTEditor.Avalonia.ViewModels;

/// <summary>Shared presentation contract for a tool's selectable submodes.</summary>
public sealed record ToolSubModeViewModel(
    string Id,
    string DisplayName,
    string Description,
    Geometry Icon,
    bool UsesFalloff)
{
    public string Tooltip => $"{DisplayName}\n{Description}";
}
