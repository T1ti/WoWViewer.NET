using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly HashSet<Window> _trackedOwnedWindows = [];

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
        Opened += OnOpened;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        InitializeForegroundFrameRateLimit();
        PublishViewportRenderActivity();
    }

    private void OnActivated(object? sender, EventArgs e) => PublishViewportRenderActivity();

    private void OnDeactivated(object? sender, EventArgs e) =>
        QueueViewportRenderActivityUpdate();

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
            PublishViewportRenderActivity();
    }

    private void PublishViewportRenderActivity()
    {
        TrackOwnedWindows();
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.UpdateWindowActivity(
                IsActive || _trackedOwnedWindows.Any(window => window.IsActive),
                WindowState == WindowState.Minimized);
    }

    private void QueueViewportRenderActivityUpdate() =>
        Dispatcher.UIThread.Post(PublishViewportRenderActivity, DispatcherPriority.Background);

    private void TrackOwnedWindows()
    {
        foreach (var window in OwnedWindows)
        {
            if (!_trackedOwnedWindows.Add(window))
                continue;

            window.Activated += OnOwnedWindowActivityChanged;
            window.Deactivated += OnOwnedWindowActivityChanged;
            window.Closed += OnOwnedWindowClosed;
        }
    }

    private void OnOwnedWindowActivityChanged(object? sender, EventArgs e) =>
        QueueViewportRenderActivityUpdate();

    private void OnOwnedWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window && _trackedOwnedWindows.Remove(window))
        {
            window.Activated -= OnOwnedWindowActivityChanged;
            window.Deactivated -= OnOwnedWindowActivityChanged;
            window.Closed -= OnOwnedWindowClosed;
        }

        QueueViewportRenderActivityUpdate();
    }

    private void InitializeForegroundFrameRateLimit()
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var rendering = viewModel.Session.Current.Rendering;
        if (rendering.IsForegroundFrameRateLimitInitialized)
            return;

        viewModel.Session.UpdateRendering(rendering with
        {
            IsForegroundFrameRateLimitInitialized = true,
            ViewportFrameRateLimit = DisplayRefreshRateResolver.GetRefreshRate(this)
        }, save: true);
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
