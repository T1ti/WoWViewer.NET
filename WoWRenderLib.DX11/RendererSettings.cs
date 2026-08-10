namespace WoWRenderLib.DX11;

public sealed class RendererSettings
{
    public float RenderDistance { get; set; } = 20_000f;
    public int TileLoadingDistance { get; set; } = 4;
    public float MovementSpeed { get; set; } = 150f;
    public float MouseSensitivity { get; set; } = 0.1f;

    public bool RenderADT { get; set; } = true;
    public bool RenderWMO { get; set; } = true;
    public bool RenderM2 { get; set; } = true;
    public bool ShowBoundingBoxes { get; set; }
    public bool ShowBoundingSpheres { get; set; }

    public RendererSettings Clone() => new()
    {
        RenderDistance = RenderDistance,
        TileLoadingDistance = TileLoadingDistance,
        MovementSpeed = MovementSpeed,
        MouseSensitivity = MouseSensitivity,
        RenderADT = RenderADT,
        RenderWMO = RenderWMO,
        RenderM2 = RenderM2,
        ShowBoundingBoxes = ShowBoundingBoxes,
        ShowBoundingSpheres = ShowBoundingSpheres
    };
}
