using System.Numerics;
using System.Text.Json;
using WTEditor.Application.Models;
using WTEditor.Application.Services;

namespace WTEditor.Avalonia.Services;

public sealed class PersistedRenderingSettings
{
    public bool IsForegroundFrameRateLimitEnabled { get; set; }
    public bool IsForegroundFrameRateLimitInitialized { get; set; }
    public int ViewportFrameRateLimit { get; set; } = 60;
    public float AmbientColorR { get; set; } = 104f / 255f;
    public float AmbientColorG { get; set; } = 130f / 255f;
    public float AmbientColorB { get; set; } = 154f / 255f;
    public float DiffuseColorR { get; set; } = 1f;
    public float DiffuseColorG { get; set; } = 136f / 255f;
    public float DiffuseColorB { get; set; }
    public float TerrainRenderDistance { get; set; } = 20_000f;
    public float ModelRenderDistance { get; set; } = 20_000f;
    public float MinimumModelScreenSizePixels { get; set; } = 1f;
    public float TerrainLodTransitionPixels { get; set; } = 32f;
    public int TileLoadingDistance { get; set; } = 4;
    public float MovementSpeed { get; set; } = 150f;
    public float MouseSensitivity { get; set; } = 0.1f;
    public bool RenderADT { get; set; } = true;
    public bool RenderWMO { get; set; } = true;
    public bool RenderM2 { get; set; } = true;
    public bool EnableWmoPortalCulling { get; set; }
    public bool ShowBoundingBoxes { get; set; }
    public bool ShowBoundingSpheres { get; set; }
    public bool ShowTerrainGrid { get; set; }
    public bool ShowTerrainWireframe { get; set; }

    public RenderingConfiguration ToModel() => new()
    {
        IsForegroundFrameRateLimitEnabled = IsForegroundFrameRateLimitEnabled,
        IsForegroundFrameRateLimitInitialized = IsForegroundFrameRateLimitInitialized,
        ViewportFrameRateLimit = ViewportFrameRateLimit,
        AmbientColor = new Vector3(AmbientColorR, AmbientColorG, AmbientColorB),
        DiffuseColor = new Vector3(DiffuseColorR, DiffuseColorG, DiffuseColorB),
        TerrainRenderDistance = TerrainRenderDistance,
        ModelRenderDistance = ModelRenderDistance,
        MinimumModelScreenSizePixels = MinimumModelScreenSizePixels,
        TerrainLodTransitionPixels = TerrainLodTransitionPixels,
        TileLoadingDistance = TileLoadingDistance,
        MovementSpeed = MovementSpeed,
        MouseSensitivity = MouseSensitivity,
        RenderADT = RenderADT,
        RenderWMO = RenderWMO,
        RenderM2 = RenderM2,
        EnableWmoPortalCulling = EnableWmoPortalCulling,
        ShowBoundingBoxes = ShowBoundingBoxes,
        ShowBoundingSpheres = ShowBoundingSpheres,
        ShowTerrainGrid = ShowTerrainGrid,
        ShowTerrainWireframe = ShowTerrainWireframe
    };

    public static PersistedRenderingSettings From(RenderingConfiguration rendering) => new()
    {
        IsForegroundFrameRateLimitEnabled = rendering.IsForegroundFrameRateLimitEnabled,
        IsForegroundFrameRateLimitInitialized = rendering.IsForegroundFrameRateLimitInitialized,
        ViewportFrameRateLimit = rendering.ViewportFrameRateLimit,
        AmbientColorR = rendering.AmbientColor.X,
        AmbientColorG = rendering.AmbientColor.Y,
        AmbientColorB = rendering.AmbientColor.Z,
        DiffuseColorR = rendering.DiffuseColor.X,
        DiffuseColorG = rendering.DiffuseColor.Y,
        DiffuseColorB = rendering.DiffuseColor.Z,
        TerrainRenderDistance = rendering.TerrainRenderDistance,
        ModelRenderDistance = rendering.ModelRenderDistance,
        MinimumModelScreenSizePixels = rendering.MinimumModelScreenSizePixels,
        TerrainLodTransitionPixels = rendering.TerrainLodTransitionPixels,
        TileLoadingDistance = rendering.TileLoadingDistance,
        MovementSpeed = rendering.MovementSpeed,
        MouseSensitivity = rendering.MouseSensitivity,
        RenderADT = rendering.RenderADT,
        RenderWMO = rendering.RenderWMO,
        RenderM2 = rendering.RenderM2,
        EnableWmoPortalCulling = rendering.EnableWmoPortalCulling,
        ShowBoundingBoxes = rendering.ShowBoundingBoxes,
        ShowBoundingSpheres = rendering.ShowBoundingSpheres,
        ShowTerrainGrid = rendering.ShowTerrainGrid,
        ShowTerrainWireframe = rendering.ShowTerrainWireframe
    };
}

