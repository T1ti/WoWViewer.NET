using System.Numerics;
using WoWRenderLib.DX11.Objects;

namespace WoWRenderLib.DX11.Editing;

/// <summary>An opaque renderer edit retains its original targets for undo after selection changes.</summary>
public sealed class ObjectTransformEdit
{
    internal readonly record struct Entry(Container3D Target, PlacementTransform Before, PlacementTransform After);
    internal Entry[] Entries { get; }
    public string Description { get; }
    internal ObjectTransformEdit(Entry[] entries, ObjectGizmoMode mode)
    {
        Entries = entries;
        Description = $"{mode} {entries.Length} object{(entries.Length == 1 ? "" : "s")}";
    }
}

public readonly record struct ObjectGizmoFeedback(string? Text, Vector2 ScreenPosition);
internal readonly record struct GizmoDragVisual(ObjectGizmoMode Operation, GizmoFrame Start, GizmoFrame Current,
    Vector3 Axis, Vector3 Radial, float Angle, float Scale, float RotationRadius = .85f);

internal sealed class ObjectGizmoController(Action<Container3D, PlacementTransform> apply)
{
    private readonly Queue<ObjectTransformEdit> _completed = [];
    private (Container3D Target, PlacementTransform Before)[] _targets = [];
    private GizmoFrame _startFrame;
    private Vector2 _startPointer, _screenTangent;
    private Vector3 _startPoint, _axis, _planeNormal;
    private float _lastAngle, _angle;
    private int _selectionRevision;
    private bool _wasDown, _planeRotation, _scaleLocked;
    private ObjectGizmoOrientation _orientation;
    private (GizmoFrame Frame, ObjectGizmoMode Mode, Vector2 Pointer, Matrix4x4 VP, Vector2 Viewport, float Dpi, bool Scale)? _lastPick;
    private GizmoHandle _lastHandle;
    private Vector3 _lastHitPoint;
    public bool IsDragging { get; private set; }
    public bool IsVisible { get; private set; }
    public GizmoHandle Handle { get; private set; }
    public GizmoFrame Frame { get; private set; }
    public ObjectGizmoMode Mode { get; private set; }
    public ObjectGizmoMode Operation { get; private set; }
    public bool ScaleEnabled => !_scaleLocked;
    public Vector2 ViewportSize { get; private set; }
    public float PixelScale { get; private set; } = 1;
    public ObjectGizmoFeedback Feedback { get; private set; }
    public GizmoDragVisual DragVisual { get; private set; }
    public ObjectTransformEdit? TakeCompletedEdit() => _completed.TryDequeue(out var edit) ? edit : null;

    /// <returns>True while the gizmo owns input, including the release/cancel frame.</returns>
    public bool Update(InputFrame input, ObjectSelection selection, Camera camera, int width, int height, bool lockWmoScale)
    {
        ViewportSize = new(width, height);
        PixelScale = Math.Max(1, input.PixelScale);
        var consumed = IsDragging;
        var pressed = input.LeftMouseDown && !_wasDown;
        _wasDown = input.LeftMouseDown;
        if (IsDragging && (input.CancelObjectManipulation || input.RightMouseDown ||
            input.Mode != EditorModeId.Selection || selection.Revision != _selectionRevision ||
            input.GizmoMode != Mode || input.GizmoOrientation != _orientation))
            Cancel();
        if (IsDragging)
        {
            Drag(input.MousePosition, camera, width, height);
            if (!input.LeftMouseDown) Commit();
            return true;
        }
        Mode = input.GizmoMode;
        _orientation = input.GizmoOrientation;
        var editable = true;
        _scaleLocked = false;
        for (var index = 0; index < selection.Objects.Count; index++)
        {
            editable &= selection.Objects[index] is WMOContainer or M2Container { ParentWMO: null };
            _scaleLocked |= lockWmoScale && selection.Objects[index] is WMOContainer;
        }
        var hasFrame = ObjectGizmoMath.TryCreateFrame(selection.Objects, _orientation, camera, height, PixelScale, out var frame);
        IsVisible = input.Mode == EditorModeId.Selection && editable && hasFrame && !(Mode == ObjectGizmoMode.Scale && _scaleLocked);
        Handle = GizmoHandle.None;
        if (!IsVisible) return consumed;
        Frame = frame;
        var pick = (Frame, Mode, input.MousePosition, camera.GetViewMatrix() * camera.GetProjectionMatrix(), ViewportSize, PixelScale, ScaleEnabled);
        if (_lastPick != pick)
        {
            _lastHandle = ObjectGizmoGeometry.For(Mode).HitTest(Frame, Mode, input.MousePosition,
                pick.Item4, ViewportSize, PixelScale, out _lastHitPoint, ScaleEnabled);
            _lastPick = pick;
        }
        Handle = _lastHandle;
        var hitPoint = _lastHitPoint;
        if (!consumed && pressed && !input.RightMouseDown && !input.CancelObjectManipulation && Handle != GizmoHandle.None)
        {
            consumed = true;
            Begin(input.MousePosition, hitPoint, selection, camera, width, height);
        }
        return consumed;
    }

