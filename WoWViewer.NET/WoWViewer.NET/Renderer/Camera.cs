using System.Numerics;
using WoWViewer.NET.Raycasting;

public class Camera
{
    public Vector3 Position { get; set; }
    public Vector3 Front { get; set; }
    public Vector3 Up { get; private set; }
    public float AspectRatio { get; set; }

    public float Yaw { get; set; } = 0f;
    public float Pitch { get; set; } = 0f;
    public float Roll { get; set; } = 0f;

    public Vector3 Direction = new();

    private float Zoom = 45f;

    private readonly WoWViewer.NET.Renderer.Frustum _frustum = new();

    public Camera(Vector3 position, Vector3 front, Vector3 up, float aspectRatio)
    {
        Position = position;
        AspectRatio = aspectRatio;
        Front = front;
        Up = up;
        Pitch = MathF.Asin(Front.Z) * 180f / MathF.PI;
        Yaw = MathF.Atan2(Front.Y, Front.X) * 180f / MathF.PI;
    }

    public void ModifyZoom(float zoomAmount)
    {
        //We don't want to be able to zoom in too close or too far away so clamp to these values
        Zoom = Math.Clamp(Zoom - zoomAmount, 1.0f, 45f);
    }

    public void ModifyDirection(float xOffset, float yOffset)
    {
        Yaw -= xOffset;
        Pitch -= yOffset;

        // clamp view angles (gimbal lock?)
        Pitch = Math.Clamp(Pitch, -89.0f, 89.0f);

        Direction.X = MathF.Cos(DegreesToRadians(Yaw)) * MathF.Cos(DegreesToRadians(Pitch));
        Direction.Y = MathF.Sin(DegreesToRadians(Yaw)) * MathF.Cos(DegreesToRadians(Pitch));
        Direction.Z = -MathF.Sin(DegreesToRadians(Pitch));

        Front = Vector3.Normalize(Direction);

        var worldUp = Vector3.UnitZ * -1; // wow coordinate system fix
        var right = Math.Abs(Vector3.Dot(Front, worldUp)) > 0.999f ? Vector3.Normalize(Vector3.Cross(worldUp, Vector3.UnitX)) : Vector3.Normalize(Vector3.Cross(worldUp, Front));

        Up = Vector3.Normalize(Vector3.Cross(Front, right));
    }

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Position + Front, Up);
    }

    public Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(DegreesToRadians(Zoom), AspectRatio, 10.0f, 4096.0f);
    }

    public Ray GetRayFromScreen(float screenX, float screenY, int screenWidth, int screenHeight)
    {
        //Console.WriteLine("ScreenX: " + screenX + " ScreenY: " + screenY);
        (float x, float y) = ((2.0f * screenX) / screenWidth - 1.0f, 1.0f - (2.0f * screenY) / screenHeight);

       // Console.WriteLine("Normalized Device Coordinates: " + x + ", " + y);

        var clipCoords = new Vector4(x, y, -1.0f, 1.0f);

        // invert the projection matrix
        Matrix4x4.Invert(GetProjectionMatrix(), out var invProjection);

        // get eye coords by multiplying clip coords with inverse projection matrix
        var eyeCoords = Vector4.Transform(clipCoords, invProjection);
        eyeCoords = new Vector4(eyeCoords.X, eyeCoords.Y, -1.0f, 0.0f);

        //Console.WriteLine("Eye Coords: " + eyeCoords);

        // Apply the same 180° rotation that's used in rendering
        var viewMatrix = GetViewMatrix();
        viewMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);
        Matrix4x4.Invert(viewMatrix, out var invView);

        // get world coords by multiplying eye coords with inverse view matrix
        var worldCoords = Vector4.Transform(eyeCoords, invView);
        var rayDirection = Vector3.Normalize(new Vector3(worldCoords.X, worldCoords.Y, worldCoords.Z));

        //Console.WriteLine("world Coords: " + worldCoords);

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

    public WoWViewer.NET.Renderer.Frustum GetFrustum()
    {
        return _frustum;
    }
}