public sealed class PersistedEditorSettings
{
    public string WowDirectory { get; set; } = @"C:\Program Files (x86)\World of Warcraft";
    public string WowProduct { get; set; } = "wow_classic_era";
    public string BuildConfig { get; set; } = "";
    public string CdnConfig { get; set; } = "";
    public string KeyboardLayout { get; set; } = "Auto";
    public PersistedRenderingSettings Renderer { get; set; } = new();
    public bool HasCameraPosition { get; set; }
    public float CameraPositionX { get; set; }
    public float CameraPositionY { get; set; }
    public float CameraPositionZ { get; set; }
    public bool HasCameraDirection { get; set; }
    public float CameraDirectionX { get; set; }
    public float CameraDirectionY { get; set; }
    public float CameraDirectionZ { get; set; }
    public bool HasWindowBounds { get; set; }
    public string WindowState { get; set; } = "Normal";
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }

    public EditorSettingsSnapshot ToModel()
    {
        CameraState? camera = HasCameraPosition || HasCameraDirection
            ? new CameraState(
                new Vector3(CameraPositionX, CameraPositionY, CameraPositionZ),
                new Vector3(CameraDirectionX, CameraDirectionY, CameraDirectionZ))
            : null;

        return new EditorSettingsSnapshot
        {
            Client = new ClientConfiguration
            {
                WowDirectory = WowDirectory ?? "",
                WowProduct = WowProduct ?? "",
                BuildConfig = BuildConfig ?? "",
                CdnConfig = CdnConfig ?? ""
            },
            Rendering = (Renderer ?? new PersistedRenderingSettings()).ToModel(),
            KeyboardLayout = ParseKeyboardLayout(KeyboardLayout),
            Camera = camera,
            Window = new WindowPlacement
            {
                HasBounds = HasWindowBounds,
                State = WindowState ?? "Normal",
                X = WindowX,
                Y = WindowY,
                Width = WindowWidth,
                Height = WindowHeight
            }
        }.Normalize();
    }

    public static PersistedEditorSettings From(EditorSettingsSnapshot settings)
    {
        var normalized = settings.Normalize();
        return new PersistedEditorSettings
        {
            WowDirectory = normalized.Client.WowDirectory,
            WowProduct = normalized.Client.WowProduct,
            BuildConfig = normalized.Client.BuildConfig,
            CdnConfig = normalized.Client.CdnConfig,
            KeyboardLayout = normalized.KeyboardLayout.ToString(),
            Renderer = PersistedRenderingSettings.From(normalized.Rendering),
            HasCameraPosition = normalized.Camera != null,
            CameraPositionX = normalized.Camera?.Position.X ?? 0f,
            CameraPositionY = normalized.Camera?.Position.Y ?? 0f,
            CameraPositionZ = normalized.Camera?.Position.Z ?? 0f,
            HasCameraDirection = normalized.Camera != null,
            CameraDirectionX = normalized.Camera?.Direction.X ?? 0f,
            CameraDirectionY = normalized.Camera?.Direction.Y ?? 0f,
            CameraDirectionZ = normalized.Camera?.Direction.Z ?? 0f,
            HasWindowBounds = normalized.Window.HasBounds,
            WindowState = normalized.Window.State,
            WindowX = normalized.Window.X,
            WindowY = normalized.Window.Y,
            WindowWidth = normalized.Window.Width,
            WindowHeight = normalized.Window.Height
        };
    }

    private static KeyboardLayoutMode ParseKeyboardLayout(string? value) =>
        Enum.TryParse<KeyboardLayoutMode>(value, true, out var parsed)
            ? parsed
            : KeyboardLayoutMode.Auto;
}

public sealed class JsonEditorSettingsStore : IEditorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public JsonEditorSettingsStore()
        : this(Path.Combine(AppContext.BaseDirectory, "settings.json"))
    {
    }

    public JsonEditorSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public EditorSettingsSnapshot Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new EditorSettingsSnapshot();

            return (JsonSerializer.Deserialize<PersistedEditorSettings>(
                        File.ReadAllText(_settingsPath), JsonOptions)
                    ?? new PersistedEditorSettings()).ToModel();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to load editor settings: {ex.Message}");
            return new EditorSettingsSnapshot();
        }
    }

    public void Save(EditorSettingsSnapshot settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = _settingsPath + ".tmp";
            var json = JsonSerializer.Serialize(PersistedEditorSettings.From(settings), JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to save editor settings: {ex.Message}");
        }
    }
}
