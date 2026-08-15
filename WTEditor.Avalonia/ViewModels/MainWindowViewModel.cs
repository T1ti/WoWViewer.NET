using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISettingsDialogService _settingsDialogService;
    public UndoService UndoService { get; }

    public string Title { get; } = "WoW.Tools Editor";
    public EditorSession Session { get; }

    [ObservableProperty]
    private ViewModelBase _currentView;

    public MainWindowViewModel(
        MainViewModel mainView,
        EditorSession session,
        ISettingsDialogService settingsDialogService,
        UndoService undoService)
    {
        _currentView = mainView;
        Session = session;
        _settingsDialogService = settingsDialogService;
        UndoService = undoService;
        UndoService.HistoryChanged += OnHistoryChanged;
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
}
