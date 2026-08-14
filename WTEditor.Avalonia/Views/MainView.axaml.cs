using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace WTEditor.Avalonia.Views;

public partial class MainView : UserControl
{
    private enum InspectorDock { Left, Right, Floating }

    private InspectorDock _inspectorDock = InspectorDock.Right;
    private Point _dragOffset;
    private bool _draggingInspector;

    public MainView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangeInspector();
    }

    private void InspectorDragHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(InspectorDragHandle).Properties.IsLeftButtonPressed)
            return;

        var pointer = e.GetPosition(PanelCanvas);
        _dragOffset = new Point(pointer.X - Canvas.GetLeft(InspectorPanel), pointer.Y - Canvas.GetTop(InspectorPanel));
        _draggingInspector = true;
        _inspectorDock = InspectorDock.Floating;
        InspectorPanel.Height = Math.Min(620, Math.Max(260, PanelCanvas.Bounds.Height - 20));
        e.Pointer.Capture(InspectorDragHandle);
        e.Handled = true;
    }

    private void InspectorDragHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingInspector)
            return;

        var pointer = e.GetPosition(PanelCanvas);
        SetFloatingPosition(pointer.X - _dragOffset.X, pointer.Y - _dragOffset.Y);
        e.Handled = true;
    }

    private void InspectorDragHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_draggingInspector)
            return;

        _draggingInspector = false;
        e.Pointer.Capture(null);
        var left = Canvas.GetLeft(InspectorPanel);
        const double snapDistance = 28;
        if (left <= snapDistance)
            _inspectorDock = InspectorDock.Left;
        else if (PanelCanvas.Bounds.Width - left - InspectorPanel.Width <= snapDistance)
            _inspectorDock = InspectorDock.Right;
        ArrangeInspector();
        e.Handled = true;
    }

    private void InspectorHorizontalResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        var oldWidth = InspectorPanel.Width;
        var resizingLeftEdge = ReferenceEquals(sender, InspectorLeftResizeHandle);
        var widthDelta = resizingLeftEdge ? -e.Vector.X : e.Vector.X;
        InspectorPanel.Width = Math.Clamp(oldWidth + widthDelta, 260, Math.Max(260, PanelCanvas.Bounds.Width - 20));
        if (_inspectorDock == InspectorDock.Floating && resizingLeftEdge)
            Canvas.SetLeft(InspectorPanel, Canvas.GetLeft(InspectorPanel) + oldWidth - InspectorPanel.Width);
        ArrangeInspector();
    }

    private void InspectorBottomResize_OnDragDelta(object? sender, VectorEventArgs e)
    {
        if (_inspectorDock != InspectorDock.Floating)
            return;
        InspectorPanel.Height = Math.Clamp(
            InspectorPanel.Height + e.Vector.Y,
            260,
            Math.Max(260, PanelCanvas.Bounds.Height - Canvas.GetTop(InspectorPanel)));
        ArrangeInspector();
    }

    private void ArrangeInspector()
    {
        if (PanelCanvas.Bounds.Width <= 0 || PanelCanvas.Bounds.Height <= 0)
            return;

        InspectorPanel.Width = Math.Min(InspectorPanel.Width, Math.Max(260, PanelCanvas.Bounds.Width));
        if (_inspectorDock == InspectorDock.Left)
        {
            Canvas.SetLeft(InspectorPanel, 0);
            Canvas.SetTop(InspectorPanel, 0);
            InspectorPanel.Height = PanelCanvas.Bounds.Height;
        }
        else if (_inspectorDock == InspectorDock.Right)
        {
            Canvas.SetLeft(InspectorPanel, Math.Max(0, PanelCanvas.Bounds.Width - InspectorPanel.Width));
            Canvas.SetTop(InspectorPanel, 0);
            InspectorPanel.Height = PanelCanvas.Bounds.Height;
        }
        else
        {
            SetFloatingPosition(Canvas.GetLeft(InspectorPanel), Canvas.GetTop(InspectorPanel));
        }
    }

    private void SetFloatingPosition(double left, double top)
    {
        Canvas.SetLeft(InspectorPanel, Math.Clamp(left, 0, Math.Max(0, PanelCanvas.Bounds.Width - InspectorPanel.Width)));
        Canvas.SetTop(InspectorPanel, Math.Clamp(top, 0, Math.Max(0, PanelCanvas.Bounds.Height - InspectorPanel.Height)));
    }
}
