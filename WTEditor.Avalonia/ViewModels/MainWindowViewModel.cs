using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISettingsDialogService _settingsDialogService;
    private readonly IProjectSelectionDialogService _projectSelectionDialogService;
    private readonly IProjectService _projectService;
    public UndoService UndoService { get; }

    public string Title => _projectService.CurrentProject is { } project
        ? $"WoW.Tools Editor - {project.Name}"
        : "WoW.Tools Editor";
    public EditorSession Session { get; }
    public ProjectDefinition? CurrentProject => _projectService.CurrentProject;

    [ObservableProperty]
    private ViewModelBase _currentView;

    public MainWindowViewModel(
        MainViewModel mainView,
        EditorSession session,
        ISettingsDialogService settingsDialogService,
        IProjectSelectionDialogService projectSelectionDialogService,
        UndoService undoService,
        IProjectService projectService)
    {
        _currentView = mainView;
        Session = session;
        _settingsDialogService = settingsDialogService;
        _projectSelectionDialogService = projectSelectionDialogService;
        _projectService = projectService;
        UndoService = undoService;
        UndoService.HistoryChanged += OnHistoryChanged;
        _projectService.CurrentProjectChanged += OnCurrentProjectChanged;
    }

    private void OnCurrentProjectChanged(object? sender, ProjectDefinition? project)
    {
        if (project != null)
            Session.Reload(_projectService.CurrentSettings);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CurrentProject));
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
        var updated = await _settingsDialogService.ShowAsync(Session.Current);
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
