using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.Services;

public sealed class PersistedEditorSettings
{
    public string WowDirectory { get; set; } = @"C:\Program Files (x86)\World of Warcraft";
    public string WowProduct { get; set; } = "wow_classic_era";
    public string BuildConfig { get; set; } = "";
    public string CdnConfig { get; set; } = "";
    public string KeyboardLayout { get; set; } = "Auto";
    public RendererSettings Renderer { get; set; } = new();
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

    public Vector3 GetCameraPosition() => new(CameraPositionX, CameraPositionY, CameraPositionZ);
    public Vector3 GetCameraDirection() => new(CameraDirectionX, CameraDirectionY, CameraDirectionZ);

    public WowClientConfig ToClientConfig() => new()
    {
        wowDir = WowDirectory ?? "",
        wowProduct = WowProduct ?? "",
        buildConfig = BuildConfig ?? "",
        cdnConfig = CdnConfig ?? ""
    };

    public static PersistedEditorSettings From(
        WowClientConfig clientConfig,
        RendererSettings renderer,
        string keyboardLayout,
        Vector3? cameraPosition = null,
        Vector3? cameraDirection = null) => new()
    {
        WowDirectory = clientConfig.wowDir,
        WowProduct = clientConfig.wowProduct,
        BuildConfig = clientConfig.buildConfig,
        CdnConfig = clientConfig.cdnConfig,
        KeyboardLayout = keyboardLayout,
        Renderer = renderer.Clone(),
        HasCameraPosition = cameraPosition.HasValue,
        CameraPositionX = cameraPosition?.X ?? 0f,
        CameraPositionY = cameraPosition?.Y ?? 0f,
        CameraPositionZ = cameraPosition?.Z ?? 0f,
        HasCameraDirection = cameraDirection.HasValue,
        CameraDirectionX = cameraDirection?.X ?? 0f,
        CameraDirectionY = cameraDirection?.Y ?? 0f,
        CameraDirectionZ = cameraDirection?.Z ?? 0f
    };
}

public static class EditorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Keep the editor configuration next to the executable so portable copies
    // of the application retain their settings with the program directory.
    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static PersistedEditorSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new PersistedEditorSettings();

            return JsonSerializer.Deserialize<PersistedEditorSettings>(
                       File.ReadAllText(SettingsPath), JsonOptions)
                   ?? new PersistedEditorSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to load editor settings: {ex.Message}");
            return new PersistedEditorSettings();
        }
    }

    public static void Save(
        WowClientConfig clientConfig,
        RendererSettings renderer,
        string keyboardLayout,
        Vector3? cameraPosition = null,
        int? windowX = null,
        int? windowY = null,
        double? windowWidth = null,
        double? windowHeight = null,
        string? windowState = null,
        Vector3? cameraDirection = null)
    {
        try
        {
            var temporaryPath = SettingsPath + ".tmp";
            var persisted = PersistedEditorSettings.From(
                clientConfig,
                renderer,
                keyboardLayout,
                cameraPosition,
                cameraDirection);
            if (windowX.HasValue && windowY.HasValue && windowWidth.HasValue && windowHeight.HasValue)
            {
                persisted.HasWindowBounds = true;
                persisted.WindowX = windowX.Value;
                persisted.WindowY = windowY.Value;
                persisted.WindowWidth = windowWidth.Value;
                persisted.WindowHeight = windowHeight.Value;
            }

            if (!windowX.HasValue || !windowY.HasValue || !windowWidth.HasValue || !windowHeight.HasValue)
            {
                var previous = Load();
                persisted.HasWindowBounds = previous.HasWindowBounds;
                persisted.WindowX = previous.WindowX;
                persisted.WindowY = previous.WindowY;
                persisted.WindowWidth = previous.WindowWidth;
                persisted.WindowHeight = previous.WindowHeight;
                if (windowState == null)
                    persisted.WindowState = previous.WindowState;
            }

            if (windowState != null)
                persisted.WindowState = windowState;

            var json = JsonSerializer.Serialize(persisted, JsonOptions);

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to save editor settings: {ex.Message}");
        }
    }
}
