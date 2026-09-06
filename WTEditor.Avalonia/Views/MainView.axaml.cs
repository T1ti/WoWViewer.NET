using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.ComponentModel;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views;

public partial class MainView : UserControl
{
    private enum PanelDock { Left, Right, Floating }

    private PanelDock _panelDock = PanelDock.Right;
    private Border? _draggingPanel;
    private Point _dragOffset;
    private ViewModels.MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangePanels();
        DataContextChanged += MainView_OnDataContextChanged;
        KeyDown += MainView_OnKeyDown;
    }

    private void MainView_OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= MainViewModel_OnPropertyChanged;

        _viewModel = DataContext as ViewModels.MainViewModel;
        if (_viewModel != null)
            _viewModel.PropertyChanged += MainViewModel_OnPropertyChanged;

        EditorViewport.RenderActivity = _viewModel?.ViewportRenderActivity
            ?? WTEditor.Avalonia.Rendering.ViewportRenderActivity.Foreground;
        Dispatcher.UIThread.Post(ArrangePanels);
    }

    private void MainViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.MainViewModel.IsSelectionPanelVisible)
            or nameof(ViewModels.MainViewModel.IsEditingToolsPanelVisible))
        {
            Dispatcher.UIThread.Post(ArrangePanels);
        }

        if (e.PropertyName == nameof(ViewModels.MainViewModel.ViewportRenderActivity))
            EditorViewport.RenderActivity = _viewModel?.ViewportRenderActivity
                ?? WTEditor.Avalonia.Rendering.ViewportRenderActivity.Foreground;
    }

    private void MainView_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel)
            return;

        var mode = e.Key switch
        {
            Key.D1 => viewModel.Modes.FirstOrDefault(mode => mode.Id == EditorModeDefinitions.SelectionId),
            Key.D2 => viewModel.Modes.FirstOrDefault(mode => mode.Id == EditorModeDefinitions.TerrainId),
            Key.D3 => viewModel.Modes.FirstOrDefault(mode => mode.Id == EditorModeDefinitions.TextureId),
            _ => null
        };
        if (mode == null)
            return;

        viewModel.ActiveMode = mode;
        e.Handled = true;
    }

    private void PanelDragHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        var panel = GetPanel(handle);
        if (panel == null)
            return;

        var pointer = e.GetPosition(PanelCanvas);
        var left = GetCanvasCoordinate(Canvas.GetLeft(panel));
        var top = GetCanvasCoordinate(Canvas.GetTop(panel));
        _dragOffset = new Point(pointer.X - left, pointer.Y - top);
        _draggingPanel = panel;
        _panelDock = PanelDock.Floating;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void PanelDragHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingPanel == null)
            return;

        var pointer = e.GetPosition(PanelCanvas);
        SetFloatingPosition(_draggingPanel, pointer.X - _dragOffset.X, pointer.Y - _dragOffset.Y);
        e.Handled = true;
    }

    private void PanelDragHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingPanel == null)
            return;

        var panel = _draggingPanel;
        _draggingPanel = null;
        e.Pointer.Capture(null);
        var left = GetCanvasCoordinate(Canvas.GetLeft(panel));
        const double snapDistance = 28;
        if (left <= snapDistance)
            _panelDock = PanelDock.Left;
        else if (PanelCanvas.Bounds.Width - left - panel.Width <= snapDistance)
            _panelDock = PanelDock.Right;
        ArrangePanel(panel);
        e.Handled = true;
    }

    private void PanelHorizontalResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var panel = sender is Control control ? GetPanel(control) : null;
        if (panel == null)
            return;

        var oldWidth = panel.Width;
        var resizingLeftEdge = sender is Control handle && handle.Name?.Contains("Left", StringComparison.Ordinal) == true;
        var widthDelta = resizingLeftEdge ? -e.Vector.X : e.Vector.X;
        panel.Width = Math.Clamp(oldWidth + widthDelta, 260, Math.Max(260, PanelCanvas.Bounds.Width - 20));
        if (resizingLeftEdge)
            Canvas.SetLeft(panel, GetCanvasCoordinate(Canvas.GetLeft(panel)) + oldWidth - panel.Width);
        ArrangePanel(panel);
    }

    private void PanelBottomResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var panel = sender is Control control ? GetPanel(control) : null;
        if (panel == null)
            return;

        panel.Height = Math.Clamp(
            panel.Height + e.Vector.Y,
            260,
            Math.Max(260, PanelCanvas.Bounds.Height - GetCanvasCoordinate(Canvas.GetTop(panel))));
        ArrangePanel(panel);
    }

    private void ArrangePanels()
    {
        if (PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0)
            return;

        ArrangePanel(InspectorPanel);
        ArrangePanel(ToolsPanel);
    }

    private void ArrangePanel(Border panel)
    {
        if (PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0 || !panel.IsVisible)
            return;

        panel.Width = Math.Min(panel.Width, Math.Max(260, PanelCanvas.Bounds.Width));
        panel.Height = Math.Clamp(panel.Height, 260, Math.Max(260, PanelCanvas.Bounds.Height - 20));
        if (_panelDock == PanelDock.Left)
        {
            Canvas.SetLeft(panel, 10);
            Canvas.SetTop(panel, 10);
        }
        else if (_panelDock == PanelDock.Right)
        {
            Canvas.SetLeft(panel, Math.Max(0, PanelCanvas.Bounds.Width - panel.Width - 10));
            Canvas.SetTop(panel, 10);
        }
        else
        {
            SetFloatingPosition(panel, GetCanvasCoordinate(Canvas.GetLeft(panel)), GetCanvasCoordinate(Canvas.GetTop(panel)));
        }
    }

    private static double GetCanvasCoordinate(double coordinate) => double.IsNaN(coordinate) ? 0 : coordinate;

    private void SetFloatingPosition(Border panel, double left, double top)
    {
        Canvas.SetLeft(panel, Math.Clamp(left, 0, Math.Max(0, PanelCanvas.Bounds.Width - panel.Width)));
        Canvas.SetTop(panel, Math.Clamp(top, 0, Math.Max(0, PanelCanvas.Bounds.Height - panel.Height)));
    }

    private Border? GetPanel(Control source)
    {
        if (source.Name?.Contains("Inspector", StringComparison.Ordinal) == true)
            return InspectorPanel;
        if (source.Name?.Contains("Tools", StringComparison.Ordinal) == true)
            return ToolsPanel;
        return null;
    }
}
