using System.Globalization;
using System.Runtime.CompilerServices;
using WoWLib;
using WoWLib.Database;
using WoWRenderLib.Database;
using WoWRenderLib.Diagnostics;
using WoWRenderLib.Services;
using WTEditor.Avalonia.Models;
using Fs = WoWLib.Filesystem;

namespace WTEditor.Avalonia.Services;

public interface IMapCatalogService
{
    Task<IReadOnlyList<WorldMapCatalogEntry>> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the map catalogue from the active client's Map.db2 or Map.dbc.
/// </summary>
public sealed class MapCatalogService : IMapCatalogService
{
    private const string WdtFileDataIdColumn = "WdtFileDataID";

    private readonly object _cacheLock = new();
    private readonly HashSet<string> _reportedSettingFailures = new(StringComparer.Ordinal);
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
        var fileSystem = WowlibFileSystem.TryGetCurrent();
        if (fileSystem == null || (fileSystem.Kind == StorageKind.Casc
            && (!CASC.IsInitialized || string.IsNullOrWhiteSpace(CASC.BuildName))))
        {
            throw new InvalidOperationException(
                "WoW content must finish loading before the map catalogue can be read.");
        }

        var buildName = fileSystem.Kind == StorageKind.Mpq
            ? $"mpq:{fileSystem.Version}:{RuntimeHelpers.GetHashCode(fileSystem)}"
            : CASC.BuildName;
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
                _loadingTask = LoadCoreAsync(buildName, fileSystem);
                loadTask = _loadingTask;
            }
        }

        return await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WorldMapCatalogEntry>> LoadCoreAsync(
        string buildName,
        Fs.FileSystem fileSystem)
    {
        try
        {
            var maps = await Task.Run(() => LoadMaps(fileSystem, buildName)).ConfigureAwait(false);

            var wdtMetadata = fileSystem.Kind == StorageKind.Mpq
                ? await _terrainMetadataCacheService.CacheMpqAsync(buildName, maps, fileSystem).ConfigureAwait(false)
                : await _terrainMetadataCacheService.CacheAllAsync(buildName, maps).ConfigureAwait(false);
            var catalog = maps
                .Select(map => new WorldMapCatalogEntry(
                    map,
                    wdtMetadata.TryGetValue(map.Id, out var metadata)
                        ? metadata
                        : new WorldMapWdtMetadata(map.WdtFileDataId, 0)))
                .ToArray();

            lock (_cacheLock)
            {
                if (ReferenceEquals(WowlibFileSystem.TryGetCurrent(), fileSystem))
                {
                    _cachedBuildName = buildName;
                    _cachedMaps = catalog;
                }
            }

            return catalog;
        }
        catch (Exception exception)
        {
            LoadDiagnostics.Error($"Loading Map catalog for build {buildName}", exception);
            throw;
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

    private IReadOnlyList<WorldMapRecord> LoadMaps(Fs.FileSystem fileSystem, string buildName)
    {
        var table = Db2TableLoader.TryLoad(fileSystem, "Map", out var diagnostic)
            ?? throw new FileNotFoundException(diagnostic);
        using (table)
        {
            var columns = Enumerable.Range(0, checked((int)table.ColumnCount))
                .Select(index => (Index: (ulong)index, Info: table.ColumnInfo((ulong)index)))
                .ToArray();
            var idColumn = table.ColumnIndex("id");
            var directoryColumn = table.ColumnIndex("directory");
            var nameColumn = table.ColumnIndex("map_name");
            var optionalColumns = columns.ToDictionary(column => column.Info.Name, column => column.Index);
            var expansionColumn = optionalColumns.TryGetValue("expansion_id", out var expansion) ? (ulong?)expansion : null;
            var instanceColumn = optionalColumns.TryGetValue("instance_type", out var instance) ? (ulong?)instance : null;
            var wdtColumn = optionalColumns.TryGetValue("wdt_file_data_id", out var wdt) ? (ulong?)wdt : null;
            var records = new List<WorldMapRecord>(checked((int)table.RowCount));
            for (ulong row = 0; row < table.RowCount; row++)
            {
                var directory = table.GetString(row, directoryColumn, 0);
                var name = table.GetString(row, nameColumn, 0);
                if (string.IsNullOrWhiteSpace(name))
                    name = string.IsNullOrWhiteSpace(directory) ? $"Unnamed map {row}" : directory;

                var wdtPath = MapAssetPathResolver.GetWdtPath(directory);
                var wdtFileDataId = fileSystem.Kind == StorageKind.Mpq
                    ? 0
                    : ResolveWdtFileDataId(
                        directory,
                        wdtColumn is ulong wdtIndex ? checked((uint)table.GetInt(row, wdtIndex, 0)) : 0,
                        path => MapAssetPathResolver.TryResolveFileDataId(path, out var resolved)
                            ? resolved
                            : 0);

                records.Add(new WorldMapRecord(
                    checked((int)table.GetInt(row, idColumn, 0)),
                    name,
                    directory,
                    wdtFileDataId,
                    expansionColumn is ulong expansionIndex ? checked((int)table.GetInt(row, expansionIndex, 0)) : 0,
                    instanceColumn is ulong instanceIndex ? checked((int)table.GetInt(row, instanceIndex, 0)) : 0)
                {
                    WdtPath = fileSystem.Kind == StorageKind.Mpq
                        ? wdtPath
                        : wdtPath,
                    Settings = columns.Select(column => new WorldMapDbSetting(
                        column.Info.Name,
                        ReadWowlibSetting(table, row, column.Index, column.Info, buildName),
                        column.Info.Type.ToString())).ToArray()
                });
            }

            if (records.Count == 0)
                throw new InvalidDataException("Map database contains no records.");
            if (fileSystem.Kind == StorageKind.Casc)
            {
                var availableColumns = new HashSet<string>(
                    columns.Select(column => column.Info.Name), StringComparer.OrdinalIgnoreCase);
                ValidateMapRecords(buildName, records, availableColumns, CASC.FileExists);
            }
            return records.OrderBy(map => map.Id).ToArray();
        }
    }

    private string ReadWowlibSetting(Table table, ulong row, ulong index, Column column, string buildName)
    {
        try
        {
            return FormatWowlibSetting(table, row, index, column);
        }
        catch (Exception exception)
        {
            var failureKey = $"{buildName}\0{column.Name}\0{exception.GetType().FullName}\0{exception.Message}";
            lock (_cacheLock)
            {
                if (_reportedSettingFailures.Add(failureKey))
                    LoadDiagnostics.Error($"Reading Map column '{column.Name}' for build {buildName}", exception);
            }
            return $"Unavailable ({exception.GetType().Name})";
        }
    }

    private static string FormatWowlibSetting(Table table, ulong row, ulong index, Column column)
    {
        var length = column.Type == ColumnType.LocString
            ? 1
            : Math.Max(1, (int)column.ArrayLen);
        var values = Enumerable.Range(0, length).Select(element => column.Type switch
        {
            ColumnType.Int => table.GetInt(row, index, (ulong)element).ToString(CultureInfo.InvariantCulture),
            ColumnType.Float => table.GetFloat(row, index, (ulong)element).ToString(CultureInfo.InvariantCulture),
            _ => table.GetString(row, index, (ulong)element)
        }).ToArray();
        return values.Length == 1 ? values[0] : $"[{string.Join(", ", values)}]";
    }

    internal static void ValidateMapSchema(string buildName, ISet<string> columns)
    {
        if (!columns.Contains("Directory"))
            throw DefinitionError(buildName, "the required Directory column is missing", columns);
    }

    internal static void ValidateMapRecords(
        string buildName,
        IReadOnlyList<WorldMapRecord> maps,
        ISet<string> columns,
        Func<uint, bool> fileExists)
    {
        ValidateMapSchema(buildName, columns);
        if (maps.Count == 0)
            throw DefinitionError(buildName, "Map DB2 produced no records", columns);
        if (maps.All(map => string.IsNullOrWhiteSpace(map.Directory)))
            throw DefinitionError(buildName, "every map directory is empty", columns);

        var fileDataIds = maps
            .Select(map => map.WdtFileDataId)
            .Where(fileDataId => fileDataId != 0)
            .Distinct()
            .ToArray();
        if (fileDataIds.Length == 0 && maps.All(map => string.IsNullOrWhiteSpace(map.WdtPath)))
            throw DefinitionError(
                buildName,
                $"neither column {WdtFileDataIdColumn} nor Directory path resolution produced a canonical WDT path",
                columns);

        var missingFileDataIds = fileDataIds.Where(fileDataId => !fileExists(fileDataId)).ToArray();
        if (fileDataIds.Length > 0 && missingFileDataIds.Length == fileDataIds.Length &&
            maps.All(map => string.IsNullOrWhiteSpace(map.WdtPath)))
        {
            throw DefinitionError(buildName,
                $"none of the {fileDataIds.Length} nonzero WDT FileDataIDs exist in the active CASC build",
                columns);
        }

        if (missingFileDataIds.Length > 0)
        {
            var samples = string.Join(", ", missingFileDataIds.Take(8));
            var warning = $"Map DB2 {buildName} references {missingFileDataIds.Length} WDT FileDataID(s) " +
                          $"that are absent from CASC (examples: {samples}).";
            LoadDiagnostics.Warning(warning);
        }
    }

    private static InvalidDataException DefinitionError(
        string buildName,
        string problem,
        IEnumerable<string> columns)
    {
        var relevantColumns = columns
            .Where(column => column.Contains("FileDataID", StringComparison.OrdinalIgnoreCase)
                          || column.Contains("file_data_id", StringComparison.OrdinalIgnoreCase)
                          || column.StartsWith("Field_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(column => column)
            .Take(20);
        return new InvalidDataException(
            $"Map DB2 definition mismatch for build {buildName}: {problem}. " +
            $"Relevant loaded columns: {string.Join(", ", relevantColumns)}. " +
            "The WowLib WoWDBDefs schema may be outdated, or the managed listfile " +
            "does not contain the legacy map paths for this build.");
    }

    internal static uint ResolveWdtFileDataId(
        string directory,
        uint databaseFileDataId,
        Func<string, uint> pathResolver)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        if (databaseFileDataId != 0)
        {
            var knownPath = MapAssetPathResolver.GetWdtPath(directory);
            MapAssetPathResolver.Remember(databaseFileDataId, knownPath);
            return databaseFileDataId;
        }

        var path = MapAssetPathResolver.GetWdtPath(directory);
        return path.Length == 0 ? 0 : pathResolver(path);
    }
}
