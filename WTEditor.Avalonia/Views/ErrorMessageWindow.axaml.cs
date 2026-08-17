using Avalonia.Controls;

namespace WTEditor.Avalonia.Views;

public partial class ErrorMessageWindow : Window
{
    public string Message { get; }

    public ErrorMessageWindow()
        : this("Error", "")
    {
    }

    public ErrorMessageWindow(string title, string message)
    {
        Message = message;
        InitializeComponent();
        Title = title;
        DataContext = this;
    }

    private void Ok_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(true);
}
