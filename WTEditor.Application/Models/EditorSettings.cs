using System.Numerics;

namespace WTEditor.Application.Models;

public enum KeyboardLayoutMode
{
    Auto,
    Qwerty,
    Azerty
}

public sealed record ClientConfiguration
{
    public string WowDirectory { get; init; } = @"C:\Program Files (x86)\World of Warcraft";
    public string WowProduct { get; init; } = "wow_classic_era";
    public string BuildConfig { get; init; } = "";
    public string CdnConfig { get; init; } = "";

    public ClientConfiguration Normalize() => this with
    {
        WowDirectory = WowDirectory.Trim(),
        WowProduct = WowProduct.Trim(),
        BuildConfig = BuildConfig.Trim(),
        CdnConfig = CdnConfig.Trim()
    };
}

public sealed record RenderingConfiguration
{
    public Vector3 AmbientColor { get; init; } = new(104f / 255f, 130f / 255f, 154f / 255f);
    public Vector3 DiffuseColor { get; init; } = new(1f, 136f / 255f, 0f);
    public float TerrainRenderDistance { get; init; } = 20_000f;
    public float ModelRenderDistance { get; init; } = 20_000f;
    public float MinimumModelScreenSizePixels { get; init; } = 1f;
    public float TerrainLodTransitionPixels { get; init; } = 32f;
    public int TileLoadingDistance { get; init; } = 4;
    public float MovementSpeed { get; init; } = 150f;
    public float MouseSensitivity { get; init; } = 0.1f;
    public bool RenderADT { get; init; } = true;
    public bool RenderWMO { get; init; } = true;
    public bool RenderM2 { get; init; } = true;
    public bool EnableWmoPortalCulling { get; init; }
    public bool ShowBoundingBoxes { get; init; }
    public bool ShowBoundingSpheres { get; init; }

    public RenderingConfiguration Normalize() => this with
    {
        AmbientColor = ClampColor(AmbientColor),
        DiffuseColor = ClampColor(DiffuseColor),
        TerrainRenderDistance = Math.Clamp(TerrainRenderDistance, 100f, 1_000_000f),
        ModelRenderDistance = Math.Clamp(ModelRenderDistance, 100f, 1_000_000f),
        MinimumModelScreenSizePixels = Math.Clamp(MinimumModelScreenSizePixels, 0f, 16f),
        TerrainLodTransitionPixels = Math.Clamp(TerrainLodTransitionPixels, 0f, 256f),
        TileLoadingDistance = Math.Clamp(TileLoadingDistance, 0, 32),
        MovementSpeed = Math.Clamp(MovementSpeed, 1f, 10_000f),
        MouseSensitivity = Math.Clamp(MouseSensitivity, 0.001f, 2f)
    };

    private static Vector3 ClampColor(Vector3 color) => new(
        Math.Clamp(color.X, 0f, 4f),
        Math.Clamp(color.Y, 0f, 4f),
        Math.Clamp(color.Z, 0f, 4f));
}

public sealed record CameraState(Vector3 Position, Vector3 Direction);

public sealed record WindowPlacement
{
    public bool HasBounds { get; init; }
    public string State { get; init; } = "Normal";
    public int X { get; init; }
    public int Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed record EditorSettingsSnapshot
{
    public ClientConfiguration Client { get; init; } = new();
    public RenderingConfiguration Rendering { get; init; } = new();
    public KeyboardLayoutMode KeyboardLayout { get; init; } = KeyboardLayoutMode.Auto;
    public CameraState? Camera { get; init; }
    public WindowPlacement Window { get; init; } = new();

    public EditorSettingsSnapshot Normalize() => this with
    {
        Client = Client.Normalize(),
        Rendering = Rendering.Normalize()
    };
}
