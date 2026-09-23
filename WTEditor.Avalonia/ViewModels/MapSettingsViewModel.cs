using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Avalonia.Models;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.ViewModels;

/// <summary>
/// A labelled value projected for the map settings inspector.
/// </summary>
public sealed record MapSettingDisplayViewModel(
    string Label,
    string Value,
    string TypeName = "")
{
    public string ToolTip => string.IsNullOrWhiteSpace(TypeName)
        ? Value
        : $"{TypeName}: {Value}";
}

/// <summary>
/// A compact row for one non-empty WDT MAIN/MAID tile record.
/// </summary>
public sealed record WdtTileDisplayViewModel(
    string Position,
    string Flags,
    string FileDataIds)
{
    public string ToolTip => FileDataIds;
}

/// <summary>
/// Presentation state for the map.db and WDT inspector beside the minimap.
/// </summary>
public sealed partial class MapSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _hasSelectedMap;

    [ObservableProperty]
    private string _selectedMapName = "No map selected";

    [ObservableProperty]
    private string _selectedMapSubtitle = "Select a map to inspect its settings.";

    [ObservableProperty]
    private IReadOnlyList<MapSettingDisplayViewModel> _mapDbSettings = [];

    [ObservableProperty]
    private IReadOnlyList<MapSettingDisplayViewModel> _wdtSettings = [];

    [ObservableProperty]
    private IReadOnlyList<WdtTileDisplayViewModel> _wdtTiles = [];

    [ObservableProperty]
    private string _wdtStatus = string.Empty;

    [ObservableProperty]
    private bool _hasWdtError;

    public bool HasMapDbSettings => MapDbSettings.Count > 0;
    public bool HasWdtSettings => WdtSettings.Count > 0;
    public bool HasWdtTiles => WdtTiles.Count > 0;
    public bool HasNoSelectedMap => !HasSelectedMap;

    public string MapDbSettingsHeader => $"MAP DB2 · {MapDbSettings.Count} fields";
    public string WdtSettingsHeader => $"WDT HEADER · {WdtSettings.Count} fields";
    public string WdtTilesHeader => $"WDT TILES · {WdtTiles.Count} present";

    public void SetMap(WorldMapCatalogEntry? entry)
    {
        HasSelectedMap = entry != null;
        SelectedMapName = entry?.Map.Name ?? "No map selected";
        SelectedMapSubtitle = entry == null
            ? "Select a map to inspect its settings."
            : $"Map {entry.Map.Id} · {entry.Map.Directory}";

        MapDbSettings = entry == null
            ? []
            : entry.Map.Settings
                .Select(setting => new MapSettingDisplayViewModel(
                    setting.Name,
                    setting.Value,
                    setting.TypeName))
                .ToArray();
        if (entry != null && MapDbSettings.Count == 0)
            MapDbSettings = CreateFallbackMapSettings(entry.Map);

        WdtSettings = entry == null
            ? []
            : entry.Wdt.Settings
                .Select(setting => new MapSettingDisplayViewModel(setting.Name, setting.Value, "WDT"))
                .ToArray();
        if (entry != null && WdtSettings.Count == 0)
            WdtSettings = CreateFallbackWdtSettings(entry.Wdt);

        WdtTiles = entry?.Wdt.Tiles
            .Select(tile => CreateTile(tile, entry.Wdt.Path))
            .ToArray() ?? [];
        WdtStatus = entry?.Wdt.Error ?? (entry == null
            ? string.Empty
            : entry.Wdt.IsAvailable ? "WDT loaded" : "WDT data is unavailable");
        HasWdtError = entry?.Wdt.Error != null;

        OnPropertyChanged(nameof(HasMapDbSettings));
        OnPropertyChanged(nameof(HasWdtSettings));
        OnPropertyChanged(nameof(HasWdtTiles));
        OnPropertyChanged(nameof(HasNoSelectedMap));
        OnPropertyChanged(nameof(MapDbSettingsHeader));
        OnPropertyChanged(nameof(WdtSettingsHeader));
        OnPropertyChanged(nameof(WdtTilesHeader));
    }

    private static IReadOnlyList<MapSettingDisplayViewModel> CreateFallbackMapSettings(
        WorldMapRecord map) =>
    [
        new("ID", map.Id.ToString(CultureInfo.InvariantCulture), "Int32"),
        new("Directory", map.Directory, "String"),
        string.IsNullOrWhiteSpace(map.WdtPath)
            ? new("WdtFileDataID", map.WdtFileDataId.ToString(CultureInfo.InvariantCulture), "UInt32")
            : new("WDT path", map.WdtPath, "String"),
        new("ExpansionID", map.ExpansionId.ToString(CultureInfo.InvariantCulture), "Int32"),
        new("InstanceType", map.InstanceType.ToString(CultureInfo.InvariantCulture), "Int32")
    ];

    private static IReadOnlyList<MapSettingDisplayViewModel> CreateFallbackWdtSettings(
        WorldMapWdtMetadata wdt) =>
    [
        string.IsNullOrWhiteSpace(wdt.Path)
            ? new("WDT.FileDataID", wdt.FileDataId.ToString(CultureInfo.InvariantCulture), "UInt32")
            : new("WDT.Path", wdt.Path, "String"),
        new("WDT.Version", wdt.Version.ToString(CultureInfo.InvariantCulture), "UInt32"),
        new("MPHD.Flags", $"0x{wdt.Flags:X8}", "UInt32"),
        new("Terrain", wdt.HasTerrain ? "Yes" : "No", "Boolean"),
        new("MAIN.ActiveTerrainTiles", wdt.ActiveTiles.Count.ToString(CultureInfo.InvariantCulture), "Int32")
    ];

    private static WdtTileDisplayViewModel CreateTile(WorldMapWdtTileData tile, string wdtPath)
    {
        if (!string.IsNullOrWhiteSpace(wdtPath))
        {
            var adtPath = tile.IsActive
                ? MapAssetPathResolver.GetLegacyAdtPath(
                    wdtPath, (byte)tile.Position.X, (byte)tile.Position.Y)
                : string.Empty;
            return new WdtTileDisplayViewModel(
                $"({tile.Position.X}, {tile.Position.Y})",
                $"0x{tile.Flags:X8}{(tile.IsActive ? " · active" : string.Empty)}",
                adtPath);
        }

        var fileDataIds = string.Join(
            " · ",
            $"Root={FormatFileDataId(tile.RootAdtFileDataId)}",
            $"Obj0={FormatFileDataId(tile.Obj0AdtFileDataId)}",
            $"Obj1={FormatFileDataId(tile.Obj1AdtFileDataId)}",
            $"Tex0={FormatFileDataId(tile.Tex0AdtFileDataId)}",
            $"LOD={FormatFileDataId(tile.LodAdtFileDataId)}",
            $"Map={FormatFileDataId(tile.MapTextureFileDataId)}",
            $"Normal={FormatFileDataId(tile.MapTextureNormalFileDataId)}",
            $"Minimap={FormatFileDataId(tile.MinimapTextureFileDataId)}");

        return new WdtTileDisplayViewModel(
            $"({tile.Position.X}, {tile.Position.Y})",
            $"0x{tile.Flags:X8}{(tile.IsActive ? " · active" : string.Empty)}",
            fileDataIds);
    }

    private static string FormatFileDataId(uint value) =>
        value == 0
            ? "0"
            : $"{value.ToString(CultureInfo.InvariantCulture)} (0x{value:X8})";
}
