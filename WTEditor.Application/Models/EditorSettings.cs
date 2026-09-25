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
    public bool IsForegroundFrameRateLimitEnabled { get; init; }
    public bool IsForegroundFrameRateLimitInitialized { get; init; }
    public int ViewportFrameRateLimit { get; init; } = 60;
    public Vector3 AmbientColor { get; init; } = new(104f / 255f, 130f / 255f, 154f / 255f);
    public Vector3 DiffuseColor { get; init; } = new(1f, 136f / 255f, 0f);
    public float TerrainRenderDistance { get; init; } = 20_000f;
    public float ModelRenderDistance { get; init; } = 20_000f;
    public float AnimationRenderDistancePercent { get; init; } = 50f;
    public float ParticleRenderDistancePercent { get; init; } = 20f;
    public float MinimumModelScreenSizePixels { get; init; } = 1f;
    public float TerrainLodTransitionPixels { get; init; } = 32f;
    public int TileLoadingDistance { get; init; } = 4;
    public int WorldLightingTime { get; init; } = 1440;
    public bool UseLocalWorldLightingTime { get; init; }
    public float MovementSpeed { get; init; } = 150f;
    public float MouseSensitivity { get; init; } = 0.1f;
    public bool RenderADT { get; init; } = true;
    public bool RenderLiquid { get; init; } = true;
    public bool RenderWMO { get; init; } = true;
    public bool RenderM2 { get; init; } = true;
    public bool RenderParticles { get; init; } = true;
    public bool DisableScreenGlow { get; init; }
    public bool AnimateModels { get; init; } = true;
    public bool EnableWmoPortalCulling { get; init; }
    public bool ShowBoundingBoxes { get; init; }
    public bool ShowBoundingSpheres { get; init; }
    public bool ShowTerrainGrid { get; init; }
    public bool ShowTerrainWireframe { get; init; }

    public RenderingConfiguration Normalize()
    {
        var defaults = new RenderingConfiguration();
        return this with
        {
            ViewportFrameRateLimit = Math.Clamp(ViewportFrameRateLimit, 30, 360),
            AmbientColor = ClampColor(AmbientColor, defaults.AmbientColor),
            DiffuseColor = ClampColor(DiffuseColor, defaults.DiffuseColor),
            TerrainRenderDistance = ClampFinite(
                TerrainRenderDistance, 100f, 1_000_000f, defaults.TerrainRenderDistance),
            ModelRenderDistance = ClampFinite(
                ModelRenderDistance, 100f, 1_000_000f, defaults.ModelRenderDistance),
            AnimationRenderDistancePercent = ClampFinite(
                AnimationRenderDistancePercent, 0f, 100f, defaults.AnimationRenderDistancePercent),
            ParticleRenderDistancePercent = ClampFinite(
                ParticleRenderDistancePercent, 0f, 100f, defaults.ParticleRenderDistancePercent),
            MinimumModelScreenSizePixels = ClampFinite(
                MinimumModelScreenSizePixels, 0f, 16f, defaults.MinimumModelScreenSizePixels),
            TerrainLodTransitionPixels = ClampFinite(
                TerrainLodTransitionPixels, 0f, 256f, defaults.TerrainLodTransitionPixels),
            TileLoadingDistance = Math.Clamp(TileLoadingDistance, 0, 32),
            WorldLightingTime = Math.Clamp(WorldLightingTime, 0, 2879),
            MovementSpeed = ClampFinite(MovementSpeed, 1f, 10_000f, defaults.MovementSpeed),
            MouseSensitivity = ClampFinite(
                MouseSensitivity, 0.001f, 2f, defaults.MouseSensitivity)
        };
    }

    private static float ClampFinite(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static Vector3 ClampColor(Vector3 color, Vector3 fallback) => new(
        ClampFinite(color.X, 0f, 4f, fallback.X),
        ClampFinite(color.Y, 0f, 4f, fallback.Y),
        ClampFinite(color.Z, 0f, 4f, fallback.Z));
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
