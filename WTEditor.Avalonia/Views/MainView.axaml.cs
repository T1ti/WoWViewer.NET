using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.ComponentModel;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Controls;

namespace WTEditor.Avalonia.Views;

public partial class MainView : UserControl
{
    private enum PanelDock { Left, Right, Floating }

    private readonly Dictionary<Border, PanelDock> _panelDocks = [];
    private Border? _draggingPanel;
    private Point _dragOffset;
    private ViewModels.MainViewModel? _viewModel;
    private Point _lastPointerPosition;

    public MainView()
    {
        InitializeComponent();
        _panelDocks[InspectorPanel] = PanelDock.Right;
        _panelDocks[ToolsPanel] = PanelDock.Right;
        _panelDocks[TextureBrowserPanel] = PanelDock.Left;
        DragDrop.AddDragOverHandler(WorkspaceRoot, WorkspaceRoot_OnDragOver);
        DragDrop.AddDropHandler(WorkspaceRoot, WorkspaceRoot_OnDrop);
        AttachedToVisualTree += (_, _) => TextureDragDrop.ActiveTextureChanged += OnDraggedTextureChanged;
        DetachedFromVisualTree += (_, _) => TextureDragDrop.ActiveTextureChanged -= OnDraggedTextureChanged;
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
            or nameof(ViewModels.MainViewModel.IsEditingToolsPanelVisible)
            or nameof(ViewModels.MainViewModel.IsTextureBrowserPanelVisible))
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

    private void WorkspaceRoot_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastPointerPosition = e.GetPosition(WorkspaceRoot);
        PositionTextureDragGhost(_lastPointerPosition);
    }

    private void WorkspaceRoot_OnDragOver(object? sender, DragEventArgs e)
    {
        if (TextureDragDrop.Read(e) == null)
            return;
        _lastPointerPosition = e.GetPosition(WorkspaceRoot);
        PositionTextureDragGhost(_lastPointerPosition);
    }

    private void WorkspaceRoot_OnDrop(object? sender, DragEventArgs e)
    {
        if (TextureDragDrop.Read(e) != null)
        {
            e.DragEffects = DragDropEffects.None;
            TextureDragGhost.IsVisible = false;
        }
    }

    private void OnDraggedTextureChanged(TexturePaletteItemViewModel? texture)
    {
        TextureDragGhost.IsVisible = texture != null;
        TextureDragGhostImage.Source = texture?.Thumbnail;
        TextureDragGhostName.Text = texture?.DisplayName ?? string.Empty;
        if (texture != null)
            PositionTextureDragGhost(_lastPointerPosition);
    }

    private void PositionTextureDragGhost(Point pointer)
    {
        if (!TextureDragGhost.IsVisible)
            return;
        Canvas.SetLeft(TextureDragGhost, Math.Min(pointer.X + 14, Math.Max(0, WorkspaceRoot.Bounds.Width - 80)));
        Canvas.SetTop(TextureDragGhost, Math.Min(pointer.Y + 14, Math.Max(0, WorkspaceRoot.Bounds.Height - 96)));
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
        _panelDocks[panel] = PanelDock.Floating;
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
            _panelDocks[panel] = PanelDock.Left;
        else if (PanelCanvas.Bounds.Width - left - panel.Width <= snapDistance)
            _panelDocks[panel] = PanelDock.Right;
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

        var minimumHeight = GetMinimumPanelHeight(panel);
        var currentHeight = double.IsNaN(panel.Height)
            ? Math.Max(panel.Bounds.Height, panel.DesiredSize.Height)
            : panel.Height;
        panel.Height = Math.Clamp(
            currentHeight + e.Vector.Y,
            minimumHeight,
            Math.Max(minimumHeight, PanelCanvas.Bounds.Height - GetCanvasCoordinate(Canvas.GetTop(panel))));
        ArrangePanel(panel);
    }

    private void ArrangePanels()
    {
        if (PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0)
            return;

        ArrangePanel(InspectorPanel);
        ArrangePanel(ToolsPanel);
        ArrangePanel(TextureBrowserPanel);
    }

    private void ArrangePanel(Border panel)
    {
        if (PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0 || !panel.IsVisible)
            return;

        panel.Width = Math.Min(panel.Width, Math.Max(260, PanelCanvas.Bounds.Width));
        var minimumHeight = GetMinimumPanelHeight(panel);
        panel.MaxHeight = Math.Max(minimumHeight, PanelCanvas.Bounds.Height - 20);
        if (!double.IsNaN(panel.Height))
        {
            panel.Height = Math.Clamp(
                panel.Height,
                minimumHeight,
                panel.MaxHeight);
        }
        var dock = _panelDocks.GetValueOrDefault(panel, PanelDock.Floating);
        if (dock == PanelDock.Left)
        {
            Canvas.SetLeft(panel, 10);
            Canvas.SetTop(panel, 10);
        }
        else if (dock == PanelDock.Right)
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

    private double GetMinimumPanelHeight(Border panel) =>
        ReferenceEquals(panel, TextureBrowserPanel) &&
        _viewModel?.TextureEditor.IsBrowserExpanded == false
            ? 44d
            : Math.Max(260d, panel.MinHeight);

    private void SetFloatingPosition(Border panel, double left, double top)
    {
        Canvas.SetLeft(panel, Math.Clamp(left, 0, Math.Max(0, PanelCanvas.Bounds.Width - panel.Width)));
        var panelHeight = double.IsNaN(panel.Height)
            ? Math.Max(panel.Bounds.Height, panel.DesiredSize.Height)
            : panel.Height;
        Canvas.SetTop(panel, Math.Clamp(top, 0, Math.Max(0, PanelCanvas.Bounds.Height - panelHeight)));
    }

    private Border? GetPanel(Control source)
    {
        if (source.Name?.Contains("Inspector", StringComparison.Ordinal) == true)
            return InspectorPanel;
        if (source.Name?.Contains("Tools", StringComparison.Ordinal) == true)
            return ToolsPanel;
        if (source.Name?.Contains("TextureBrowser", StringComparison.Ordinal) == true)
            return TextureBrowserPanel;
        return null;
    }
}
