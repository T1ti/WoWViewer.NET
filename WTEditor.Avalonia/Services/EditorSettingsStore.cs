using System;
using System.IO;
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

    public WowClientConfig ToClientConfig() => new()
    {
        wowDir = WowDirectory ?? "",
        wowProduct = WowProduct ?? "",
        buildConfig = BuildConfig ?? "",
        cdnConfig = CdnConfig ?? ""
    };

    public static PersistedEditorSettings From(WowClientConfig clientConfig, RendererSettings renderer, string keyboardLayout) => new()
    {
        WowDirectory = clientConfig.wowDir,
        WowProduct = clientConfig.wowProduct,
        BuildConfig = clientConfig.buildConfig,
        CdnConfig = clientConfig.cdnConfig,
        KeyboardLayout = keyboardLayout,
        Renderer = renderer.Clone()
    };
}

public static class EditorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WoW.Tools");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

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

    public static void Save(WowClientConfig clientConfig, RendererSettings renderer, string keyboardLayout)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            var temporaryPath = SettingsPath + ".tmp";
            var json = JsonSerializer.Serialize(
                PersistedEditorSettings.From(clientConfig, renderer, keyboardLayout), JsonOptions);

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to save editor settings: {ex.Message}");
        }
    }
}
