using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application.Services;
using WTEditor.Application.Models;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    public Editor3DViewModel ViewportVM { get; }
    public SelectionInspectorViewModel Inspector { get; }
    public TerrainEditingViewModel TerrainEditor { get; }
    public IReadOnlyList<EditorModeViewModel> Modes { get; }
    public UndoService UndoService { get; }

    public bool IsTerrainModeActive => ActiveMode.Capabilities.HasFlag(EditorModeCapabilities.TerrainEditing);
    public bool IsSelectionModeActive => ActiveMode.Capabilities.HasFlag(EditorModeCapabilities.Selection);
    public bool IsSelectionPanelVisible => IsSelectionModeActive && Inspector.IsPanelVisible;
    public bool IsTerrainToolsPanelVisible => IsTerrainModeActive && TerrainEditor.IsPanelVisible;

    [ObservableProperty]
    private EditorModeViewModel _activeMode;
    private ObjectTransform? _lastInspectorTransform;

    public MainViewModel(
        Editor3DViewModel viewportViewModel,
        SelectionInspectorViewModel inspector,
        TerrainEditingViewModel terrainEditor,
        UndoService undoService)
    {
        ViewportVM = viewportViewModel;
        Inspector = inspector;
        TerrainEditor = terrainEditor;
        UndoService = undoService;
        Modes =
        [
            new EditorModeViewModel(EditorModeDefinitions.Selection),
            new EditorModeViewModel(EditorModeDefinitions.Terrain)
        ];
        _activeMode = Modes[0];
        _activeMode.IsActive = true;
        ViewportVM.PropertyChanged += OnViewportPropertyChanged;
        ViewportVM.ClientConfigurationChanged += OnClientConfigurationChanged;
        Inspector.TransformChanged += OnInspectorTransformChanged;
        Inspector.WmoPlacementChanged += OnInspectorWmoPlacementChanged;
        TerrainEditor.PropertyChanged += OnTerrainEditorPropertyChanged;
        SyncTerrainSettings();
        ViewportVM.EditorMode = EditorModeId.Selection;
        Inspector.SetBuildProfile(ClientBuildProfile.From(ViewportVM.ClientConfiguration));
        Inspector.Inspect(ViewportVM.SelectedObject);
        _lastInspectorTransform = ViewportVM.SelectedObject?.Transform;
    }

    [RelayCommand]
    private void ActivateMode(EditorModeViewModel? mode)
    {
        if (mode == null || ReferenceEquals(mode, ActiveMode))
            return;

        ActiveMode.IsActive = false;
        ActiveMode = mode;
        ActiveMode.IsActive = true;
    }

    partial void OnActiveModeChanged(EditorModeViewModel value)
    {
        foreach (var mode in Modes)
            mode.IsActive = ReferenceEquals(mode, value);

        ViewportVM.EditorMode = value.Definition.RendererMode;
        OnPropertyChanged(nameof(IsTerrainModeActive));
        OnPropertyChanged(nameof(IsSelectionModeActive));
        OnPropertyChanged(nameof(IsSelectionPanelVisible));
        OnPropertyChanged(nameof(IsTerrainToolsPanelVisible));
    }

    private void OnTerrainEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncTerrainSettings();

        if (e.PropertyName == nameof(TerrainEditingViewModel.IsPanelVisible))
            OnPropertyChanged(nameof(IsTerrainToolsPanelVisible));
    }

    private void SyncTerrainSettings()
    {
        ViewportVM.TerrainBrushSize = TerrainEditor.BrushSize;
        ViewportVM.TerrainBrushInnerRadius = TerrainEditor.InnerRadius;
        ViewportVM.TerrainBrushToolMode = (int)TerrainEditor.ToolMode;
        ViewportVM.TerrainBrushSpeed = TerrainEditor.Speed;
        ViewportVM.TerrainFlattenHeight = TerrainEditor.FlattenHeight;
        ViewportVM.TerrainSmoothIterations = TerrainEditor.SmoothIterations;
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Editor3DViewModel.SelectedObject))
        {
            Inspector.Inspect(ViewportVM.SelectedObject);
            _lastInspectorTransform = ViewportVM.SelectedObject?.Transform;
        }
    }

    private void OnClientConfigurationChanged(object? sender, ClientConfiguration configuration) =>
        Inspector.SetBuildProfile(ClientBuildProfile.From(configuration));

    private void OnInspectorTransformChanged(object? sender, ObjectTransform transform) =>
        ExecuteObjectTransform(transform);

    private void ExecuteObjectTransform(ObjectTransform transform)
    {
        var selection = ViewportVM.SelectedObject;
        if (selection == null || selection.Transform == transform)
            return;

        var before = _lastInspectorTransform ?? selection.Transform;
        _lastInspectorTransform = transform;
        UndoService.Execute(new DelegateEditorCommand(
            "Transform object",
            () =>
            {
                _lastInspectorTransform = transform;
                ViewportVM.RequestSelectedObjectTransform(transform);
            },
            () =>
            {
                _lastInspectorTransform = before;
                ViewportVM.RequestSelectedObjectTransform(before);
            }));
    }

    private void OnInspectorWmoPlacementChanged(object? sender, WmoPlacementSelection selection) =>
        ViewportVM.RequestSelectedWmoPlacement(selection);

    public void Dispose()
    {
        ViewportVM.PropertyChanged -= OnViewportPropertyChanged;
        ViewportVM.ClientConfigurationChanged -= OnClientConfigurationChanged;
        Inspector.TransformChanged -= OnInspectorTransformChanged;
        Inspector.WmoPlacementChanged -= OnInspectorWmoPlacementChanged;
        TerrainEditor.PropertyChanged -= OnTerrainEditorPropertyChanged;
    }
}
