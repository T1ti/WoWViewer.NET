using System.Numerics;

namespace WoWRenderLib.DX11.Editing;

/// <summary>Logical-pixel overlay bounds; independent of a particular UI framework.</summary>
public readonly record struct ScreenSelectionRectangle(bool IsVisible, Vector2 Minimum, Vector2 Maximum);
internal readonly record struct ScreenSelectionRequest(Vector2 Start, Vector2 End, InputModifiers Modifiers, bool IsMarquee);

internal sealed class ScreenSelectionGesture
{
    private bool _wasDown;
    private bool _dragging;
    private Vector2? _start;
    public bool HasPointerGesture => _start.HasValue;
    public ScreenSelectionRectangle Rectangle { get; private set; }

    public ScreenSelectionRequest? Update(InputFrame input, bool gizmoOwnsInput, Vector2 viewport)
    {
        var pressed = input.LeftMouseDown && !_wasDown;
        var released = !input.LeftMouseDown && _wasDown;
        _wasDown = input.LeftMouseDown;
        if (input.Mode != EditorModeId.Selection || gizmoOwnsInput || input.RightMouseDown || input.CancelObjectManipulation)
        {
            Cancel();
            return null;
        }
        var point = Vector2.Clamp(input.MousePosition, Vector2.Zero, Vector2.Max(Vector2.One, viewport));
        if (pressed) _start = point;
        if (_start is not { } start) return null;
        var scale = Math.Max(1, input.PixelScale);
        _dragging |= Vector2.Distance(start, point) >= 5 * scale;
        Rectangle = new(_dragging, Vector2.Min(start, point) / scale, Vector2.Max(start, point) / scale);
        if (!released) return null;
        var result = new ScreenSelectionRequest(start, point, input.Modifiers, _dragging);
        Cancel();
        return result;
    }

    public void Cancel()
    {
        _start = null;
        _dragging = false;
        Rectangle = default;
    }
}
