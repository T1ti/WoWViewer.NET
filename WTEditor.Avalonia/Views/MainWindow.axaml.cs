using System;
using Avalonia;
using Avalonia.Controls;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var persisted = EditorSettingsStore.Load();
            if (persisted.HasWindowBounds && persisted.WindowWidth > 0 && persisted.WindowHeight > 0)
            {
                Width = persisted.WindowWidth;
                Height = persisted.WindowHeight;
                Position = new PixelPoint(persisted.WindowX, persisted.WindowY);
            }

            if (Enum.TryParse<WindowState>(persisted.WindowState, true, out var savedWindowState))
                WindowState = savedWindowState;

            Closing += OnClosing;
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (DataContext is MainWindowViewModel windowViewModel &&
                windowViewModel.CurrentView is MainViewModel mainViewModel)
            {
                if (WindowState == WindowState.Normal && Width > 0 && Height > 0)
                    mainViewModel.SaveSettings(Position.X, Position.Y, Width, Height, WindowState.ToString());
                else
                    mainViewModel.SaveSettings(WindowState.ToString());
            }
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
