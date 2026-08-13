namespace WTEditor.Avalonia.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public Editor3DViewModel ViewportVM { get; }

    public MainViewModel(Editor3DViewModel viewportViewModel)
    {
        ViewportVM = viewportViewModel;
    }
}
