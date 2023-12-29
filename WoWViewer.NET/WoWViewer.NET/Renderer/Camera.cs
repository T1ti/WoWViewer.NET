using System.Numerics;

public class Camera
{
    public Vector3 Position { get; set; }
    public Vector3 Front { get; set; }
    public Vector3 Up { get; private set; }
    public float AspectRatio { get; set; }

    public float Yaw { get; set; } = 0f;
    public float Pitch { get; set; }
    public float Roll { get; set; }

    public Vector3 Direction = new();

    private float Zoom = 45f;

    public Camera(Vector3 position, Vector3 front, Vector3 up, float aspectRatio)
    {
        Position = position;
        AspectRatio = aspectRatio;
        Front = front;
        Up = up;
    }

    public void ModifyZoom(float zoomAmount)
    {
        //We don't want to be able to zoom in too close or too far away so clamp to these values
        Zoom = Math.Clamp(Zoom - zoomAmount, 1.0f, 45f);
    }

    public void ModifyDirection(float xOffset, float yOffset)
    {
        Yaw += yOffset;
        Pitch -= xOffset;

        Direction.X = MathF.Cos(DegreesToRadians(Yaw)) * MathF.Cos(DegreesToRadians(Pitch));
        Direction.Y = MathF.Sin(DegreesToRadians(Pitch));
        Direction.Z = MathF.Sin(DegreesToRadians(Yaw)) * MathF.Cos(DegreesToRadians(Pitch));

        Front = Vector3.Normalize(Direction);
    }

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Position + Front, Up);
    }

    public Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(DegreesToRadians(Zoom), AspectRatio, 0.1f, 4096.0f);
    }
    public static float DegreesToRadians(float degrees)
    {
        return MathF.PI / 180f * degrees;
    }
}