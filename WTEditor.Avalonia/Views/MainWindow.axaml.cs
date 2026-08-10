using Avalonia.Controls;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Settings_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel windowViewModel ||
                windowViewModel.CurrentView is not MainViewModel mainViewModel)
                return;

            var settings = new SettingsWindow
            {
                    DataContext = new ClientSettingsViewModel(
                    mainViewModel.ClientConfig,
                    mainViewModel.ViewportVM.RendererSettings,
                    mainViewModel.KeyboardLayout)
            };

            if (await settings.ShowDialog<bool>(this) && settings.DataContext is ClientSettingsViewModel settingsViewModel)
                mainViewModel.ApplyEditorSettings(
                    settingsViewModel.ToConfig(),
                    settingsViewModel.ToRendererSettings(),
                    settingsViewModel.KeyboardLayout);
        }
    }
}
