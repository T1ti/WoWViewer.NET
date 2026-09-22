using System.Collections.Concurrent;
using TACTSharp;
using WoWRenderLib.Diagnostics;

namespace WoWRenderLib.Services;

/// <summary>
/// Resolves map assets for client schemas that predate FileDataID columns.
/// Modern clients use IDs embedded in Map/MAID; older CASC clients use the
/// same assets but require their canonical virtual paths to be resolved.
/// </summary>
public static class MapAssetPathResolver
{
    private static readonly ConcurrentDictionary<uint, string> ResolvedPaths = [];

    public static string GetWdtPath(string mapDirectory)
    {
        var directory = NormalizeMapDirectory(mapDirectory);
        if (directory.Length == 0)
            return string.Empty;

        var separator = directory.LastIndexOf('/');
        var mapName = separator >= 0 ? directory[(separator + 1)..] : directory;
        return $"world/maps/{directory}/{mapName}.wdt";
    }

    public static string GetLegacyAdtPath(string wdtPath, byte tileX, byte tileY)
    {
        if (string.IsNullOrWhiteSpace(wdtPath))
            return string.Empty;

        var normalized = NormalizePath(wdtPath);
        var basePath = normalized.EndsWith(".wdt", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
        return $"{basePath}_{tileX}_{tileY}.adt";
    }

    public static bool TryResolveFileDataId(string path, out uint fileDataId)
    {
        fileDataId = 0;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = NormalizePath(path);
        if (Listfile.TryGetFileDataID(normalized, out fileDataId)
            && fileDataId != 0
            && (!CASC.IsInitialized || CASC.FileExists(fileDataId)))
        {
            Remember(fileDataId, normalized);
            return true;
        }

        // Some roots still retain filename lookup hashes even when the
        // community listfile does not know the name. This is a useful second
        // source and keeps compatibility independent of listfile freshness.
        try
        {
            var root = CASC.IsInitialized ? CASC.buildInstance.Root : null;
            if (root == null)
                return false;

            using var hasher = new Jenkins96();
            var entries = root.GetEntriesByLookup(hasher.ComputeHash(normalized, fix: true));
            if (entries.Count == 0 || entries[0].fileDataID == 0)
                return false;

            fileDataId = entries[0].fileDataID;
            Remember(fileDataId, normalized);
            return true;
        }
        catch (Exception exception)
        {
            LoadDiagnostics.Error($"Resolving map asset path '{normalized}'", exception);
            return false;
        }
    }

    public static bool TryGetPath(uint fileDataId, out string path)
    {
        if (ResolvedPaths.TryGetValue(fileDataId, out path!))
            return true;

        if (Listfile.TryGetFilename(fileDataId, out path!))
        {
            path = NormalizePath(path);
            Remember(fileDataId, path);
            return true;
        }

        path = string.Empty;
        return false;
    }

    public static void Remember(uint fileDataId, string path)
    {
        if (fileDataId != 0 && !string.IsNullOrWhiteSpace(path))
            ResolvedPaths[fileDataId] = NormalizePath(path);
    }

    private static string NormalizeMapDirectory(string directory) =>
        NormalizePath(directory).Trim('/');

    private static string NormalizePath(string path) =>
        path.Trim().Replace('\\', '/');
}
