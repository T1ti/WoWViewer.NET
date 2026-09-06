using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Avalonia.Presentation;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.ViewModels;

/// <summary>Capabilities exposed by an editor interaction mode.</summary>
[Flags]
public enum EditorModeCapabilities
{
    None = 0,
    Selection = 1,
    TerrainEditing = 2
}

/// <summary>
/// Immutable mode metadata. Keeping identity, presentation, and capabilities
/// together prevents the shell and input bridge from duplicating mode strings.
/// </summary>
public sealed record EditorModeDefinition(
    string Id,
    string DisplayName,
    string Description,
    string Shortcut,
    Geometry Icon,
    EditorModeCapabilities Capabilities,
    EditorModeId RendererMode);

public static class EditorModeDefinitions
{
    public const string SelectionId = "selection";
    public const string TerrainId = "terrain";

    public static EditorModeDefinition Selection { get; } = new(
        SelectionId,
        "Select",
        "Select and inspect objects in the world viewport",
        "1",
        EditorIcons.Select,
        EditorModeCapabilities.Selection,
        EditorModeId.Selection);

    public static EditorModeDefinition Terrain { get; } = new(
        TerrainId,
        "Terrain",
        "Sculpt, smooth, and flatten terrain in the world viewport",
        "2",
        EditorIcons.Terrain,
        EditorModeCapabilities.TerrainEditing,
        EditorModeId.Terrain);
}

/// <summary>
/// View-model wrapper for a registered editor mode. Additional modes only need
/// a definition and their own settings/panel view model.
/// </summary>
public partial class EditorModeViewModel(EditorModeDefinition definition) : ViewModelBase
{
    public EditorModeDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string Description => Definition.Description;
    public string Shortcut => Definition.Shortcut;
    public Geometry Icon => Definition.Icon;
    public EditorModeCapabilities Capabilities => Definition.Capabilities;

    [ObservableProperty]
    private bool _isActive;
}