    private void Begin(Vector2 pointer, Vector3 hitPoint, ObjectSelection selection, Camera camera, int width, int height)
    {
        _startFrame = Frame;
        _startPointer = pointer;
        Operation = ResolveOperation(Mode, Handle);
        var index = AxisIndex(Handle);
        _axis = Handle == GizmoHandle.ViewRotate ? -Vector3.Normalize(camera.Front) : index >= 0 ? Frame.Axis(index) : Vector3.Zero;
        _startPoint = Frame.Pivot;
        var ray = camera.GetRayFromScreen(pointer.X, pointer.Y, width, height);
        _planeNormal = Handle switch
        {
            GizmoHandle.XY => Frame.Axis(2), GizmoHandle.YZ => Frame.Axis(0), GizmoHandle.ZX => Frame.Axis(1), _ => camera.Front
        };
        _angle = _lastAngle = 0;
        if (Operation == ObjectGizmoMode.Rotate)
        {
            _planeNormal = _axis;
            _planeRotation = MathF.Abs(Vector3.Dot(ray.Direction, _axis)) > .15f;
            if (_planeRotation)
            {
                if (!ObjectGizmoMath.IntersectPlane(ray, Frame.Pivot, _axis, out _startPoint) ||
                    Vector3.DistanceSquared(_startPoint, Frame.Pivot) < .0001f) return;
            }
            else
            {
                // Edge-on rings use their projected tangent instead of a nearly parallel plane.
                _startPoint = hitPoint;
                var tangent = Vector3.Cross(_axis, hitPoint - Frame.Pivot);
                var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
                if (!ObjectGizmoMath.Project(hitPoint, vp, new(width, height), out var a) ||
                    !ObjectGizmoMath.Project(hitPoint + tangent, vp, new(width, height), out var b) ||
                    Vector2.DistanceSquared(a, b) < 16) return;
                _screenTangent = b - a;
            }
        }
        else if (Operation == ObjectGizmoMode.Move)
        {
            if (_axis != Vector3.Zero)
            {
                _planeNormal = camera.Front - _axis * Vector3.Dot(camera.Front, _axis);
                if (_planeNormal.LengthSquared() < .0001f) return;
                _planeNormal = Vector3.Normalize(_planeNormal);
            }
            if (!ObjectGizmoMath.IntersectPlane(ray, Frame.Pivot, _planeNormal, out _startPoint)) return;
        }
        else
        {
            _screenTangent = new Vector2(1, -1) * 64 * PixelScale;
            if (_axis != Vector3.Zero)
            {
                var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
                if (!ObjectGizmoMath.Project(Frame.Pivot, vp, new(width, height), out var a) ||
                    !ObjectGizmoMath.Project(Frame.Pivot + _axis * Frame.Size, vp, new(width, height), out var b) ||
                    Vector2.DistanceSquared(a, b) < 16) return;
                _screenTangent = b - a;
            }
        }
        _targets = selection.Objects.Select(item => (item, PlacementTransform.Capture(item))).ToArray();
        _selectionRevision = selection.Revision;
        IsDragging = true;
        Drag(pointer, camera, width, height);
    }

