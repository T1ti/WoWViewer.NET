namespace WTEditor.Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public string Greeting { get; } = "Welcome to Avalonia!";

        public string Title { get; } = "WoW.Tools Editor";

        public ViewModelBase CurrentView { get; set; }
        // public MainViewModel MainView { get; }

        public MainWindowViewModel(MainViewModel mainView)
        {
            CurrentView = mainView;
        }
    }
}
