using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Raycasting;

namespace WoWRenderLib.DX11.Editing;

public enum ObjectGizmoMode { Move, Rotate, Scale, Transform }
public enum ObjectGizmoOrientation { Global, Local, View }
internal enum GizmoHandle
{
    None, X, Y, Z, Center, XY, YZ, ZX, ViewRotate,
    RotateX, RotateY, RotateZ, ScaleX, ScaleY, ScaleZ, ScaleCenter,
    IndicatorLine, IndicatorFill
}

public readonly record struct PlacementTransform(Vector3 Position, Vector3 Rotation, float Scale)
{
    public static PlacementTransform Capture(Container3D item) => new(item.Position, item.Rotation, item.Scale);
}

internal readonly record struct GizmoFrame(Vector3 Pivot, Matrix4x4 Basis, float Size)
{
    public Matrix4x4 World => Matrix4x4.CreateScale(Size) * Basis * Matrix4x4.CreateTranslation(Pivot);
    public Vector3 Axis(int index) => Vector3.TransformNormal(ObjectGizmoMath.UnitAxis(index), Basis);
}

/// <summary>All calculations use renderer world space (Z up), never placement-file axes.</summary>
internal static class ObjectGizmoMath
{
    public static Vector3 UnitAxis(int index) => index switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
    public static Vector3 ToWorldPosition(Vector3 placement) => new(-placement.Z, placement.X, placement.Y);
    public static Vector3 ToPlacementPosition(Vector3 world) => new(world.Y, world.Z, -world.X);

    public static Matrix4x4 RotationMatrix(Vector3 degrees) =>
        Matrix4x4.CreateRotationX(degrees.Z * MathF.PI / 180f) *
        Matrix4x4.CreateRotationY(degrees.X * MathF.PI / 180f) *
        Matrix4x4.CreateRotationZ((degrees.Y + 180f) * MathF.PI / 180f);

    public static Vector3 PlacementRotation(Matrix4x4 rotation)
    {
        var y = MathF.Asin(Math.Clamp(-rotation.M13, -1f, 1f));
        var regular = MathF.Abs(MathF.Cos(y)) > 0.0001f;
        var x = regular ? MathF.Atan2(rotation.M23, rotation.M33) : MathF.Atan2(-rotation.M32, rotation.M22);
        var z = regular ? MathF.Atan2(rotation.M12, rotation.M11) : 0f;
        return new Vector3(y * 180f / MathF.PI, z * 180f / MathF.PI - 180f, x * 180f / MathF.PI);
    }

    public static Matrix4x4 ViewBasis(Camera camera)
    {
        var x = camera.Right;
        var z = -Vector3.Normalize(camera.Front);
        var y = Vector3.Normalize(Vector3.Cross(z, x));
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    public static bool TryCreateFrame(IReadOnlyList<Container3D> selection, ObjectGizmoOrientation orientation,
        Camera camera, int height, float pixelScale, out GizmoFrame frame)
    {
        frame = default;
        if (selection.Count == 0 || height <= 0)
            return false;
        var pivot = Vector3.Zero;
        for (var index = 0; index < selection.Count; index++)
            pivot += ToWorldPosition(selection[index].Position) / selection.Count;
        var depth = Vector3.Dot(pivot - camera.Position, camera.Front);
        if (!float.IsFinite(depth) || depth <= 1.1f)
            return false;
        var basis = orientation switch
        {
            ObjectGizmoOrientation.Local => RotationMatrix(selection[^1].Rotation),
            ObjectGizmoOrientation.View => ViewBasis(camera),
            _ => Matrix4x4.Identity
        };
        var size = 180f * pixelScale * depth / (height * camera.GetProjectionMatrix().M22);
        frame = new GizmoFrame(pivot, basis, size);
        return true;
    }

    public static bool Project(Vector3 world, Matrix4x4 viewProjection, Vector2 viewport, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), viewProjection);
        screen = default;
        if (clip.W <= 0.001f || clip.Z < 0 || !float.IsFinite(clip.W))
            return false;
        screen = new Vector2((clip.X / clip.W + 1) * viewport.X * .5f, (1 - clip.Y / clip.W) * viewport.Y * .5f);
        return true;
    }

    public static float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
    {
        var line = b - a;
        var t = line.LengthSquared() > .0001f ? Math.Clamp(Vector2.Dot(point - a, line) / line.LengthSquared(), 0, 1) : 0;
        return Vector2.Distance(point, a + line * t);
    }

    public static bool IntersectPlane(Ray ray, Vector3 origin, Vector3 normal, out Vector3 point)
    {
        point = default;
        var denominator = Vector3.Dot(ray.Direction, normal);
        if (MathF.Abs(denominator) < .0001f)
            return false;
        var distance = Vector3.Dot(origin - ray.Origin, normal) / denominator;
        if (!float.IsFinite(distance) || distance < 0)
            return false;
        point = ray.Origin + ray.Direction * distance;
        return true;
    }

    public static PlacementTransform Translate(PlacementTransform before, Vector3 delta) =>
        before with { Position = before.Position + ToPlacementPosition(delta) };

    public static PlacementTransform Rotate(PlacementTransform before, Vector3 pivot, Vector3 axis, float angle)
    {
        if (MathF.Abs(angle) < .000001f)
            return before;
        var rotation = Matrix4x4.CreateFromAxisAngle(axis, angle);
        return before with
        {
            Position = ToPlacementPosition(pivot + Vector3.TransformNormal(ToWorldPosition(before.Position) - pivot, rotation)),
            Rotation = PlacementRotation(RotationMatrix(before.Rotation) * rotation)
        };
    }

    public static PlacementTransform Scale(PlacementTransform before, Vector3 pivot, float factor, bool locked) =>
        locked || MathF.Abs(factor - 1) < .000001f ? before : before with
        {
            Position = ToPlacementPosition(pivot + (ToWorldPosition(before.Position) - pivot) * factor),
            Scale = Math.Clamp(before.Scale * factor, .001f, 65535f / 1024f)
        };
}
