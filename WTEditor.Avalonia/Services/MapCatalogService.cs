using DBCD;
using DBCD.Providers;
using System.Globalization;
using System.Diagnostics;
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
    private const string WdtFileDataIdColumn = "WdtFileDataID";

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
            var maps = await Task.Run(() =>
            {
                var dbcProvider = new DBCProvider();
                IDBCDStorage mapDatabase;
                try
                {
                    // The binary release can lag behind WoWDBDefs master. Map is
                    // small and loaded once, so use the current textual definition
                    // and let DBCD reject builds it does not describe.
                    var dbcd = new DBCD.DBCD(dbcProvider, new GithubDBDProvider(useCache: false));
                    mapDatabase = dbcd.Load("Map", buildName);
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException(
                        $"The current WoWDBDefs Map definition does not support active build {buildName}, " +
                        "or could not be downloaded. Update WoWDBDefs before loading this client build.",
                        exception);
                }

                var availableColumns = mapDatabase.AvailableColumns;
                var columns = new HashSet<string>(
                    availableColumns,
                    StringComparer.OrdinalIgnoreCase);
                ValidateMapSchema(buildName, columns);

                var records = mapDatabase.Values
                    .Select(row => ToRecord(row, availableColumns))
                    .OrderBy(map => map.Id)
                    .ToArray();
                ValidateMapRecords(
                    buildName,
                    records,
                    columns,
                    CASC.FileExists);
                return (IReadOnlyList<WorldMapRecord>)records;
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

    private static WorldMapRecord ToRecord(DBCDRow row, IReadOnlyList<string> columns)
    {
        var columnSet = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        var id = ReadInt(row, columnSet, "ID", row.ID);
        var name = ReadString(row, columnSet, "MapName_lang");
        if (string.IsNullOrWhiteSpace(name))
            name = ReadString(row, columnSet, "Directory");
        if (string.IsNullOrWhiteSpace(name))
            name = $"Unnamed map {id}";

        var directory = ReadString(row, columnSet, "Directory");

        return new WorldMapRecord(
            id,
            name,
            directory,
            Convert.ToUInt32(row[WdtFileDataIdColumn]),
            ReadInt(row, columnSet, "ExpansionID"),
            ReadInt(row, columnSet, "InstanceType", ReadInt(row, columnSet, "MapType")))
        {
            Settings = columns
                .Select(column => ReadSetting(row, column))
                .ToArray()
        };
    }

    private static WorldMapDbSetting ReadSetting(DBCDRow row, string column)
    {
        try
        {
            var value = row[column];
            return new WorldMapDbSetting(column, FormatSettingValue(value), GetTypeName(value));
        }
        catch (Exception exception)
        {
            // A build-specific field should not prevent the rest of the map
            // catalogue from loading if its generated accessor is unavailable.
            return new WorldMapDbSetting(
                column,
                $"Unavailable ({exception.GetType().Name})",
                "Unavailable");
        }
    }

    internal static string FormatSettingValue(object? value)
    {
        if (value == null)
            return "—";

        if (value is string text)
            return string.IsNullOrEmpty(text) ? "—" : text;

        if (value is Array array)
        {
            if (array.Length == 0)
                return "[]";

            return $"[{string.Join(", ", array.Cast<object?>().Select(FormatSettingValue))}]";
        }

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "—";

        return value.ToString() ?? "—";
    }

    private static string GetTypeName(object? value) => value switch
    {
        null => "null",
        Array array => $"{array.GetType().GetElementType()?.Name ?? "Array"}[]",
        _ => value.GetType().Name
    };

    internal static void ValidateMapSchema(string buildName, ISet<string> columns)
    {
        if (!columns.Contains("Directory"))
            throw DefinitionError(buildName, "the required Directory column is missing", columns);
        if (!columns.Contains(WdtFileDataIdColumn))
            throw DefinitionError(buildName, $"the required {WdtFileDataIdColumn} column is missing", columns);
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
        if (fileDataIds.Length == 0)
            throw DefinitionError(buildName, $"column {WdtFileDataIdColumn} yielded only zero WDT FileDataIDs", columns);

        var missingFileDataIds = fileDataIds.Where(fileDataId => !fileExists(fileDataId)).ToArray();
        if (missingFileDataIds.Length == fileDataIds.Length)
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
            Console.Error.WriteLine($"Warning: {warning}");
            Trace.TraceWarning(warning);
        }
    }

    private static InvalidDataException DefinitionError(
        string buildName,
        string problem,
        IEnumerable<string> columns)
    {
        var relevantColumns = columns
            .Where(column => column.Contains("FileDataID", StringComparison.OrdinalIgnoreCase)
                          || column.StartsWith("Field_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(column => column)
            .Take(20);
        return new InvalidDataException(
            $"Map DB2 definition mismatch for build {buildName}: {problem}. " +
            $"Relevant loaded columns: {string.Join(", ", relevantColumns)}. " +
            "The loaded textual WoWDBDefs definition is outdated or does not cover this build.");
    }

    private static int ReadInt(DBCDRow row, ISet<string> columns, string column, int fallback = 0) =>
        columns.Contains(column) ? Convert.ToInt32(row[column]) : fallback;

    private static string ReadString(DBCDRow row, ISet<string> columns, string column) =>
        columns.Contains(column) ? Convert.ToString(row[column]) ?? string.Empty : string.Empty;
}
