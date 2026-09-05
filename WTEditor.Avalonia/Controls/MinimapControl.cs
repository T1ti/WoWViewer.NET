using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.Controls;

/// <summary>Only drawing and pointer wiring; content and viewport state belong to the view model.</summary>
public sealed class MinimapControl : Control
{
    private static readonly IBrush GridBrush = new SolidColorBrush(Colors.Orange, 0.2);
    private static readonly IBrush BackgroundBrush = Brush.Parse("#0B1116");
    private static readonly IBrush WmoBrush = new SolidColorBrush(Colors.LimeGreen, 0.35);
    private static readonly Pen WorldBorderPen = new(new SolidColorBrush(Color.FromRgb(255, 72, 28), 0.45), 2);
    private MinimapViewModel? _model;
    private Point? _dragPoint;
    private double Side => Math.Max(0, Math.Min(Bounds.Width, Bounds.Height));
    private Rect Square => new((Bounds.Width - Side) / 2, (Bounds.Height - Side) / 2, Side, Side);

    public MinimapControl()
    {
        ClipToBounds = true;
        DataContextChanged += (_, _) => BindModel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_model != null) _model.PropertyChanged -= ModelChanged;
        _model = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void BindModel()
    {
        if (_model != null) _model.PropertyChanged -= ModelChanged;
        _model = DataContext as MinimapViewModel;
        if (_model != null) _model.PropertyChanged += ModelChanged;
        InvalidateVisual();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var square = Square;
        context.FillRectangle(BackgroundBrush, square);
        if (_model == null || Side <= 0) return;
        var size = Side * _model.Zoom;
        var origin = square.TopLeft + new Vector(_model.OffsetX * Side, _model.OffsetY * Side);
        var cell = size / 64;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        using (context.PushClip(square))
        {
            var document = _model.Document;
            if (document?.Overview != null && cell * scale <= MinimapDocument.OverviewTileSize)
                context.DrawImage(document.Overview, new Rect(origin, new Size(size, size)));
            else if (document != null)
            {
                var minX = Math.Clamp((int)Math.Floor((square.Left - origin.X) / cell), 0, 63);
                var maxX = Math.Clamp((int)Math.Floor((square.Right - origin.X) / cell), 0, 63);
                var minY = Math.Clamp((int)Math.Floor((square.Top - origin.Y) / cell), 0, 63);
                var maxY = Math.Clamp((int)Math.Floor((square.Bottom - origin.Y) / cell), 0, 63);
                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var image = document.GetTile(x, y);
                    if (image != null)
                        context.DrawImage(image, new Rect(origin.X + x * cell, origin.Y + y * cell, cell, cell));
                }
            }
            if (_model.GlobalWmoBounds is { } bounds)
                context.FillRectangle(WmoBrush, new Rect(origin.X + bounds.MinX * cell,
                    origin.Y + bounds.MinY * cell, (bounds.MaxX - bounds.MinX) * cell,
                    (bounds.MaxY - bounds.MinY) * cell));
            if (document != null)
                foreach (var image in document.WmoImages)
                {
                    var topLeft = new Point(origin.X + image.TopLeft.X * cell, origin.Y + image.TopLeft.Y * cell);
                    var right = new Vector((image.TopRight.X - image.TopLeft.X) * cell,
                        (image.TopRight.Y - image.TopLeft.Y) * cell);
                    var down = new Vector((image.BottomLeft.X - image.TopLeft.X) * cell,
                        (image.BottomLeft.Y - image.TopLeft.Y) * cell);
                    using (context.PushTransform(new Matrix(right.X, right.Y, down.X, down.Y, topLeft.X, topLeft.Y)))
                        context.DrawImage(image.Image, new Rect(0, 0, 1, 1));
                }
            // Give every line the same physical-pixel coverage. Fractional positions
            // otherwise create repeating darker lines at some zoom levels.
            var gridPen = new Pen(GridBrush, 0.5 / scale);
            var grid = new StreamGeometry();
            double Align(double coordinate) => (Math.Floor(coordinate * scale) + 0.5) / scale;
            using (var geometry = grid.Open())
            {
                for (var i = 1; i < 64; i++)
                {
                    var x = Align(origin.X + i * cell);
                    var y = Align(origin.Y + i * cell);
                    geometry.BeginFigure(new Point(x, origin.Y), false);
                    geometry.LineTo(new Point(x, origin.Y + size));
                    geometry.EndFigure(false);
                    geometry.BeginFigure(new Point(origin.X, y), false);
                    geometry.LineTo(new Point(origin.X + size, y));
                    geometry.EndFigure(false);
                }
            }
            context.DrawGeometry(null, gridPen, grid);
            context.DrawRectangle(null, WorldBorderPen, new Rect(origin, new Size(size, size)));
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_model == null || Side <= 0 || !Square.Contains(e.GetPosition(this))) return;
        var point = e.GetPosition(this) - Square.TopLeft;
        var zoom = Math.Clamp(_model.Zoom * Math.Pow(1.2, e.Delta.Y), MinimapViewModel.MinimumZoom, MinimapViewModel.MaximumZoom);
        var ratio = zoom / _model.Zoom;
        _model.OffsetX = point.X / Side - (point.X / Side - _model.OffsetX) * ratio;
        _model.OffsetY = point.Y / Side - (point.Y / Side - _model.OffsetY) * ratio;
        _model.Zoom = zoom;
        ClampOffsets();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_model == null || !Square.Contains(e.GetPosition(this)) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) _model.ResetViewCommand.Execute(null);
        else
        {
            _dragPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_model == null || _dragPoint is not Point previous || Side <= 0) return;
        var point = e.GetPosition(this);
        _model.OffsetX += (point.X - previous.X) / Side;
        _model.OffsetY += (point.Y - previous.Y) / Side;
        _dragPoint = point;
        ClampOffsets();
        e.Handled = true;
    }

    private void ClampOffsets()
    {
        if (_model == null) return;
        _model.ClampOffsets();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragPoint = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _dragPoint = null;
        base.OnPointerCaptureLost(e);
    }
}
