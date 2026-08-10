using System.Numerics;
using WoWRenderLib.Raycasting;

public class Camera
{
    public Vector3 Position { get; set; }
    public Vector3 Front { get; set; }
    public Vector3 Up { get; private set; }
    public Vector3 Right { get; private set; }
    public float AspectRatio { get; set; }
    public float FarPlane { get; set; } = 20_000f;

    public float Yaw { get; set; } = 0f;
    public float Pitch { get; set; } = 0f;
    public float Roll { get; set; } = 0f;

    public Vector3 Direction = new();

    private float Zoom = 45f;

    private readonly WoWRenderLib.DX11.Renderer.Frustum _frustum = new();

    public Camera(Vector3 position, float yaw, float pitch, float aspectRatio)
    {
        Position = position;
        AspectRatio = aspectRatio;
        Up = Vector3.UnitY;
        UpdateVectors();
    }

    public void ModifyZoom(float zoomAmount)
    {
        Zoom = Math.Clamp(Zoom - zoomAmount, 1.0f, 45f);
    }

    public void ModifyDirection(float xOffset, float yOffset)
    {
        Yaw += xOffset;
        Pitch -= yOffset;
        Pitch = Math.Clamp(Pitch, -89.0f, 89.0f);

        UpdateVectors();
    }

    private void UpdateVectors()
    {
        Front = Vector3.Normalize(new Vector3(MathF.Cos(DegreesToRadians(Pitch)) * MathF.Cos(DegreesToRadians(Yaw)), MathF.Cos(DegreesToRadians(Pitch)) * MathF.Sin(DegreesToRadians(Yaw)), MathF.Sin(DegreesToRadians(Pitch))));
        Up = Vector3.UnitZ;
        Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitZ));
    }

    public Matrix4x4 GetViewMatrix()
    {
        var f = Vector3.Normalize(Front);
        var r = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitZ));
        var u = Vector3.Cross(r, f);

        return new Matrix4x4(
            r.X, u.X, f.X, 0,
            r.Y, u.Y, f.Y, 0,
            r.Z, u.Z, f.Z, 0,
            -Vector3.Dot(r, Position),
            -Vector3.Dot(u, Position),
            -Vector3.Dot(f, Position),
            1
        );
    }

    public Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(
            DegreesToRadians(Zoom), AspectRatio, 1.0f, Math.Clamp(FarPlane, 100f, 1_000_000f));
    }

    public Ray GetRayFromScreen(float screenX, float screenY, int screenWidth, int screenHeight)
    {
        (float x, float y) = ((2.0f * screenX) / screenWidth - 1.0f, 1.0f - (2.0f * screenY) / screenHeight);

        var clipCoords = new Vector4(x, y, 1.0f, 1.0f);

        Matrix4x4.Invert(GetProjectionMatrix(), out var invProjection);

        var eyeCoords = Vector4.Transform(clipCoords, invProjection);
        eyeCoords = new Vector4(eyeCoords.X, eyeCoords.Y, 1.0f, 0.0f);

        Matrix4x4.Invert(GetViewMatrix(), out var invView);

        var worldCoords = Vector4.Transform(eyeCoords, invView);
        var rayDirection = Vector3.Normalize(new Vector3(worldCoords.X, worldCoords.Y, worldCoords.Z));

        return new Ray(Position, rayDirection);
    }

    public static float DegreesToRadians(float degrees)
    {
        return MathF.PI / 180f * degrees;
    }

    public void UpdateFrustum()
    {
        var viewProjection = GetViewMatrix() * GetProjectionMatrix();
        _frustum.ExtractFromMatrix(viewProjection);
    }

    public WoWRenderLib.DX11.Renderer.Frustum GetFrustum()
    {
        return _frustum;
    }
}
