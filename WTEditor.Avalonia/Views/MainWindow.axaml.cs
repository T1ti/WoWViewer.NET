using Avalonia;
using Avalonia.Controls;
using WTEditor.Application.Models;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        ApplySavedPlacement(viewModel.Session.Current.Window);
        Closing += OnClosing;
    }

    private void ApplySavedPlacement(WindowPlacement placement)
    {
        if (placement.HasBounds && placement.Width > 0 && placement.Height > 0)
        {
            Width = placement.Width;
            Height = placement.Height;
            Position = new PixelPoint(placement.X, placement.Y);
        }

        if (Enum.TryParse<WindowState>(placement.State, true, out var savedWindowState))
            WindowState = savedWindowState;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var previous = viewModel.Session.Current.Window;
        var placement = WindowState == WindowState.Normal && Width > 0 && Height > 0
            ? new WindowPlacement
            {
                HasBounds = true,
                State = WindowState.ToString(),
                X = Position.X,
                Y = Position.Y,
                Width = Width,
                Height = Height
            }
            : previous with { State = WindowState.ToString() };

        viewModel.Session.UpdateWindow(placement, save: true);
    }
}
