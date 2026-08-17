using System.Text.Json;
using WoWFormatLib.Structs.WDT;
using WoWRenderLib.Loaders;

namespace WoWRenderLib.Persistence;

/// <summary>
/// Persists the largest MDDF/MODF uniqueId found for each map.
/// </summary>
public static class MapUniqueIdStore
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "map-unique-ids.json");

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string? loadedPath;
    private static Dictionary<uint, uint> entries = [];

    public static uint GetOrScan(uint mapId, WDT map) => GetOrScan(mapId, map, DefaultPath);

    public static uint GetOrScan(uint mapId, WDT map, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (Sync)
        {
            EnsureLoaded(path);
            if (entries.TryGetValue(mapId, out var maximum))
                return maximum;

            maximum = MapUniqueIdScanner.ScanMap(map);
            entries[mapId] = maximum;
            Save(path);
            return maximum;
        }
    }

    public static bool TryGet(uint mapId, out uint maximum) => TryGet(mapId, out maximum, DefaultPath);

    public static bool TryGet(uint mapId, out uint maximum, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (Sync)
        {
            EnsureLoaded(path);
            return entries.TryGetValue(mapId, out maximum);
        }
    }

    private static void EnsureLoaded(string path)
    {
        if (string.Equals(loadedPath, path, StringComparison.Ordinal))
            return;

        loadedPath = path;
        entries = [];

        try
        {
            if (File.Exists(path))
            {
                entries = JsonSerializer.Deserialize<Dictionary<uint, uint>>(
                    File.ReadAllText(path), JsonOptions) ?? [];
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to load map uniqueId cache: {exception.Message}");
        }
    }

    private static void Save(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to save map uniqueId cache: {exception.Message}");
        }
    }
}
