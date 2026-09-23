using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using WTEditor.Avalonia.Models;
using Fs = WoWLib.Filesystem;

namespace WTEditor.Avalonia.Services;

/// <summary>Resolves MPQ-era minimap tile names to the hashed BLP paths in md5translate.trs.</summary>
internal sealed class LegacyMinimapCatalog
{
    private const string TranslationPath = "textures/Minimap/md5translate.trs";
    private static readonly ConditionalWeakTable<Fs.FileSystem, LegacyMinimapCatalog> Cache = new();
    private readonly Dictionary<string, Dictionary<WorldMapTile, string>> _maps;

    private LegacyMinimapCatalog(Dictionary<string, Dictionary<WorldMapTile, string>> maps) => _maps = maps;

    public static LegacyMinimapCatalog For(Fs.FileSystem fileSystem) =>
        Cache.GetValue(fileSystem, static fs => Parse(Encoding.UTF8.GetString(fs.ReadFile(TranslationPath))));

    public IReadOnlyDictionary<WorldMapTile, string> TilesFor(string directory) =>
        _maps.TryGetValue(directory, out var tiles) ? tiles : new Dictionary<WorldMapTile, string>();

    internal static LegacyMinimapCatalog Parse(string text)
    {
        var maps = new Dictionary<string, Dictionary<WorldMapTile, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('\t');
            if (separator <= 0)
                continue;

            var logicalPath = line[..separator].Trim().Replace('\\', '/');
            var hashedName = line[(separator + 1)..].Trim().Replace('\\', '/');
            var slash = logicalPath.LastIndexOf('/');
            if (slash <= 0 || string.IsNullOrWhiteSpace(hashedName)
                || !hashedName.EndsWith(".blp", StringComparison.OrdinalIgnoreCase))
                continue;

            var directory = logicalPath[..slash];
            var filename = logicalPath[(slash + 1)..];
            if (!filename.StartsWith("map", StringComparison.OrdinalIgnoreCase)
                || !filename.EndsWith(".blp", StringComparison.OrdinalIgnoreCase))
                continue;

            var coordinates = filename.AsSpan(3, filename.Length - 7);
            var underscore = coordinates.IndexOf('_');
            if (underscore <= 0
                || !int.TryParse(coordinates[..underscore], NumberStyles.None, CultureInfo.InvariantCulture, out var x)
                || !int.TryParse(coordinates[(underscore + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                || x is < 0 or >= 64 || y is < 0 or >= 64)
                continue;

            if (!maps.TryGetValue(directory, out var tiles))
                maps.Add(directory, tiles = new Dictionary<WorldMapTile, string>());
            tiles[new WorldMapTile(x, y)] = $"textures/Minimap/{hashedName}";
        }

        return new LegacyMinimapCatalog(maps);
    }
}
