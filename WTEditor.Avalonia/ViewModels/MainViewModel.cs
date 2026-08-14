using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WTEditor.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    public Editor3DViewModel ViewportVM { get; }
    public SelectionInspectorViewModel Inspector { get; }
    public IReadOnlyList<EditorModeViewModel> Modes { get; }

    [ObservableProperty]
    private EditorModeViewModel _activeMode;

    public MainViewModel(
        Editor3DViewModel viewportViewModel,
        SelectionInspectorViewModel inspector)
    {
        ViewportVM = viewportViewModel;
        Inspector = inspector;
        Modes =
        [
            new EditorModeViewModel(
                "selection",
                "Select",
                "Select and inspect objects in the world viewport",
                "1")
        ];
        _activeMode = Modes[0];
        _activeMode.IsActive = true;
        ViewportVM.PropertyChanged += OnViewportPropertyChanged;
        Inspector.Inspect(ViewportVM.SelectedObject);
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
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Editor3DViewModel.SelectedObject))
            Inspector.Inspect(ViewportVM.SelectedObject);
    }

    public void Dispose() => ViewportVM.PropertyChanged -= OnViewportPropertyChanged;
}
