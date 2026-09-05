using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Rendering;

namespace WTEditor.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public const int WorldSelectionTabIndex = 0;
    public const int MainEditorTabIndex = 1;
    public const int DataToolsTabIndex = 2;

    private readonly ISettingsDialogService _settingsDialogService;
    private readonly IProjectSelectionDialogService _projectSelectionDialogService;
    private readonly IProjectService _projectService;
    public UndoService UndoService { get; }
    public MainViewModel MainView { get; }
    public WorldSelectionViewModel WorldSelection { get; }

    public string Title => _projectService.CurrentProject is { } project
        ? $"WoW.Tools Editor - {project.Name}"
        : "WoW.Tools Editor";
    public EditorSession Session { get; }
    public ProjectDefinition? CurrentProject => _projectService.CurrentProject;
    public bool IsWorldSelectionTabActive => SelectedTabIndex == WorldSelectionTabIndex;
    public bool IsMainEditorTabActive => SelectedTabIndex == MainEditorTabIndex;
    public bool IsDataToolsTabActive => SelectedTabIndex == DataToolsTabIndex;

    [ObservableProperty]
    private ViewModelBase _currentView;
    [ObservableProperty]
    private int _selectedTabIndex = MainEditorTabIndex;

    private bool _isWindowActive = true;
    private bool _isWindowMinimized;

    public MainWindowViewModel(
        MainViewModel mainView,
        EditorSession session,
        ISettingsDialogService settingsDialogService,
        IProjectSelectionDialogService projectSelectionDialogService,
        UndoService undoService,
        IProjectService projectService,
        WorldSelectionViewModel worldSelection)
    {
        MainView = mainView;
        WorldSelection = worldSelection;
        _currentView = mainView;
        Session = session;
        _settingsDialogService = settingsDialogService;
        _projectSelectionDialogService = projectSelectionDialogService;
        _projectService = projectService;
        UndoService = undoService;
        UndoService.HistoryChanged += OnHistoryChanged;
        _projectService.CurrentProjectChanged += OnCurrentProjectChanged;
        UpdateViewportRenderActivity();
    }

    private void OnCurrentProjectChanged(object? sender, ProjectDefinition? project)
    {
        if (project != null)
            Session.Reload(_projectService.CurrentSettings);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CurrentProject));
    }

    public void UpdateWindowActivity(bool isActive, bool isMinimized)
    {
        _isWindowActive = isActive;
        _isWindowMinimized = isMinimized;
        UpdateViewportRenderActivity();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsWorldSelectionTabActive));
        OnPropertyChanged(nameof(IsMainEditorTabActive));
        OnPropertyChanged(nameof(IsDataToolsTabActive));
        UpdateViewportRenderActivity();

        if (value == WorldSelectionTabIndex)
            _ = WorldSelection.ActivateAsync();
    }

    private void UpdateViewportRenderActivity()
    {
        var isEditorTabActive = SelectedTabIndex == MainEditorTabIndex;
        MainView.IsEditorTabVisible = isEditorTabActive;
        MainView.ViewportRenderActivity = _isWindowMinimized || !isEditorTabActive
            ? ViewportRenderActivity.Suspended
            : _isWindowActive
                ? ViewportRenderActivity.Foreground
                : ViewportRenderActivity.Background;
    }

    public bool CanUndo => UndoService.CanUndo;
    public bool CanRedo => UndoService.CanRedo;
    public string UndoLabel => UndoService.UndoDescription is { } description
        ? $"Undo {description}"
        : "Undo";
    public string RedoLabel => UndoService.RedoDescription is { } description
        ? $"Redo {description}"
        : "Redo";

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoService.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoService.Redo();

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(RedoLabel));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var initialSettings = Session.Current;
        var updated = await _settingsDialogService.ShowAsync(
            initialSettings,
            preview => Session.Apply(preview));
        if (updated != null)
            Session.Apply(updated, save: true);
    }

    [RelayCommand]
    private async Task SelectProjectAsync()
    {
        Session.Save();
        await _projectSelectionDialogService.ShowAsync();
    }
}