    private void Drag(Vector2 pointer, Camera camera, int width, int height)
    {
        var ray = camera.GetRayFromScreen(pointer.X, pointer.Y, width, height);
        var delta = Vector3.Zero;
        var factor = 1f;
        if (Operation == ObjectGizmoMode.Move)
        {
            if (!ObjectGizmoMath.IntersectPlane(ray, _startFrame.Pivot, _planeNormal, out var point)) return;
            delta = point - _startPoint;
            if (_axis != Vector3.Zero) delta = _axis * Vector3.Dot(delta, _axis);
            Frame = _startFrame with { Pivot = _startFrame.Pivot + delta };
        }
        else if (Operation == ObjectGizmoMode.Rotate)
        {
            if (_planeRotation)
            {
                if (!ObjectGizmoMath.IntersectPlane(ray, _startFrame.Pivot, _axis, out var point) ||
                    Vector3.DistanceSquared(point, _startFrame.Pivot) < .0001f) return;
                var a = Vector3.Normalize(_startPoint - _startFrame.Pivot);
                var b = Vector3.Normalize(point - _startFrame.Pivot);
                var angle = MathF.Atan2(Vector3.Dot(_axis, Vector3.Cross(a, b)), Vector3.Dot(a, b));
                _angle += MathF.IEEERemainder(angle - _lastAngle, MathF.Tau);
                _lastAngle = angle;
            }
            else _angle = Vector2.Dot(pointer - _startPointer, _screenTangent) / _screenTangent.LengthSquared();
        }
        else
        {
            factor = MathF.Exp(Math.Clamp(Vector2.Dot(pointer - _startPointer, _screenTangent) / _screenTangent.LengthSquared(), -8, 8));
            var minimum = 0f;
            var maximum = float.MaxValue;
            foreach (var target in _targets)
            {
                minimum = Math.Max(minimum, .001f / Math.Max(.001f, target.Before.Scale));
                maximum = Math.Min(maximum, (65535f / 1024f) / Math.Max(.001f, target.Before.Scale));
            }
            factor = minimum <= maximum ? Math.Clamp(factor, minimum, maximum) : 1;
        }
        var radial = _startPoint - _startFrame.Pivot;
        DragVisual = new(Operation, _startFrame, Frame, _axis,
            radial.LengthSquared() > 1e-8f ? Vector3.Normalize(radial) : Vector3.UnitX, _angle, factor,
            Handle == GizmoHandle.ViewRotate ? Mode == ObjectGizmoMode.Transform ? 1.38f : 1.08f : .85f);
        var axisName = AxisIndex(Handle) switch { 0 => "X", 1 => "Y", 2 => "Z", _ => Handle == GizmoHandle.ViewRotate ? "View" : "Δ" };
        var localDelta = Vector3.TransformNormal(delta, Matrix4x4.Transpose(_startFrame.Basis));
        var text = Operation switch
        {
            ObjectGizmoMode.Rotate => FormattableString.Invariant($"{axisName}: {_angle * 180 / MathF.PI:+0.00;-0.00;0.00}°"),
            ObjectGizmoMode.Scale => FormattableString.Invariant($"Scale: {factor:0.000}× ({(factor - 1) * 100:+0.0;-0.0;0.0}%)"),
            _ when _axis != Vector3.Zero => FormattableString.Invariant($"{axisName}: {Vector3.Dot(delta, _axis):+0.000;-0.000;0.000}"),
            _ => FormattableString.Invariant($"Δ: {localDelta.X:0.000}, {localDelta.Y:0.000}, {localDelta.Z:0.000}")
        };
        Feedback = new(text, new Vector2(Math.Clamp(pointer.X / PixelScale + 18, 0, Math.Max(0, width / PixelScale - 245)),
            Math.Clamp(pointer.Y / PixelScale + 22, 0, Math.Max(0, height / PixelScale - 35))));
        foreach (var target in _targets)
        {
            var after = Operation switch
            {
                ObjectGizmoMode.Move => ObjectGizmoMath.Translate(target.Before, delta),
                ObjectGizmoMode.Rotate => ObjectGizmoMath.Rotate(target.Before, _startFrame.Pivot, _axis, _angle),
                _ => ObjectGizmoMath.Scale(target.Before, _startFrame.Pivot, factor, _scaleLocked)
            };
            apply(target.Target, after);
        }
    }

    internal static int AxisIndex(GizmoHandle handle) => handle switch
    {
        >= GizmoHandle.X and <= GizmoHandle.Z => (int)handle - (int)GizmoHandle.X,
        >= GizmoHandle.RotateX and <= GizmoHandle.RotateZ => (int)handle - (int)GizmoHandle.RotateX,
        >= GizmoHandle.ScaleX and <= GizmoHandle.ScaleZ => (int)handle - (int)GizmoHandle.ScaleX,
        _ => -1
    };
    internal static ObjectGizmoMode ResolveOperation(ObjectGizmoMode mode, GizmoHandle handle) =>
        handle == GizmoHandle.ViewRotate || handle is >= GizmoHandle.RotateX and <= GizmoHandle.RotateZ ? ObjectGizmoMode.Rotate :
        GizmoView.IsScale(handle) ? ObjectGizmoMode.Scale : mode == ObjectGizmoMode.Transform ? ObjectGizmoMode.Move : mode;

    public void Cancel()
    {
        if (IsDragging)
            foreach (var target in _targets) apply(target.Target, target.Before);
        _targets = [];
        IsDragging = IsVisible = false;
        Feedback = default;
        Handle = GizmoHandle.None;
    }
    private void Commit()
    {
        var entries = _targets.Select(target => new ObjectTransformEdit.Entry(target.Target, target.Before, PlacementTransform.Capture(target.Target)))
            .Where(entry => entry.Before != entry.After).ToArray();
        if (entries.Length > 0) _completed.Enqueue(new ObjectTransformEdit(entries, Operation));
        _targets = [];
        IsDragging = false;
        Feedback = default;
    }
}
