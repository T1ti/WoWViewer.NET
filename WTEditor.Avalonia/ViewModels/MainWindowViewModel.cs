using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISettingsDialogService _settingsDialogService;

    public string Title { get; } = "WoW.Tools Editor";
    public EditorSession Session { get; }

    [ObservableProperty]
    private ViewModelBase _currentView;

    public MainWindowViewModel(
        MainViewModel mainView,
        EditorSession session,
        ISettingsDialogService settingsDialogService)
    {
        _currentView = mainView;
        Session = session;
        _settingsDialogService = settingsDialogService;
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var updated = await _settingsDialogService.ShowAsync(Session.Current);
        if (updated != null)
            Session.Apply(updated, save: true);
    }
}
