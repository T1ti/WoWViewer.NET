using System.Numerics;

namespace WoWRenderLib.DX11.Editing;

/// <summary>Reusable world-space gesture guides. They are visual only and never participate in picking.</summary>
internal sealed class GizmoDragGeometry
{
    public List<Vector4> Vertices { get; } = new(2048);

    public void Build(GizmoDragVisual drag, Matrix4x4 viewProjection)
    {
        Vertices.Clear();
        var frame = drag.Start;
        var view = GizmoView.Create(frame, viewProjection);
        var right = Vector3.TransformNormal(Vector3.TransformNormal(Vector3.UnitX, view.ScreenToLocal), frame.Basis);
        var up = Vector3.TransformNormal(Vector3.TransformNormal(Vector3.UnitY, view.ScreenToLocal), frame.Basis);
        var width = frame.Size * .009f;
        if (drag.Operation == ObjectGizmoMode.Rotate)
        {
            var radial = drag.Radial * frame.Size * drag.RotationRadius;
            var tangent = Vector3.Cross(drag.Axis, radial);
            var angle = Math.Clamp(drag.Angle, -MathF.Tau, MathF.Tau);
            var segments = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(angle) * 20));
            var previous = frame.Pivot + radial;
            for (var index = 1; index <= segments; index++)
            {
                var step = angle * index / segments;
                var current = frame.Pivot + radial * MathF.Cos(step) + tangent * MathF.Sin(step);
                Triangle(frame.Pivot, previous, current, GizmoHandle.IndicatorFill);
                Line(previous, current);
                previous = current;
            }
            Line(frame.Pivot, frame.Pivot + radial);
            Line(frame.Pivot, previous);
        }
        else if (drag.Operation == ObjectGizmoMode.Move)
        {
            Line(frame.Pivot, drag.Current.Pivot);
            Marker(frame.Pivot);
            Marker(drag.Current.Pivot);
        }
        else
        {
            var axis = drag.Axis == Vector3.Zero ? right : drag.Axis;
            var start = frame.Pivot + axis * frame.Size;
            var end = frame.Pivot + axis * frame.Size * drag.Scale;
            Line(frame.Pivot, end);
            Marker(start);
            Marker(end);
        }

        void Marker(Vector3 position)
        {
            var radius = frame.Size * .055f;
            for (var index = 0; index < 32; index++)
            {
                var a = index * MathF.Tau / 32;
                var b = (index + 1) * MathF.Tau / 32;
                Line(position + (right * MathF.Cos(a) + up * MathF.Sin(a)) * radius,
                    position + (right * MathF.Cos(b) + up * MathF.Sin(b)) * radius);
            }
        }
        void Line(Vector3 a, Vector3 b)
        {
            var direction = b - a;
            if (direction.LengthSquared() < 1e-10f) return;
            var normal = Vector3.Cross(direction, Vector3.Cross(right, up));
            if (normal.LengthSquared() < 1e-10f) normal = right;
            normal = Vector3.Normalize(normal) * width;
            Triangle(a - normal, a + normal, b + normal, GizmoHandle.IndicatorLine);
            Triangle(a - normal, b + normal, b - normal, GizmoHandle.IndicatorLine);
        }
    }

    private void Triangle(Vector3 a, Vector3 b, Vector3 c, GizmoHandle handle)
    {
        Vertices.Add(new(a, (float)handle));
        Vertices.Add(new(b, (float)handle));
        Vertices.Add(new(c, (float)handle));
    }
}
