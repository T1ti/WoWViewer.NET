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
    private enum PanelDock
    {
        OverlayLeft,
        OverlayRight,
        Floating,
        BrowserLeft,
        BrowserRight,
        BrowserBottom
    }

    private readonly Dictionary<Border, PanelDock> _panelDocks = [];
    private Border? _draggingPanel;
    private Point _dragOffset;
    private ViewModels.MainViewModel? _viewModel;
    private Point _lastPointerPosition;
    private Point _panelDragPointer;
    private double _browserFloatingWidth = 500d;
    private double _browserFloatingHeight = 500d;
    private double _browserDockWidthFraction = 0.3d;
    private double _browserDockHeightFraction = 0.35d;

    public MainView()
    {
        InitializeComponent();
        _panelDocks[InspectorPanel] = PanelDock.OverlayRight;
        _panelDocks[ToolsPanel] = PanelDock.OverlayRight;
        _panelDocks[TextureBrowserPanel] = PanelDock.Floating;
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
        _panelDragPointer = pointer;
        if (ReferenceEquals(panel, TextureBrowserPanel) && IsBrowserWorkspaceDock(_panelDocks[panel]))
        {
            var localPointer = e.GetPosition(panel);
            panel.Width = _browserFloatingWidth;
            panel.Height = _browserFloatingHeight;
            panel.CornerRadius = new CornerRadius(6);
            _panelDocks[panel] = PanelDock.Floating;
            SetFloatingPosition(
                panel,
                pointer.X - Math.Min(localPointer.X, panel.Width - 20),
                pointer.Y - Math.Min(localPointer.Y, panel.Height - 20));
            ApplyBrowserViewportMargin();
        }
        var left = GetCanvasCoordinate(Canvas.GetLeft(panel));
        var top = GetCanvasCoordinate(Canvas.GetTop(panel));
        _dragOffset = new Point(pointer.X - left, pointer.Y - top);
        _draggingPanel = panel;
        _panelDocks[panel] = PanelDock.Floating;
        TextureBrowserDockPreview.IsVisible = false;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void PanelDragHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingPanel == null)
            return;

        var pointer = e.GetPosition(PanelCanvas);
        _panelDragPointer = pointer;
        SetFloatingPosition(_draggingPanel, pointer.X - _dragOffset.X, pointer.Y - _dragOffset.Y);
        if (ReferenceEquals(_draggingPanel, TextureBrowserPanel))
            UpdateTextureBrowserDockPreview(pointer);
        e.Handled = true;
    }

    private void PanelDragHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingPanel == null)
            return;

        var panel = _draggingPanel;
        _draggingPanel = null;
        _panelDragPointer = e.GetPosition(PanelCanvas);
        TextureBrowserDockPreview.IsVisible = false;
        e.Pointer.Capture(null);
        var left = GetCanvasCoordinate(Canvas.GetLeft(panel));
        if (ReferenceEquals(panel, TextureBrowserPanel))
        {
            _browserFloatingWidth = panel.Width;
            _browserFloatingHeight = panel.Height;
            var dockTarget = GetTextureBrowserDockTarget(_panelDragPointer);
            if (dockTarget == PanelDock.BrowserLeft)
            {
                _browserDockWidthFraction = GetWidthFraction(panel.Width);
                _panelDocks[panel] = PanelDock.BrowserLeft;
            }
            else if (dockTarget == PanelDock.BrowserRight)
            {
                _browserDockWidthFraction = GetWidthFraction(panel.Width);
                _panelDocks[panel] = PanelDock.BrowserRight;
            }
            else if (dockTarget == PanelDock.BrowserBottom)
            {
                _browserDockHeightFraction = GetHeightFraction(panel.Height);
                _panelDocks[panel] = PanelDock.BrowserBottom;
            }
        }
        else if (left <= 36d)
            _panelDocks[panel] = PanelDock.OverlayLeft;
        else if (PanelCanvas.Bounds.Width - left - panel.Width <= 36d)
            _panelDocks[panel] = PanelDock.OverlayRight;
        ArrangePanels();
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
        if (ReferenceEquals(panel, TextureBrowserPanel) &&
            _panelDocks[panel] is PanelDock.BrowserLeft or PanelDock.BrowserRight)
            _browserDockWidthFraction = GetWidthFraction(panel.Width);
        else if (ReferenceEquals(panel, TextureBrowserPanel))
            _browserFloatingWidth = panel.Width;
        if (resizingLeftEdge)
            Canvas.SetLeft(panel, GetCanvasCoordinate(Canvas.GetLeft(panel)) + oldWidth - panel.Width);
        if (ReferenceEquals(panel, TextureBrowserPanel) && IsBrowserWorkspaceDock(_panelDocks[panel]))
            ArrangePanels();
        else
            ArrangePanel(panel);
    }

    private void PanelCornerResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        if (_panelDocks.GetValueOrDefault(TextureBrowserPanel) != PanelDock.Floating)
            return;

        var resizeFromLeft = sender is Control handle &&
                             handle.Name?.Contains("Left", StringComparison.Ordinal) == true;
        var oldWidth = TextureBrowserPanel.Width;
        TextureBrowserPanel.Width = Math.Clamp(
            oldWidth + (resizeFromLeft ? -e.Vector.X : e.Vector.X),
            260d,
            Math.Max(260d, PanelCanvas.Bounds.Width - 20d));
        if (resizeFromLeft)
        {
            Canvas.SetLeft(
                TextureBrowserPanel,
                GetCanvasCoordinate(Canvas.GetLeft(TextureBrowserPanel)) + oldWidth - TextureBrowserPanel.Width);
        }

        if (sender is Control corner && corner.Name?.Contains("Top", StringComparison.Ordinal) == true)
            ResizeFloatingPanelTop(TextureBrowserPanel, e.Vector.Y);
        else
            ResizePanelBottom(TextureBrowserPanel, e.Vector.Y);
        _browserFloatingWidth = TextureBrowserPanel.Width;
        _browserFloatingHeight = TextureBrowserPanel.Height;
        ArrangePanel(TextureBrowserPanel);
    }

    private void PanelTopResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var dock = _panelDocks.GetValueOrDefault(TextureBrowserPanel);
        if (dock == PanelDock.BrowserBottom)
        {
            var height = Math.Clamp(
                TextureBrowserPanel.Height - e.Vector.Y,
                180d,
                Math.Max(180d, PanelCanvas.Bounds.Height - 80d));
            _browserDockHeightFraction = GetHeightFraction(height);
        }
        else if (dock == PanelDock.Floating)
        {
            ResizeFloatingPanelTop(TextureBrowserPanel, e.Vector.Y);
            _browserFloatingHeight = TextureBrowserPanel.Height;
        }
        else
        {
            return;
        }

        if (dock == PanelDock.BrowserBottom)
            ArrangePanels();
        else
            ArrangePanel(TextureBrowserPanel);
    }

    private void PanelBottomResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var panel = sender is Control control ? GetPanel(control) : null;
        if (panel == null)
            return;

        ResizePanelBottom(panel, e.Vector.Y);
        if (ReferenceEquals(panel, TextureBrowserPanel))
            _browserFloatingHeight = panel.Height;
        ArrangePanel(panel);
    }

    private void ResizePanelBottom(Border panel, double delta)
    {
        var minimumHeight = GetMinimumPanelHeight(panel);
        var currentHeight = double.IsNaN(panel.Height)
            ? Math.Max(panel.Bounds.Height, panel.DesiredSize.Height)
            : panel.Height;
        panel.Height = Math.Clamp(
            currentHeight + delta,
            minimumHeight,
            Math.Max(minimumHeight, PanelCanvas.Bounds.Height - GetCanvasCoordinate(Canvas.GetTop(panel))));
    }

    private void ResizeFloatingPanelTop(Border panel, double delta)
    {
        var oldTop = GetCanvasCoordinate(Canvas.GetTop(panel));
        var oldHeight = double.IsNaN(panel.Height)
            ? Math.Max(panel.Bounds.Height, panel.DesiredSize.Height)
            : panel.Height;
        var newHeight = Math.Clamp(
            oldHeight - delta,
            GetMinimumPanelHeight(panel),
            oldHeight + oldTop);
        panel.Height = newHeight;
        Canvas.SetTop(panel, oldTop + oldHeight - newHeight);
    }

    private void ArrangePanels()
    {
        if (PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0)
            return;

        ArrangePanel(TextureBrowserPanel);
        ApplyBrowserViewportMargin();
        ArrangePanel(InspectorPanel);
        ArrangePanel(ToolsPanel);
        ApplyViewportChromeMargin();
    }

    private void ArrangePanel(Border panel)
    {
        if (PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0 || !panel.IsVisible)
            return;

        var dock = _panelDocks.GetValueOrDefault(panel, PanelDock.Floating);
        if (ReferenceEquals(panel, TextureBrowserPanel) && IsBrowserWorkspaceDock(dock))
        {
            ArrangeDockedTextureBrowser(dock);
            return;
        }

        if (ReferenceEquals(panel, TextureBrowserPanel))
        {
            var expanded = _viewModel?.TextureEditor.IsBrowserExpanded != false;
            TextureBrowserLeftResizeHandle.IsVisible = expanded;
            TextureBrowserRightResizeHandle.IsVisible = expanded;
            TextureBrowserTopResizeHandle.IsVisible = expanded;
            TextureBrowserBottomResizeHandle.IsVisible = expanded;
            TextureBrowserBottomLeftResizeHandle.IsVisible = expanded;
            TextureBrowserBottomRightResizeHandle.IsVisible = expanded;
            TextureBrowserTopLeftResizeHandle.IsVisible = expanded;
            TextureBrowserTopRightResizeHandle.IsVisible = expanded;
        }
        panel.CornerRadius = new CornerRadius(6);
        panel.Width = Math.Min(panel.Width, Math.Max(260, PanelCanvas.Bounds.Width));
        var viewportBounds = GetViewportBounds();
        var minimumHeight = GetMinimumPanelHeight(panel);
        panel.MaxHeight = Math.Max(minimumHeight, viewportBounds.Height - 20);
        if (!double.IsNaN(panel.Height))
        {
            panel.Height = Math.Clamp(
                panel.Height,
                minimumHeight,
                panel.MaxHeight);
        }
        if (dock == PanelDock.OverlayLeft)
        {
            Canvas.SetLeft(panel, viewportBounds.Left + 10);
            Canvas.SetTop(panel, viewportBounds.Top + 10);
        }
        else if (dock == PanelDock.OverlayRight)
        {
            Canvas.SetLeft(panel, Math.Max(viewportBounds.Left, viewportBounds.Right - panel.Width - 10));
            Canvas.SetTop(panel, viewportBounds.Top + 10);
        }
        else
        {
            SetFloatingPosition(panel, GetCanvasCoordinate(Canvas.GetLeft(panel)), GetCanvasCoordinate(Canvas.GetTop(panel)));
        }

        if (ReferenceEquals(panel, TextureBrowserPanel))
            SyncTextureBrowserResizeLayer();
    }

    private void ArrangeDockedTextureBrowser(PanelDock dock)
    {
        if (_viewModel?.TextureEditor.IsBrowserExpanded == false)
            _viewModel.TextureEditor.IsBrowserExpanded = true;

        TextureBrowserPanel.CornerRadius = new CornerRadius(0);
        if (dock is PanelDock.BrowserLeft or PanelDock.BrowserRight)
        {
            TextureBrowserPanel.Width = Math.Clamp(
                PanelCanvas.Bounds.Width * _browserDockWidthFraction,
                260d,
                Math.Max(260d, PanelCanvas.Bounds.Width - 200d));
            TextureBrowserPanel.Height = PanelCanvas.Bounds.Height;
            Canvas.SetLeft(
                TextureBrowserPanel,
                dock == PanelDock.BrowserLeft
                    ? 0d
                    : Math.Max(0d, PanelCanvas.Bounds.Width - TextureBrowserPanel.Width));
            Canvas.SetTop(TextureBrowserPanel, 0d);
        }
        else
        {
            TextureBrowserPanel.Width = PanelCanvas.Bounds.Width;
            TextureBrowserPanel.Height = Math.Clamp(
                PanelCanvas.Bounds.Height * _browserDockHeightFraction,
                180d,
                Math.Max(180d, PanelCanvas.Bounds.Height - 80d));
            Canvas.SetLeft(TextureBrowserPanel, 0d);
            Canvas.SetTop(TextureBrowserPanel, PanelCanvas.Bounds.Height - TextureBrowserPanel.Height);
        }

        TextureBrowserLeftResizeHandle.IsVisible = dock == PanelDock.BrowserRight;
        TextureBrowserRightResizeHandle.IsVisible = dock == PanelDock.BrowserLeft;
        TextureBrowserTopResizeHandle.IsVisible = dock == PanelDock.BrowserBottom;
        TextureBrowserBottomResizeHandle.IsVisible = false;
        TextureBrowserBottomLeftResizeHandle.IsVisible = false;
        TextureBrowserBottomRightResizeHandle.IsVisible = false;
        TextureBrowserTopLeftResizeHandle.IsVisible = false;
        TextureBrowserTopRightResizeHandle.IsVisible = false;
        SyncTextureBrowserResizeLayer();
        ApplyBrowserViewportMargin();
        ApplyViewportChromeMargin();
    }

    private void ApplyBrowserViewportMargin()
    {
        if (!TextureBrowserPanel.IsVisible)
        {
            EditorViewport.Margin = default;
            return;
        }

        EditorViewport.Margin = _panelDocks.GetValueOrDefault(TextureBrowserPanel) switch
        {
            PanelDock.BrowserLeft => new Thickness(TextureBrowserPanel.Width + 5, 0, 0, 0),
            PanelDock.BrowserRight => new Thickness(0, 0, TextureBrowserPanel.Width + 5, 0),
            PanelDock.BrowserBottom => new Thickness(0, 0, 0, TextureBrowserPanel.Height + 5),
            _ => default
        };
    }

    private Rect GetViewportBounds()
    {
        if (!TextureBrowserPanel.IsVisible)
            return new Rect(0, 0, PanelCanvas.Bounds.Width, PanelCanvas.Bounds.Height);

        var separator = 5d;
        return _panelDocks.GetValueOrDefault(TextureBrowserPanel) switch
        {
            PanelDock.BrowserLeft => new Rect(
                TextureBrowserPanel.Width + separator,
                0,
                Math.Max(0, PanelCanvas.Bounds.Width - TextureBrowserPanel.Width - separator),
                PanelCanvas.Bounds.Height),
            PanelDock.BrowserRight => new Rect(
                0,
                0,
                Math.Max(0, PanelCanvas.Bounds.Width - TextureBrowserPanel.Width - separator),
                PanelCanvas.Bounds.Height),
            PanelDock.BrowserBottom => new Rect(
                0,
                0,
                PanelCanvas.Bounds.Width,
                Math.Max(0, PanelCanvas.Bounds.Height - TextureBrowserPanel.Height - separator)),
            _ => new Rect(0, 0, PanelCanvas.Bounds.Width, PanelCanvas.Bounds.Height)
        };
    }

    private void ApplyViewportChromeMargin()
    {
        var viewport = GetViewportBounds();
        var leftInset = viewport.Left;
        var rightInset = Math.Max(0d, PanelCanvas.Bounds.Width - viewport.Right);
        var bottomInset = Math.Max(0d, PanelCanvas.Bounds.Height - viewport.Bottom);

        ViewportModeToolbar.Margin = new Thickness(leftInset + 10d, 0d, 0d, bottomInset);
        InspectorToggleButton.Margin = new Thickness(0d, 10d, rightInset + 10d, 0d);
        TerrainToolsToggleButton.Margin = new Thickness(0d, 10d, rightInset + 100d, 0d);
        TextureToolsToggleButton.Margin = new Thickness(0d, 10d, rightInset + 100d, 0d);
        TextureBrowserShowButton.Margin = new Thickness(leftInset + 56d, 10d, 0d, 0d);
    }

    private PanelDock? GetTextureBrowserDockTarget(Point pointer)
    {
        const double snapDistance = 48d;
        if (pointer.X <= snapDistance)
            return PanelDock.BrowserLeft;
        if (pointer.X >= PanelCanvas.Bounds.Width - snapDistance)
            return PanelDock.BrowserRight;
        if (pointer.Y >= PanelCanvas.Bounds.Height - snapDistance)
            return PanelDock.BrowserBottom;
        return null;
    }

    private void UpdateTextureBrowserDockPreview(Point pointer)
    {
        var dock = GetTextureBrowserDockTarget(pointer);
        if (dock == null)
        {
            TextureBrowserDockPreview.IsVisible = false;
            return;
        }

        if (dock is PanelDock.BrowserLeft or PanelDock.BrowserRight)
        {
            TextureBrowserDockPreview.Width = Math.Clamp(
                PanelCanvas.Bounds.Width * GetWidthFraction(TextureBrowserPanel.Width),
                260d,
                Math.Max(260d, PanelCanvas.Bounds.Width - 200d));
            TextureBrowserDockPreview.Height = PanelCanvas.Bounds.Height;
            Canvas.SetLeft(
                TextureBrowserDockPreview,
                dock == PanelDock.BrowserLeft
                    ? 0d
                    : PanelCanvas.Bounds.Width - TextureBrowserDockPreview.Width);
            Canvas.SetTop(TextureBrowserDockPreview, 0d);
        }
        else
        {
            TextureBrowserDockPreview.Width = PanelCanvas.Bounds.Width;
            TextureBrowserDockPreview.Height = Math.Clamp(
                PanelCanvas.Bounds.Height * GetHeightFraction(TextureBrowserPanel.Height),
                180d,
                Math.Max(180d, PanelCanvas.Bounds.Height - 80d));
            Canvas.SetLeft(TextureBrowserDockPreview, 0d);
            Canvas.SetTop(
                TextureBrowserDockPreview,
                PanelCanvas.Bounds.Height - TextureBrowserDockPreview.Height);
        }

        TextureBrowserDockPreview.IsVisible = true;
    }

    private double GetWidthFraction(double width) =>
        Math.Clamp(width / Math.Max(1d, PanelCanvas.Bounds.Width), 0.15d, 0.8d);

    private double GetHeightFraction(double height) =>
        Math.Clamp(height / Math.Max(1d, PanelCanvas.Bounds.Height), 0.15d, 0.8d);

    private void SyncTextureBrowserResizeLayer()
    {
        TextureBrowserResizeLayer.Width = TextureBrowserPanel.Width;
        TextureBrowserResizeLayer.Height = TextureBrowserPanel.Height;
        Canvas.SetLeft(TextureBrowserResizeLayer, GetCanvasCoordinate(Canvas.GetLeft(TextureBrowserPanel)));
        Canvas.SetTop(TextureBrowserResizeLayer, GetCanvasCoordinate(Canvas.GetTop(TextureBrowserPanel)));
    }

    private static bool IsBrowserWorkspaceDock(PanelDock dock) =>
        dock is PanelDock.BrowserLeft or PanelDock.BrowserRight or PanelDock.BrowserBottom;

    private static double GetCanvasCoordinate(double coordinate) => double.IsNaN(coordinate) ? 0 : coordinate;

    private double GetMinimumPanelHeight(Border panel) =>
        ReferenceEquals(panel, TextureBrowserPanel) &&
        _viewModel?.TextureEditor.IsBrowserExpanded == false
            ? 44d
            : Math.Max(260d, panel.MinHeight);

    private void SetFloatingPosition(Border panel, double left, double top)
    {
        var panelHeight = double.IsNaN(panel.Height)
            ? Math.Max(panel.Bounds.Height, panel.DesiredSize.Height)
            : panel.Height;
        if (ReferenceEquals(panel, TextureBrowserPanel))
        {
            const double retainedHeaderWidth = 80d;
            const double retainedHeaderHeight = 44d;
            Canvas.SetLeft(panel, Math.Clamp(
                left,
                -panel.Width + retainedHeaderWidth,
                PanelCanvas.Bounds.Width - retainedHeaderWidth));
            Canvas.SetTop(panel, Math.Clamp(
                top,
                0d,
                PanelCanvas.Bounds.Height - retainedHeaderHeight));
            SyncTextureBrowserResizeLayer();
            return;
        }

        Canvas.SetLeft(panel, Math.Clamp(left, 0, Math.Max(0, PanelCanvas.Bounds.Width - panel.Width)));
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
