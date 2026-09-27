using System.Numerics;

namespace WoWRenderLib.DX11.Editing;

/// <summary>Shared camera-facing geometry and visibility rules for drawing and picking.</summary>
internal readonly record struct GizmoView(Matrix4x4 InverseViewProjection, Matrix4x4 ScreenToLocal, Vector3 CameraLocal)
{
    public static GizmoView Create(GizmoFrame frame, Matrix4x4 viewProjection)
    {
        Matrix4x4.Invert(viewProjection, out var inverse);
        static Vector3 Divide(Vector4 v) => new Vector3(v.X, v.Y, v.Z) / v.W;
        var camera = Divide(Vector4.Transform(new Vector4(0, 0, 1, 0), inverse));
        var center = Divide(Vector4.Transform(new Vector4(0, 0, 0, 1), inverse));
        var right = Vector3.Normalize(Divide(Vector4.Transform(new Vector4(1, 0, 0, 1), inverse)) - center);
        var up = Vector3.Normalize(Divide(Vector4.Transform(new Vector4(0, 1, 0, 1), inverse)) - center);
        var normal = Vector3.Normalize(Vector3.Cross(right, up));
        var screen = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
            normal.X, normal.Y, normal.Z, 0, 0, 0, 0, 1);
        var inverseBasis = Matrix4x4.Transpose(frame.Basis);
        return new(inverse, screen * inverseBasis,
            Vector3.Normalize(Vector3.TransformNormal(camera - frame.Pivot, inverseBasis)));
    }

    public Vector3 Vertex(Vector3 position, GizmoHandle handle) =>
        IsScreenAligned(handle) ? Vector3.TransformNormal(position, ScreenToLocal) : position;

    public static bool IsScreenAligned(GizmoHandle handle) => handle is GizmoHandle.ViewRotate or GizmoHandle.ScaleCenter;
    public static bool IsScale(GizmoHandle handle) => handle is >= GizmoHandle.ScaleX and <= GizmoHandle.ScaleCenter;
    public static bool IsAxisRing(GizmoHandle handle, ObjectGizmoMode mode) =>
        handle is >= GizmoHandle.RotateX and <= GizmoHandle.RotateZ ||
        mode == ObjectGizmoMode.Rotate && handle is >= GizmoHandle.X and <= GizmoHandle.Z;
    public bool IsVisible(Vector3 localPosition, GizmoHandle handle, ObjectGizmoMode mode, bool scaleEnabled) =>
        (scaleEnabled || !IsScale(handle)) &&
        (!IsAxisRing(handle, mode) || Vector3.Dot(localPosition, CameraLocal) >= 0);

    public Vector3 Unproject(Vector2 pointer, Vector2 viewport, float depth)
    {
        var point = Vector4.Transform(new Vector4(pointer.X / viewport.X * 2 - 1,
            1 - pointer.Y / viewport.Y * 2, depth, 1), InverseViewProjection);
        return new Vector3(point.X, point.Y, point.Z) / point.W;
    }
}
