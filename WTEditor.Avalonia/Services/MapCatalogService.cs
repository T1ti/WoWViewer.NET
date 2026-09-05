using DBCD;
using TACTSharp;
using WoWRenderLib.Managers;
using WoWRenderLib.Providers;
using WoWRenderLib.Services;
using WTEditor.Avalonia.Models;

namespace WTEditor.Avalonia.Services;

public interface IMapCatalogService
{
    Task<IReadOnlyList<WorldMapCatalogEntry>> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the map catalogue from the Map DB2 of the currently active WoW client.
/// </summary>
public sealed class MapCatalogService : IMapCatalogService
{
    private readonly object _cacheLock = new();
    private readonly IMapTerrainMetadataCacheService _terrainMetadataCacheService;
    private string? _cachedBuildName;
    private IReadOnlyList<WorldMapCatalogEntry>? _cachedMaps;
    private string? _loadingBuildName;
    private Task<IReadOnlyList<WorldMapCatalogEntry>>? _loadingTask;

    public MapCatalogService(IMapTerrainMetadataCacheService terrainMetadataCacheService)
    {
        _terrainMetadataCacheService = terrainMetadataCacheService;
    }

    public async Task<IReadOnlyList<WorldMapCatalogEntry>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CASC.IsInitialized || string.IsNullOrWhiteSpace(CASC.BuildName))
        {
            throw new InvalidOperationException(
                "WoW content must finish loading before the map catalogue can be read.");
        }

        var buildName = CASC.BuildName;
        Task<IReadOnlyList<WorldMapCatalogEntry>> loadTask;
        lock (_cacheLock)
        {
            if (_cachedMaps != null && string.Equals(_cachedBuildName, buildName, StringComparison.Ordinal))
                return _cachedMaps;

            if (_loadingTask != null && string.Equals(_loadingBuildName, buildName, StringComparison.Ordinal))
            {
                loadTask = _loadingTask;
            }
            else
            {
                _loadingBuildName = buildName;
                _loadingTask = LoadCoreAsync(buildName);
                loadTask = _loadingTask;
            }
        }

        return await loadTask.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<WorldMapCatalogEntry>> LoadCoreAsync(string buildName)
    {
        try
        {
            var maps = await Task.Run(async () =>
            {
                var dbdProvider = new DBDProvider();
                var dbcProvider = new DBCProvider();
                var dbcManager = new DBCManager(dbdProvider, dbcProvider);
                var mapDatabase = await dbcManager.GetOrLoad("Map", buildName);

                var columns = new HashSet<string>(
                    mapDatabase.AvailableColumns,
                    StringComparer.OrdinalIgnoreCase);

                return (IReadOnlyList<WorldMapRecord>)mapDatabase.Values
                    .Select(row => ToRecord(row, columns))
                    .OrderBy(map => map.Id)
                    .ToArray();
            });

            var wdtMetadata = await _terrainMetadataCacheService.CacheAllAsync(buildName, maps);
            var catalog = maps
                .Select(map => new WorldMapCatalogEntry(
                    map,
                    wdtMetadata.TryGetValue(map.Id, out var metadata)
                        ? metadata
                        : new WorldMapWdtMetadata(map.WdtFileDataId, 0)))
                .ToArray();

            lock (_cacheLock)
            {
                if (string.Equals(CASC.BuildName, buildName, StringComparison.Ordinal))
                {
                    _cachedBuildName = buildName;
                    _cachedMaps = catalog;
                }
            }

            return catalog;
        }
        finally
        {
            lock (_cacheLock)
            {
                if (string.Equals(_loadingBuildName, buildName, StringComparison.Ordinal))
                {
                    _loadingTask = null;
                    _loadingBuildName = null;
                }
            }
        }
    }

    private static WorldMapRecord ToRecord(DBCDRow row, ISet<string> columns)
    {
        var id = ReadInt(row, columns, "ID", row.ID);
        var name = ReadString(row, columns, "MapName_lang");
        if (string.IsNullOrWhiteSpace(name))
            name = ReadString(row, columns, "Directory");
        if (string.IsNullOrWhiteSpace(name))
            name = $"Unnamed map {id}";

        var directory = ReadString(row, columns, "Directory");

        return new WorldMapRecord(
            id,
            name,
            directory,
            GetWdtFileDataId(row, columns, directory),
            ReadInt(row, columns, "ExpansionID"),
            ReadInt(row, columns, "InstanceType", ReadInt(row, columns, "MapType")));
    }

    private static uint GetWdtFileDataId(DBCDRow row, ISet<string> columns, string directory)
    {
        if (columns.Contains("WdtFileDataID"))
        {
            var fileDataId = Convert.ToUInt32(row["WdtFileDataID"]);
            if (fileDataId != 0)
                return fileDataId;
        }

        if (string.IsNullOrWhiteSpace(directory) || CASC.buildInstance.Root == null)
            return 0;

        var filename = $"World\\Maps\\{directory}\\{directory}.wdt";
        var hash = new Jenkins96().ComputeHash(filename);
        var entries = CASC.buildInstance.Root.GetEntriesByLookup(hash);
        return entries.Count > 0 ? entries[0].fileDataID : 0;
    }

    private static int ReadInt(DBCDRow row, ISet<string> columns, string column, int fallback = 0) =>
        columns.Contains(column) ? Convert.ToInt32(row[column]) : fallback;

    private static string ReadString(DBCDRow row, ISet<string> columns, string column) =>
        columns.Contains(column) ? Convert.ToString(row[column]) ?? string.Empty : string.Empty;
}
