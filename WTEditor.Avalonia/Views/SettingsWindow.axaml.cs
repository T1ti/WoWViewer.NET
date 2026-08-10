using Avalonia.Controls;

namespace WTEditor.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void Apply_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(true);
    private void Cancel_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
}
