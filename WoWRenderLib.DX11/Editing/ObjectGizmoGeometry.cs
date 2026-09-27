using System.Numerics;

namespace WoWRenderLib.DX11.Editing;

/// <summary>Immutable handle meshes and matching pick segments, built once for all viewports.</summary>
internal sealed class ObjectGizmoGeometry
{
    private readonly record struct PickSegment(Vector3 A, Vector3 B, GizmoHandle Handle);
    private readonly List<PickSegment> _segments = [];
    public Vector4[] Vertices { get; private set; } = [];
    public static ObjectGizmoGeometry Move { get; } = new(ObjectGizmoMode.Move);
    public static ObjectGizmoGeometry Rotate { get; } = new(ObjectGizmoMode.Rotate);
    public static ObjectGizmoGeometry Scale { get; } = new(ObjectGizmoMode.Scale);
    public static ObjectGizmoGeometry Transform { get; } = new(ObjectGizmoMode.Transform);
    public static ObjectGizmoGeometry For(ObjectGizmoMode mode) => mode switch
    {
        ObjectGizmoMode.Rotate => Rotate,
        ObjectGizmoMode.Scale => Scale,
        ObjectGizmoMode.Transform => Transform,
        _ => Move
    };

    private ObjectGizmoGeometry(ObjectGizmoMode mode)
    {
        var vertices = new List<Vector4>();
        for (var axis = 0; axis < 3; axis++)
        {
            var direction = ObjectGizmoMath.UnitAxis(axis);
            var handle = (GizmoHandle)(axis + 1);
            if (mode is ObjectGizmoMode.Rotate or ObjectGizmoMode.Transform)
            {
                var ringHandle = mode == ObjectGizmoMode.Transform ? (GizmoHandle)((int)GizmoHandle.RotateX + axis) : handle;
                var u = ObjectGizmoMath.UnitAxis((axis + 1) % 3);
                var v = ObjectGizmoMath.UnitAxis((axis + 2) % 3);
                for (var segment = 0; segment < 72; segment++)
                {
                    var a = (u * MathF.Cos(segment * MathF.Tau / 72) + v * MathF.Sin(segment * MathF.Tau / 72)) * .85f;
                    var b = (u * MathF.Cos((segment + 1) * MathF.Tau / 72) + v * MathF.Sin((segment + 1) * MathF.Tau / 72)) * .85f;
                    Tube(a, b, .017f, .017f, ringHandle);
                    _segments.Add(new(a, b, ringHandle));
                }
            }
            if (mode != ObjectGizmoMode.Rotate)
            {
                var length = mode == ObjectGizmoMode.Transform ? 1.2f : 1f;
                Tube(direction * .17f, direction * (length - .18f), .015f, .015f, handle);
                if (mode is ObjectGizmoMode.Move or ObjectGizmoMode.Transform)
                    Tube(direction * (length - .22f), direction * length, .07f, 0, handle);
                else
                    Cube(direction * .92f, .065f, handle);
                _segments.Add(new(direction * .18f, direction * length, handle));
                if (mode == ObjectGizmoMode.Transform)
                {
                    var scaleHandle = (GizmoHandle)((int)GizmoHandle.ScaleX + axis);
                    Cube(direction * .62f, .065f, scaleHandle);
                    _segments.Add(new(direction * .60f, direction * .64f, scaleHandle));
                }
            }
        }
        if (mode != ObjectGizmoMode.Rotate)
            Cube(Vector3.Zero, mode == ObjectGizmoMode.Scale ? .09f : .055f, GizmoHandle.Center);
        if (mode is ObjectGizmoMode.Move or ObjectGizmoMode.Transform)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                var u = ObjectGizmoMath.UnitAxis(axis);
                var v = ObjectGizmoMath.UnitAxis((axis + 1) % 3);
                var handle = (GizmoHandle)((int)GizmoHandle.XY + axis);
                Quad(u * .23f + v * .23f, u * .43f + v * .23f, (u + v) * .43f, u * .23f + v * .43f, handle);
            }
        }
        if (mode is ObjectGizmoMode.Rotate or ObjectGizmoMode.Transform)
        {
            var radius = mode == ObjectGizmoMode.Transform ? 1.38f : 1.08f;
            for (var segment = 0; segment < 96; segment++)
            {
                var a = new Vector3(MathF.Cos(segment * MathF.Tau / 96), MathF.Sin(segment * MathF.Tau / 96), 0) * radius;
                var b = new Vector3(MathF.Cos((segment + 1) * MathF.Tau / 96), MathF.Sin((segment + 1) * MathF.Tau / 96), 0) * radius;
                Tube(a, b, .012f, .012f, GizmoHandle.ViewRotate);
                _segments.Add(new(a, b, GizmoHandle.ViewRotate));
            }
        }
        if (mode == ObjectGizmoMode.Transform)
        {
            var center = new Vector3(-.22f, -.22f, 0);
            Cube(center, .055f, GizmoHandle.ScaleCenter);
            _segments.Add(new(center, center, GizmoHandle.ScaleCenter));
        }
        Vertices = vertices.ToArray();

        void Triangle(Vector3 a, Vector3 b, Vector3 c, GizmoHandle handle)
        {
            vertices.Add(new(a, (float)handle));
            vertices.Add(new(b, (float)handle));
            vertices.Add(new(c, (float)handle));
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, GizmoHandle handle)
        {
            Triangle(a, b, c, handle);
            Triangle(a, c, d, handle);
        }
        void Tube(Vector3 a, Vector3 b, float radiusA, float radiusB, GizmoHandle handle)
        {
            var direction = Vector3.Normalize(b - a);
            var u = Vector3.Normalize(Vector3.Cross(direction, MathF.Abs(direction.Z) < .9f ? Vector3.UnitZ : Vector3.UnitY));
            var v = Vector3.Cross(direction, u);
            for (var side = 0; side < 8; side++)
            {
                var p = u * MathF.Cos(side * MathF.Tau / 8) + v * MathF.Sin(side * MathF.Tau / 8);
                var q = u * MathF.Cos((side + 1) * MathF.Tau / 8) + v * MathF.Sin((side + 1) * MathF.Tau / 8);
                Quad(a + p * radiusA, b + p * radiusB, b + q * radiusB, a + q * radiusA, handle);
                Triangle(a, a + q * radiusA, a + p * radiusA, handle);
            }
        }
        void Cube(Vector3 center, float radius, GizmoHandle handle)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                var n = ObjectGizmoMath.UnitAxis(axis) * radius;
                var u = ObjectGizmoMath.UnitAxis((axis + 1) % 3) * radius;
                var v = ObjectGizmoMath.UnitAxis((axis + 2) % 3) * radius;
                Quad(center + n - u - v, center + n + u - v, center + n + u + v, center + n - u + v, handle);
                Quad(center - n - u - v, center - n + u - v, center - n + u + v, center - n - u + v, handle);
            }
        }
    }

    public GizmoHandle HitTest(GizmoFrame frame, ObjectGizmoMode mode, Vector2 pointer,
        Matrix4x4 viewProjection, Vector2 viewport, float pixelScale, out Vector3 hitPoint, bool scaleEnabled = true)
    {
        hitPoint = frame.Pivot;
        var view = GizmoView.Create(frame, viewProjection);
        Matrix4x4.Invert(frame.World, out var inverseWorld);
        var origin = Vector3.Transform(view.Unproject(pointer, viewport, 0), inverseWorld);
        var far = Vector3.Transform(view.Unproject(pointer, viewport, .99f), inverseWorld);
        var direction = Vector3.Normalize(far - origin);
        var closestDepth = float.MaxValue;
        var result = GizmoHandle.None;
        // Test actual triangles first, by depth. A line's forgiving pick radius must never
        // steal a click on the solid face of a different, visible handle.
        for (var index = 0; index < Vertices.Length; index += 3)
        {
            var handle = (GizmoHandle)Vertices[index].W;
            if (!scaleEnabled && GizmoView.IsScale(handle))
                continue;
            var a = Local(Vertices[index], handle);
            var b = Local(Vertices[index + 1], handle);
            var c = Local(Vertices[index + 2], handle);
            if (!IntersectTriangle(origin, direction, a, b, c, out var depth) || depth >= closestDepth)
                continue;
            var point = origin + direction * depth;
            if (!view.IsVisible(point, handle, mode, scaleEnabled))
                continue;
            closestDepth = depth;
            result = handle;
            hitPoint = Vector3.Transform(point, frame.World);
        }
        if (result != GizmoHandle.None)
            return result;

        var closest = 7f * pixelScale;
        foreach (var segment in _segments)
        {
            if (!scaleEnabled && GizmoView.IsScale(segment.Handle))
                continue;
            var localA = view.Vertex(segment.A, segment.Handle);
            var localB = view.Vertex(segment.B, segment.Handle);
            if (GizmoView.IsAxisRing(segment.Handle, mode))
            {
                var da = Vector3.Dot(localA, view.CameraLocal);
                var db = Vector3.Dot(localB, view.CameraLocal);
                if (da < 0 && db < 0) continue;
                if (da < 0) localA = Vector3.Lerp(localA, localB, da / (da - db));
                else if (db < 0) localB = Vector3.Lerp(localA, localB, da / (da - db));
            }
            var worldA = Vector3.Transform(localA, frame.World);
            var worldB = Vector3.Transform(localB, frame.World);
            if (!ObjectGizmoMath.Project(worldA, viewProjection, viewport, out var a) ||
                !ObjectGizmoMath.Project(worldB, viewProjection, viewport, out var b))
                continue;
            var distance = ObjectGizmoMath.SegmentDistance(pointer, a, b);
            if (distance >= closest) continue;
            closest = distance;
            result = segment.Handle;
            hitPoint = (worldA + worldB) * .5f;
        }
        if (result == GizmoHandle.None && mode != ObjectGizmoMode.Rotate &&
            ObjectGizmoMath.Project(frame.Pivot, viewProjection, viewport, out var center) &&
            Vector2.Distance(pointer, center) <= 7 * pixelScale)
            return GizmoHandle.Center;
        return result;

        Vector3 Local(Vector4 vertex, GizmoHandle handle) => view.Vertex(new(vertex.X, vertex.Y, vertex.Z), handle);
    }

    internal static bool IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float depth)
    {
        depth = 0;
        var ab = b - a;
        var ac = c - a;
        var p = Vector3.Cross(direction, ac);
        var determinant = Vector3.Dot(ab, p);
        if (MathF.Abs(determinant) < 1e-8f) return false;
        var t = origin - a;
        var u = Vector3.Dot(t, p) / determinant;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(t, ab);
        var v = Vector3.Dot(direction, q) / determinant;
        if (v < 0 || u + v > 1) return false;
        depth = Vector3.Dot(ac, q) / determinant;
        return depth >= 0;
    }
}
