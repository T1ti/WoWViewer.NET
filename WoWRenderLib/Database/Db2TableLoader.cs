using WoWLib;
using WoWLib.Database;
using Fs = WoWLib.Filesystem;

namespace WoWRenderLib.Database;

/// <summary>
/// Loads a WowLib database table from the active client filesystem.
///
/// CASC roots commonly do not contain name hashes, so a path-only
/// <see cref="TableBase.Read(Fs.FileSystem, FileKey)"/> call is not enough.
/// Resolve the exact DBFilesClient path through the configured listfile first,
/// then read by FileDataID. MPQ-era files have no FileDataID and continue to
/// use the resolved path key.
/// </summary>
public static class Db2TableLoader
{
    private static readonly string[] Extensions = [".db2", ".dbc"];

    public static bool IsSchemaMismatch(string diagnostic) =>
        diagnostic.Contains("SchemaMismatch", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the database payload without asking WowLib to bind its generated
    /// schema. This is used only for compatibility readers whose generated
    /// WowLib schema is older than the payload; path resolution and CASC
    /// FileDataID handling remain identical to the normal table loader.
    /// </summary>
    public static byte[]? TryReadBytes(
        Fs.FileSystem fileSystem,
        string tableName,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var attempts = new List<string>(Extensions.Length);
        foreach (var extension in Extensions)
        {
            var path = $"DBFilesClient/{tableName}{extension}";
            FileKey? requestedKey = null;
            FileKey? resolvedKey = null;
            FileKey? fileDataIdKey = null;
            FileDataId? fileDataId = null;
            try
            {
                requestedKey = new FileKey(path);
                if (fileSystem.Kind == StorageKind.Mpq)
                {
                    var bytes = fileSystem.ReadFile(requestedKey);
                    diagnostic = $"Read client database table '{tableName}' from '{path}' by path.";
                    return bytes;
                }
                resolvedKey = fileSystem.Resolve(requestedKey);
                var resolvedFileDataId = resolvedKey.Fdid?.Value;
                if (resolvedFileDataId is uint id && id != 0)
                {
                    fileDataId = new FileDataId(id);
                    fileDataIdKey = new FileKey(fileDataId);
                    var bytes = fileSystem.ReadFile(fileDataIdKey);
                    diagnostic =
                        $"Read client database table '{tableName}' from '{path}' " +
                        $"via FileDataID {id}.";
                    return bytes;
                }

                var pathBytes = fileSystem.ReadFile(resolvedKey);
                diagnostic =
                    $"Read client database table '{tableName}' from '{path}' by path.";
                return pathBytes;
            }
            catch (Exception exception)
            {
                var resolution = resolvedKey == null
                    ? string.Empty
                    : $" Resolved key: path='{resolvedKey.Path ?? "<none>"}', " +
                      $"FileDataID={resolvedKey.Fdid?.Value.ToString() ?? "<none>"}.";
                attempts.Add(
                    $"'{path}':{resolution} {FormatException(exception)}");
            }
            finally
            {
                fileDataIdKey?.Dispose();
                fileDataId?.Dispose();
                resolvedKey?.Dispose();
                requestedKey?.Dispose();
            }
        }

        diagnostic =
            $"Unable to read client database table '{tableName}' for client version " +
            $"{fileSystem.Version}. Tried the exact DBFilesClient paths " +
            $"'{tableName}.db2' and '{tableName}.dbc'; attempts: " +
            $"{string.Join(" | ", attempts)}. For CASC clients the selected path " +
            "must be present in the configured listfile so it can resolve to a " +
            $"FileDataID. Managed listfile status: {DescribeListfile()}.";
        return null;
    }

    public static Table? TryLoad(
        Fs.FileSystem fileSystem,
        string tableName,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var attempts = new List<string>(Extensions.Length);
        foreach (var extension in Extensions)
        {
            var path = $"DBFilesClient/{tableName}{extension}";
            Table? table = null;
            FileKey? requestedKey = null;
            FileKey? resolvedKey = null;
            FileKey? fileDataIdKey = null;
            FileDataId? fileDataId = null;
            try
            {
                // Classic products keep legacy-looking version tuples while
                // using the current retail file/schema lineage.  Resolve the
                // table schema against that lineage; using the tuple directly
                // makes WowLib report tables such as LightData and
                // LiquidTypeXTexture as unsupported even though their WDC
                // payloads are present in CASC.
                using var schemaVersion = fileSystem.Version.FormatLineage;
                table = Table.Open(tableName, schemaVersion);

                requestedKey = new FileKey(path);
                if (fileSystem.Kind == StorageKind.Mpq)
                {
                    table.Read(fileSystem, requestedKey);
                    diagnostic = $"Loaded client database table '{tableName}' from '{path}' by path.";
                    return table;
                }
                resolvedKey = fileSystem.Resolve(requestedKey);
                var resolvedFileDataId = resolvedKey.Fdid?.Value;
                if (resolvedFileDataId is uint id && id != 0)
                {
                    // Always use an FDID-only key for CASC. A key containing
                    // both a path and an id can still make the native backend
                    // attempt the unavailable root-manifest name hash.
                    fileDataId = new FileDataId(id);
                    fileDataIdKey = new FileKey(fileDataId);
                    table.Read(fileSystem, fileDataIdKey);
                }
                else
                {
                    // MPQ-era clients are path-addressed and intentionally do
                    // not expose a FileDataID. The exact canonical path key is
                    // the correct request for that storage backend.
                    table.Read(fileSystem, resolvedKey);
                }

                diagnostic = $"Loaded client database table '{tableName}'.";
                return table;
            }
            catch (Exception exception)
            {
                var resolution = resolvedKey == null
                    ? string.Empty
                    : $" Resolved key: path='{resolvedKey.Path ?? "<none>"}', " +
                      $"FileDataID={resolvedKey.Fdid?.Value.ToString() ?? "<none>"}.";
                attempts.Add(
                    $"'{path}':{resolution} {FormatException(exception)}");
                table?.Dispose();
            }
            finally
            {
                fileDataIdKey?.Dispose();
                fileDataId?.Dispose();
                resolvedKey?.Dispose();
                requestedKey?.Dispose();
            }
        }

        diagnostic =
            $"Unable to load WowLib table '{tableName}' for client version " +
            $"{fileSystem.Version}. Tried the exact DBFilesClient paths " +
            $"'{tableName}.db2' and '{tableName}.dbc'; " +
            $"attempts: {string.Join(" | ", attempts)}. " +
            "For CASC clients the selected path must be present in the " +
            "configured listfile so it can resolve to a FileDataID. " +
            $"Managed listfile status: {DescribeListfile()}.";
        return null;
    }

    private static string DescribeListfile() => Listfile.IsLoaded
        ? "loaded"
        : $"not loaded (last error: {Listfile.LastError ?? "none reported"})";

    private static string FormatException(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current != null; current = current.InnerException)
            parts.Add($"{current.GetType().Name}: {current.Message}");

        return string.Join(" -> ", parts);
    }
}
