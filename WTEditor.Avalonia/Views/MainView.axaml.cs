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
        WorkspaceLeft,
        WorkspaceRight,
        WorkspaceBottom
    }

    private sealed class PanelLayout(double floatingWidth, double floatingHeight)
    {
        public double FloatingWidth { get; set; } = floatingWidth;
        public double FloatingHeight { get; set; } = floatingHeight;
        public double DockWidthFraction { get; set; } = 0.3d;
        public double DockHeightFraction { get; set; } = 0.35d;
    }

    private readonly Dictionary<Border, PanelDock> _panelDocks = [];
    private readonly Dictionary<Border, PanelLayout> _panelLayouts = [];
    private Border? _draggingPanel;
    private bool _pendingObjectDrag;
    private Point _objectDragStart;
    private Point _dragOffset;
    private ViewModels.MainViewModel? _viewModel;
    private Point _lastPointerPosition;
    private Point _panelDragPointer;
    private double _brushToolsWidth = 292d;
    private ObjectToolsWindow? _objectToolsWindow;
    private PixelPoint? _objectFloatPosition;

    public MainView()
    {
        InitializeComponent();
        _panelDocks[InspectorPanel] = PanelDock.OverlayRight;
        _panelDocks[ToolsPanel] = PanelDock.OverlayRight;
        _panelDocks[ObjectToolsDockPanel] = PanelDock.Floating;
        _panelDocks[TextureBrowserPanel] = PanelDock.Floating;
        _panelLayouts[InspectorPanel] = new PanelLayout(330d, 620d);
        _panelLayouts[ToolsPanel] = new PanelLayout(292d, 500d);
        _panelLayouts[ObjectToolsDockPanel] = new PanelLayout(850d, 560d);
        _panelLayouts[TextureBrowserPanel] = new PanelLayout(500d, 500d);
        DragDrop.AddDragOverHandler(WorkspaceRoot, WorkspaceRoot_OnDragOver);
        DragDrop.AddDropHandler(WorkspaceRoot, WorkspaceRoot_OnDrop);
        AttachedToVisualTree += (_, _) =>
        {
            TextureDragDrop.ActiveTextureChanged += OnDraggedTextureChanged;
            Dispatcher.UIThread.Post(UpdateObjectToolsWindow);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            TextureDragDrop.ActiveTextureChanged -= OnDraggedTextureChanged;
            _pendingObjectDrag = false;
            CloseObjectToolsWindow();
        };
        SizeChanged += (_, _) => ArrangePanels();
        DataContextChanged += MainView_OnDataContextChanged;
        KeyDown += MainView_OnKeyDown;
    }

    private void MainView_OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= MainViewModel_OnPropertyChanged;

        CloseObjectToolsWindow();
        _pendingObjectDrag = false;

        _viewModel = DataContext as ViewModels.MainViewModel;
        if (_viewModel != null)
            _viewModel.PropertyChanged += MainViewModel_OnPropertyChanged;

        SyncToolsModeWidth();

        EditorViewport.RenderActivity = _viewModel?.ViewportRenderActivity
            ?? WTEditor.Avalonia.Rendering.ViewportRenderActivity.Foreground;
        Dispatcher.UIThread.Post(ArrangePanels);
        Dispatcher.UIThread.Post(UpdateObjectToolsWindow);
    }

    private void MainViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.MainViewModel.IsSelectionPanelVisible)
            or nameof(ViewModels.MainViewModel.IsEditingToolsPanelVisible)
            or nameof(ViewModels.MainViewModel.IsObjectToolsDockPanelVisible)
            or nameof(ViewModels.MainViewModel.IsObjectModeActive)
            or nameof(ViewModels.MainViewModel.IsObjectBrowserVisible)
            or nameof(ViewModels.MainViewModel.IsTextureBrowserPanelVisible))
        {
            Dispatcher.UIThread.Post(ArrangePanels);
        }

        if (e.PropertyName is nameof(ViewModels.MainViewModel.IsObjectToolsPanelVisible)
            or nameof(ViewModels.MainViewModel.IsObjectToolsDocked)
            or nameof(ViewModels.MainViewModel.IsObjectModeActive)
            or nameof(ViewModels.MainViewModel.IsEditorTabVisible))
            Dispatcher.UIThread.Post(UpdateObjectToolsWindow);

        if (e.PropertyName == nameof(ViewModels.MainViewModel.ViewportRenderActivity))
            EditorViewport.RenderActivity = _viewModel?.ViewportRenderActivity
                ?? WTEditor.Avalonia.Rendering.ViewportRenderActivity.Foreground;
    }

    private void UpdateObjectToolsWindow()
    {
        if (_viewModel == null)
            return;

        var shouldShow = _viewModel.IsObjectToolsPanelVisible &&
                         !_viewModel.IsObjectToolsDocked && _viewModel.IsEditorTabVisible;
        if (!shouldShow)
        {
            if (_objectToolsWindow != null)
            {
                _objectToolsWindow.CancelDrag();
                _objectToolsWindow.Hide();
            }
            WorkspaceDockPreview.IsVisible = false;
            return;
        }

        if (_objectToolsWindow == null)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return;
            _objectToolsWindow = new ObjectToolsWindow { DataContext = _viewModel };
            _objectToolsWindow.Closed += ObjectToolsWindow_OnClosed;
            _objectToolsWindow.DockRequested = ObjectToolsWindow_OnDockRequested;
            _objectToolsWindow.DragMoved = ObjectToolsWindow_OnDragMoved;
            var layout = _panelLayouts[ObjectToolsDockPanel];
            _objectToolsWindow.Width = Math.Max(_objectToolsWindow.MinWidth, layout.FloatingWidth);
            _objectToolsWindow.Height = Math.Max(_objectToolsWindow.MinHeight, layout.FloatingHeight);
            _objectToolsWindow.Show(owner);
        }
        else if (!_objectToolsWindow.IsVisible)
        {
            _objectToolsWindow.Show();
        }

        if (_objectFloatPosition is { } position)
        {
            _objectToolsWindow.Position = position;
            _objectFloatPosition = null;
        }
    }

    private void ObjectToolsWindow_OnDragMoved(PixelPoint screenPointer)
    {
        var target = GetObjectDockTarget(screenPointer);
        if (target == null)
        {
            WorkspaceDockPreview.IsVisible = false;
            return;
        }

        var previewPoint = target switch
        {
            PanelDock.WorkspaceLeft => new Point(0, PanelCanvas.Bounds.Height / 2d),
            PanelDock.WorkspaceRight => new Point(PanelCanvas.Bounds.Width, PanelCanvas.Bounds.Height / 2d),
            _ => new Point(PanelCanvas.Bounds.Width / 2d, PanelCanvas.Bounds.Height)
        };
        UpdateWorkspaceDockPreview(ObjectToolsDockPanel, previewPoint);
    }

    private PanelDock? GetObjectDockTarget(PixelPoint screenPointer)
    {
        if (_objectToolsWindow == null || PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0)
            return null;

        var origin = PanelCanvas.PointToScreen(new Point());
        var far = PanelCanvas.PointToScreen(new Point(PanelCanvas.Bounds.Width, PanelCanvas.Bounds.Height));
        var workspace = new PixelRect(origin.X, origin.Y, far.X - origin.X, far.Y - origin.Y);
        var scale = _objectToolsWindow.RenderScaling;
        var edge = ObjectWindowDocking.FindTarget(
            screenPointer, workspace, Math.Max(1, (int)Math.Round(48d * scale)));
        return edge switch
        {
            ObjectDockEdge.Left => PanelDock.WorkspaceLeft,
            ObjectDockEdge.Right => PanelDock.WorkspaceRight,
            ObjectDockEdge.Bottom => PanelDock.WorkspaceBottom,
            _ => null
        };
    }

    private void ObjectToolsWindow_OnDockRequested(PixelPoint screenPointer)
    {
        if (_viewModel == null || _objectToolsWindow == null)
            return;

        WorkspaceDockPreview.IsVisible = false;
        var target = GetObjectDockTarget(screenPointer);
        if (target == null)
            return;

        var layout = _panelLayouts[ObjectToolsDockPanel];
        layout.FloatingWidth = _objectToolsWindow.Width;
        layout.FloatingHeight = _objectToolsWindow.Height;
        if (target is PanelDock.WorkspaceLeft or PanelDock.WorkspaceRight)
            layout.DockWidthFraction = Math.Clamp(
                layout.FloatingWidth / Math.Max(1d, PanelCanvas.Bounds.Width), 0.3d, 0.55d);
        else
            layout.DockHeightFraction = Math.Clamp(
                layout.FloatingHeight / Math.Max(1d, PanelCanvas.Bounds.Height), 0.25d, 0.55d);
        _panelDocks[ObjectToolsDockPanel] = target.Value;
        _viewModel.IsObjectToolsDocked = true;
        UpdateObjectToolsWindow();
        ArrangePanels();
    }

    private void ObjectToolsWindow_OnClosed(object? sender, EventArgs e)
    {
        var window = _objectToolsWindow;
        if (window == null || !ReferenceEquals(sender, window))
            return;
        _objectFloatPosition = window.Position;
        _panelLayouts[ObjectToolsDockPanel].FloatingWidth = window.Width;
        _panelLayouts[ObjectToolsDockPanel].FloatingHeight = window.Height;
        window.Closed -= ObjectToolsWindow_OnClosed;
        window.DockRequested = null;
        window.DragMoved = null;
        _objectToolsWindow = null;
        WorkspaceDockPreview.IsVisible = false;
        if (_viewModel != null)
            _viewModel.ObjectEditor.IsPanelVisible = false;
    }

    private void CloseObjectToolsWindow()
    {
        if (_objectToolsWindow == null)
            return;
        var window = _objectToolsWindow;
        _objectToolsWindow = null;
        window.Closed -= ObjectToolsWindow_OnClosed;
        window.DockRequested = null;
        window.DragMoved = null;
        window.CancelDrag();
        window.Close();
    }

    private void FloatObjectToolsAt(PixelPoint screenPosition, PixelPoint pointer)
    {
        if (_viewModel == null)
            return;
        var layout = _panelLayouts[ObjectToolsDockPanel];
        _objectFloatPosition = screenPosition;
        _viewModel.IsObjectToolsDocked = false;
        UpdateObjectToolsWindow();
        if (_objectToolsWindow != null)
        {
            _objectToolsWindow.Width = Math.Max(_objectToolsWindow.MinWidth, layout.FloatingWidth);
            _objectToolsWindow.Height = Math.Max(_objectToolsWindow.MinHeight, layout.FloatingHeight);
            _objectToolsWindow.BeginDrag(pointer, alreadyMoved: true);
        }
        ArrangePanels();
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
            Key.D4 => viewModel.Modes.FirstOrDefault(mode => mode.Id == EditorModeDefinitions.ObjectId),
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
        if (ReferenceEquals(panel, ObjectToolsDockPanel) && _viewModel?.IsObjectToolsDocked == true)
        {
            _pendingObjectDrag = true;
            _objectDragStart = pointer;
            e.Pointer.Capture(handle);
            e.Handled = true;
            return;
        }

        _panelDragPointer = pointer;
        if (IsWorkspaceDock(_panelDocks[panel]))
        {
            var localPointer = e.GetPosition(panel);
            var layout = _panelLayouts[panel];
            panel.Width = layout.FloatingWidth;
            panel.Height = layout.FloatingHeight;
            panel.CornerRadius = new CornerRadius(6);
            _panelDocks[panel] = PanelDock.Floating;
            SetFloatingPosition(
                panel,
                pointer.X - Math.Min(localPointer.X, panel.Width - 20),
                pointer.Y - Math.Min(localPointer.Y, panel.Height - 20));
            ArrangePanels();
        }
        var left = GetCanvasCoordinate(Canvas.GetLeft(panel));
        var top = GetCanvasCoordinate(Canvas.GetTop(panel));
        _dragOffset = new Point(pointer.X - left, pointer.Y - top);
        _draggingPanel = panel;
        _panelDocks[panel] = PanelDock.Floating;
        WorkspaceDockPreview.IsVisible = false;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void PanelDragHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingObjectDrag)
        {
            var dragPointer = e.GetPosition(PanelCanvas);
            if (Math.Abs(dragPointer.X - _objectDragStart.X) < 6d &&
                Math.Abs(dragPointer.Y - _objectDragStart.Y) < 6d)
                return;

            _pendingObjectDrag = false;
            var position = PanelCanvas.PointToScreen(new Point(
                GetCanvasCoordinate(Canvas.GetLeft(ObjectToolsDockPanel)),
                GetCanvasCoordinate(Canvas.GetTop(ObjectToolsDockPanel))));
            var screenPointer = PanelCanvas.PointToScreen(dragPointer);
            e.Pointer.Capture(null);
            FloatObjectToolsAt(position, screenPointer);
            e.Handled = true;
            return;
        }

        if (_draggingPanel == null)
            return;

        var pointer = e.GetPosition(PanelCanvas);
        _panelDragPointer = pointer;
        SetFloatingPosition(_draggingPanel, pointer.X - _dragOffset.X, pointer.Y - _dragOffset.Y);
        UpdateWorkspaceDockPreview(_draggingPanel, pointer);
        e.Handled = true;
    }

    private void PanelDragHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pendingObjectDrag)
        {
            _pendingObjectDrag = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_draggingPanel == null)
            return;

        var panel = _draggingPanel;
        _draggingPanel = null;
        _panelDragPointer = e.GetPosition(PanelCanvas);
        WorkspaceDockPreview.IsVisible = false;
        e.Pointer.Capture(null);
        var layout = _panelLayouts[panel];
        layout.FloatingWidth = panel.Width;
        layout.FloatingHeight = panel.Height;
        var dockTarget = GetWorkspaceDockTarget(_panelDragPointer);
        if (dockTarget is PanelDock.WorkspaceLeft or PanelDock.WorkspaceRight)
        {
            layout.DockWidthFraction = GetWidthFraction(panel.Width);
            _panelDocks[panel] = dockTarget.Value;
        }
        else if (dockTarget == PanelDock.WorkspaceBottom)
        {
            layout.DockHeightFraction = GetHeightFraction(panel.Height);
            _panelDocks[panel] = PanelDock.WorkspaceBottom;
        }
        ArrangePanels();
        e.Handled = true;
    }

    private void PanelHorizontalResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var panel = sender is Control control ? GetPanel(control) : null;
        if (panel == null)
            return;

        EnsureFloatingForResize(panel);
        var oldWidth = panel.Width;
        var resizingLeftEdge = sender is Control handle && handle.Name?.Contains("Left", StringComparison.Ordinal) == true;
        var widthDelta = resizingLeftEdge ? -e.Vector.X : e.Vector.X;
        var minimumWidth = GetMinimumPanelWidth(panel);
        panel.Width = Math.Clamp(oldWidth + widthDelta, minimumWidth,
            Math.Max(minimumWidth, PanelCanvas.Bounds.Width - 20));
        var layout = _panelLayouts[panel];
        if (_panelDocks[panel] is PanelDock.WorkspaceLeft or PanelDock.WorkspaceRight)
            layout.DockWidthFraction = GetWidthFraction(panel.Width);
        else
            layout.FloatingWidth = panel.Width;
        if (ReferenceEquals(panel, ToolsPanel))
            RememberToolsModeWidth(panel.Width);
        if (resizingLeftEdge)
            Canvas.SetLeft(panel, GetCanvasCoordinate(Canvas.GetLeft(panel)) + oldWidth - panel.Width);
        if (IsWorkspaceDock(_panelDocks[panel]))
            ArrangePanels();
        else
            ArrangePanel(panel);
    }

    private void PanelCornerResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var panel = sender is Control control ? GetPanel(control) : null;
        if (panel == null)
            return;
        EnsureFloatingForResize(panel);
        if (_panelDocks[panel] != PanelDock.Floating)
            return;

        var resizeFromLeft = sender is Control handle &&
                             handle.Name?.Contains("Left", StringComparison.Ordinal) == true;
        var oldWidth = panel.Width;
        var minimumWidth = GetMinimumPanelWidth(panel);
        panel.Width = Math.Clamp(
            oldWidth + (resizeFromLeft ? -e.Vector.X : e.Vector.X),
            minimumWidth,
            Math.Max(minimumWidth, PanelCanvas.Bounds.Width - 20d));
        if (resizeFromLeft)
        {
            Canvas.SetLeft(
                panel,
                GetCanvasCoordinate(Canvas.GetLeft(panel)) + oldWidth - panel.Width);
        }

        if (sender is Control corner && corner.Name?.Contains("Top", StringComparison.Ordinal) == true)
            ResizeFloatingPanelTop(panel, e.Vector.Y);
        else
            ResizePanelBottom(panel, e.Vector.Y);
        _panelLayouts[panel].FloatingWidth = panel.Width;
        _panelLayouts[panel].FloatingHeight = panel.Height;
        if (ReferenceEquals(panel, ToolsPanel))
            RememberToolsModeWidth(panel.Width);
        ArrangePanel(panel);
    }

    private void PanelTopResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var panel = sender is Control control ? GetPanel(control) : null;
        if (panel == null)
            return;
        EnsureFloatingForResize(panel);
        var dock = _panelDocks.GetValueOrDefault(panel);
        if (dock == PanelDock.WorkspaceBottom)
        {
            var minimumHeight = Math.Max(180d, GetMinimumPanelHeight(panel));
            var height = Math.Clamp(
                panel.Height - e.Vector.Y,
                minimumHeight,
                Math.Max(minimumHeight, PanelCanvas.Bounds.Height - 80d));
            _panelLayouts[panel].DockHeightFraction = GetHeightFraction(height);
        }
        else if (dock == PanelDock.Floating)
        {
            ResizeFloatingPanelTop(panel, e.Vector.Y);
            _panelLayouts[panel].FloatingHeight = panel.Height;
        }
        else
        {
            return;
        }

        if (dock == PanelDock.WorkspaceBottom)
            ArrangePanels();
        else
            ArrangePanel(panel);
    }

    private void PanelBottomResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var panel = sender is Control control ? GetPanel(control) : null;
        if (panel == null)
            return;

        EnsureFloatingForResize(panel);
        ResizePanelBottom(panel, e.Vector.Y);
        _panelLayouts[panel].FloatingHeight = panel.Height;
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

    private void EnsureFloatingForResize(Border panel)
    {
        if (_panelDocks[panel] is PanelDock.OverlayLeft or PanelDock.OverlayRight)
            _panelDocks[panel] = PanelDock.Floating;
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

        ArrangeDockedPanels();
        ApplyWorkspaceViewportMargin();
        ArrangePanel(TextureBrowserPanel);
        ArrangePanel(InspectorPanel);
        ArrangePanel(ToolsPanel);
        ArrangePanel(ObjectToolsDockPanel);
        ApplyViewportChromeMargin();
    }

    private void ArrangePanel(Border panel)
    {
        if (PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0 || !panel.IsVisible)
            return;

        var dock = _panelDocks.GetValueOrDefault(panel, PanelDock.Floating);
        if (IsWorkspaceDock(dock))
            return;
        panel.CornerRadius = new CornerRadius(6);
        panel.Width = Math.Clamp(panel.Width,
            Math.Min(GetMinimumPanelWidth(panel), PanelCanvas.Bounds.Width),
            PanelCanvas.Bounds.Width);
        var viewportBounds = GetViewportBounds();
        var minimumHeight = GetMinimumPanelHeight(panel);
        panel.MaxHeight = Math.Max(minimumHeight, PanelCanvas.Bounds.Height - 20);
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

        SyncResizeLayer(panel);
    }

    private Border[] GetPanelsInDockOrder() =>
        [TextureBrowserPanel, InspectorPanel, ToolsPanel, ObjectToolsDockPanel];

    private void ArrangeDockedPanels()
    {
        var panels = GetPanelsInDockOrder()
            .Where(panel => panel.IsVisible && IsWorkspaceDock(_panelDocks[panel]))
            .ToArray();
        var usedWidth = 0d;
        var usedHeight = 0d;
        foreach (var panel in panels)
        {
            var dock = _panelDocks[panel];
            var layout = _panelLayouts[panel];
            if (ReferenceEquals(panel, TextureBrowserPanel) &&
                _viewModel?.TextureEditor.IsBrowserExpanded == false)
                _viewModel.TextureEditor.IsBrowserExpanded = true;
            if (dock is PanelDock.WorkspaceLeft or PanelDock.WorkspaceRight)
            {
                var minimumWidth = GetMinimumPanelWidth(panel);
                panel.Width = Math.Clamp(
                    PanelCanvas.Bounds.Width * layout.DockWidthFraction,
                    minimumWidth,
                    Math.Max(minimumWidth, PanelCanvas.Bounds.Width - 200d - usedWidth));
                usedWidth += panel.Width + 5d;
            }
            else
            {
                var minimumHeight = Math.Max(180d, GetMinimumPanelHeight(panel));
                panel.Height = Math.Clamp(
                    PanelCanvas.Bounds.Height * layout.DockHeightFraction,
                    minimumHeight,
                    Math.Max(minimumHeight, PanelCanvas.Bounds.Height - 80d - usedHeight));
                usedHeight += panel.Height + 5d;
            }
            panel.CornerRadius = new CornerRadius(0);
            panel.MaxHeight = double.PositiveInfinity;
        }

        var leftInset = panels.Where(panel => _panelDocks[panel] == PanelDock.WorkspaceLeft)
            .Sum(panel => panel.Width + 5d);
        var rightInset = panels.Where(panel => _panelDocks[panel] == PanelDock.WorkspaceRight)
            .Sum(panel => panel.Width + 5d);
        var bottomInset = panels.Where(panel => _panelDocks[panel] == PanelDock.WorkspaceBottom)
            .Sum(panel => panel.Height + 5d);
        var left = 0d;
        var right = 0d;
        var bottom = 0d;
        foreach (var panel in panels)
        {
            switch (_panelDocks[panel])
            {
                case PanelDock.WorkspaceLeft:
                    panel.Height = Math.Max(0d, PanelCanvas.Bounds.Height - bottomInset);
                    Canvas.SetLeft(panel, left);
                    Canvas.SetTop(panel, 0d);
                    left += panel.Width + 5d;
                    break;
                case PanelDock.WorkspaceRight:
                    panel.Height = Math.Max(0d, PanelCanvas.Bounds.Height - bottomInset);
                    Canvas.SetLeft(panel, PanelCanvas.Bounds.Width - right - panel.Width);
                    Canvas.SetTop(panel, 0d);
                    right += panel.Width + 5d;
                    break;
                case PanelDock.WorkspaceBottom:
                    panel.Width = Math.Max(0d, PanelCanvas.Bounds.Width - leftInset - rightInset);
                    Canvas.SetLeft(panel, leftInset);
                    Canvas.SetTop(panel, PanelCanvas.Bounds.Height - bottom - panel.Height);
                    bottom += panel.Height + 5d;
                    break;
            }
            SyncResizeLayer(panel);
        }
    }

    private void ApplyWorkspaceViewportMargin()
    {
        var panels = GetPanelsInDockOrder().Where(panel => panel.IsVisible).ToArray();
        EditorViewport.Margin = new Thickness(
            panels.Where(panel => _panelDocks[panel] == PanelDock.WorkspaceLeft).Sum(panel => panel.Width + 5d),
            0d,
            panels.Where(panel => _panelDocks[panel] == PanelDock.WorkspaceRight).Sum(panel => panel.Width + 5d),
            panels.Where(panel => _panelDocks[panel] == PanelDock.WorkspaceBottom).Sum(panel => panel.Height + 5d));
    }

    private Rect GetViewportBounds()
    {
        var margin = EditorViewport.Margin;
        return new Rect(
            margin.Left,
            margin.Top,
            Math.Max(0d, PanelCanvas.Bounds.Width - margin.Left - margin.Right),
            Math.Max(0d, PanelCanvas.Bounds.Height - margin.Top - margin.Bottom));
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
        ObjectToolsToggleButton.Margin = new Thickness(0d, 10d, rightInset + 100d, 0d);
        TextureBrowserShowButton.Margin = new Thickness(leftInset + 56d, 10d, 0d, 0d);
    }

    private PanelDock? GetWorkspaceDockTarget(Point pointer)
    {
        const double snapDistance = 48d;
        if (pointer.X <= snapDistance)
            return PanelDock.WorkspaceLeft;
        if (pointer.X >= PanelCanvas.Bounds.Width - snapDistance)
            return PanelDock.WorkspaceRight;
        if (pointer.Y >= PanelCanvas.Bounds.Height - snapDistance)
            return PanelDock.WorkspaceBottom;
        return null;
    }

    private void UpdateWorkspaceDockPreview(Border panel, Point pointer)
    {
        if (ReferenceEquals(panel, ObjectToolsDockPanel) &&
            (pointer.X < 0 || pointer.X > PanelCanvas.Bounds.Width ||
             pointer.Y < 0 || pointer.Y > PanelCanvas.Bounds.Height))
        {
            WorkspaceDockPreview.IsVisible = false;
            return;
        }

        var dock = GetWorkspaceDockTarget(pointer);
        if (dock == null)
        {
            WorkspaceDockPreview.IsVisible = false;
            return;
        }

        var viewport = GetViewportBounds();
        if (dock is PanelDock.WorkspaceLeft or PanelDock.WorkspaceRight)
        {
            var minimumWidth = GetMinimumPanelWidth(panel);
            WorkspaceDockPreview.Width = Math.Clamp(
                PanelCanvas.Bounds.Width * GetWidthFraction(panel.Width),
                minimumWidth,
                Math.Max(minimumWidth, viewport.Width - 200d));
            WorkspaceDockPreview.Height = viewport.Height;
            Canvas.SetLeft(
                WorkspaceDockPreview,
                dock == PanelDock.WorkspaceLeft
                    ? viewport.Left
                    : viewport.Right - WorkspaceDockPreview.Width);
            Canvas.SetTop(WorkspaceDockPreview, 0d);
        }
        else
        {
            var minimumHeight = Math.Max(180d, GetMinimumPanelHeight(panel));
            WorkspaceDockPreview.Width = viewport.Width;
            WorkspaceDockPreview.Height = Math.Clamp(
                PanelCanvas.Bounds.Height * GetHeightFraction(panel.Height),
                minimumHeight,
                Math.Max(minimumHeight, viewport.Height - 80d));
            Canvas.SetLeft(WorkspaceDockPreview, viewport.Left);
            Canvas.SetTop(
                WorkspaceDockPreview,
                PanelCanvas.Bounds.Height - WorkspaceDockPreview.Height);
        }

        WorkspaceDockPreview.IsVisible = true;
    }

    private double GetWidthFraction(double width) =>
        Math.Clamp(width / Math.Max(1d, PanelCanvas.Bounds.Width), 0.15d, 0.8d);

    private double GetHeightFraction(double height) =>
        Math.Clamp(height / Math.Max(1d, PanelCanvas.Bounds.Height), 0.15d, 0.8d);

    private void SyncResizeLayer(Border panel)
    {
        var layer = ReferenceEquals(panel, TextureBrowserPanel)
            ? TextureBrowserResizeLayer
            : ReferenceEquals(panel, InspectorPanel)
                ? InspectorResizeLayer
                : ToolsResizeLayer;
        layer.Width = panel.Width;
        layer.Height = panel.Height;
        Canvas.SetLeft(layer, GetCanvasCoordinate(Canvas.GetLeft(panel)));
        Canvas.SetTop(layer, GetCanvasCoordinate(Canvas.GetTop(panel)));

        var dock = _panelDocks[panel];
        var floating = !IsWorkspaceDock(dock);
        var canResize = !ReferenceEquals(panel, TextureBrowserPanel) ||
                        _viewModel?.TextureEditor.IsBrowserExpanded != false;
        if (ReferenceEquals(panel, TextureBrowserPanel))
            SetResizeHandles(canResize, floating, dock,
                TextureBrowserLeftResizeHandle, TextureBrowserRightResizeHandle,
                TextureBrowserTopResizeHandle, TextureBrowserBottomResizeHandle,
                TextureBrowserTopLeftResizeHandle, TextureBrowserTopRightResizeHandle,
                TextureBrowserBottomLeftResizeHandle, TextureBrowserBottomRightResizeHandle);
        else if (ReferenceEquals(panel, InspectorPanel))
            SetResizeHandles(canResize, floating, dock,
                InspectorOuterLeftResizeHandle, InspectorOuterRightResizeHandle,
                InspectorTopResizeHandle, InspectorOuterBottomResizeHandle,
                InspectorTopLeftResizeHandle, InspectorTopRightResizeHandle,
                InspectorBottomLeftResizeHandle, InspectorBottomRightResizeHandle);
        else
            SetResizeHandles(canResize, floating, dock,
                ToolsOuterLeftResizeHandle, ToolsOuterRightResizeHandle,
                ToolsTopResizeHandle, ToolsOuterBottomResizeHandle,
                ToolsTopLeftResizeHandle, ToolsTopRightResizeHandle,
                ToolsBottomLeftResizeHandle, ToolsBottomRightResizeHandle);
    }

    private static void SetResizeHandles(
        bool canResize, bool floating, PanelDock dock,
        Thumb left, Thumb right, Thumb top, Thumb bottom,
        Thumb topLeft, Thumb topRight, Thumb bottomLeft, Thumb bottomRight)
    {
        left.IsVisible = canResize && (floating || dock == PanelDock.WorkspaceRight);
        right.IsVisible = canResize && (floating || dock == PanelDock.WorkspaceLeft);
        top.IsVisible = canResize && (floating || dock == PanelDock.WorkspaceBottom);
        bottom.IsVisible = canResize && floating;
        topLeft.IsVisible = canResize && floating;
        topRight.IsVisible = canResize && floating;
        bottomLeft.IsVisible = canResize && floating;
        bottomRight.IsVisible = canResize && floating;
    }

    private static bool IsWorkspaceDock(PanelDock dock) =>
        dock is PanelDock.WorkspaceLeft or PanelDock.WorkspaceRight or PanelDock.WorkspaceBottom;

    private static double GetCanvasCoordinate(double coordinate) => double.IsNaN(coordinate) ? 0 : coordinate;

    private double GetMinimumPanelHeight(Border panel) =>
        ReferenceEquals(panel, TextureBrowserPanel) &&
        _viewModel?.TextureEditor.IsBrowserExpanded == false
            ? 44d
            : Math.Max(260d, panel.MinHeight);

    private double GetMinimumPanelWidth(Border panel) =>
        ReferenceEquals(panel, ObjectToolsDockPanel)
            ? (_viewModel?.ObjectEditor.IsBrowserVisible == true ? 500d : 310d)
            : 260d;

    private void SyncToolsModeWidth()
    {
        if (_viewModel == null)
            return;

        ToolsPanel.Width = _brushToolsWidth;
        _panelLayouts[ToolsPanel].FloatingWidth = _brushToolsWidth;
        if (_panelDocks[ToolsPanel] is PanelDock.WorkspaceLeft or PanelDock.WorkspaceRight)
            _panelLayouts[ToolsPanel].DockWidthFraction = GetWidthFraction(_brushToolsWidth);
    }

    private void RememberToolsModeWidth(double width)
    {
        _brushToolsWidth = width;
    }

    private void SetFloatingPosition(Border panel, double left, double top)
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
            Math.Max(0d, PanelCanvas.Bounds.Height - retainedHeaderHeight)));
        SyncResizeLayer(panel);
    }

    private Border? GetPanel(Control source)
    {
        var name = source.Name;
        if (name?.Contains("Inspector", StringComparison.Ordinal) == true)
            return InspectorPanel;
        if (name?.StartsWith("ObjectTools", StringComparison.Ordinal) == true)
            return ObjectToolsDockPanel;
        if (_viewModel?.IsObjectToolsDockPanelVisible == true &&
            (name?.StartsWith("ToolsOuter", StringComparison.Ordinal) == true ||
             name?.StartsWith("ToolsTop", StringComparison.Ordinal) == true ||
             name?.StartsWith("ToolsBottom", StringComparison.Ordinal) == true))
            return ObjectToolsDockPanel;
        if (name?.Contains("Tools", StringComparison.Ordinal) == true)
            return ToolsPanel;
        if (name?.Contains("TextureBrowser", StringComparison.Ordinal) == true)
            return TextureBrowserPanel;
        return null;
    }
}
