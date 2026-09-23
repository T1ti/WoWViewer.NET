using System.Runtime.CompilerServices;
using WoWLib;
using Fs = WoWLib.Filesystem;

namespace WoWRenderLib.Services;

/// <summary>
/// Gives path-addressed MPQ files stable in-process IDs for renderer caches.
/// These values are never passed to WowLib as FileDataIDs.
/// </summary>
public static class LegacyAssetIds
{
    private sealed class Registry
    {
        public readonly object Sync = new();
        public readonly Dictionary<string, uint> Ids = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<uint, string> Paths = [];
    }

    private static readonly ConditionalWeakTable<Fs.FileSystem, Registry> Registries = new();
    // Use a process-wide sequence so cache entries from a closed client never
    // collide with an asset in a newly opened client. Material fields are signed.
    private static long nextId = 0x3fffffff;

    public static uint Resolve(Fs.FileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(path))
            return 0;

        var normalized = path.Trim().Replace('\\', '/');
        using var key = new FileKey(normalized);
        if (!fileSystem.Exists(key))
            return 0;

        var registry = Registries.GetValue(fileSystem, static _ => new Registry());
        lock (registry.Sync)
        {
            if (registry.Ids.TryGetValue(normalized, out var existing))
                return existing;

            var id = Interlocked.Increment(ref nextId);
            if (id > int.MaxValue)
                throw new InvalidOperationException("The MPQ asset ID range is exhausted.");
            registry.Ids.Add(normalized, (uint)id);
            registry.Paths.Add((uint)id, normalized);
            return (uint)id;
        }
    }

    public static bool TryGetPath(Fs.FileSystem fileSystem, uint id, out string path)
    {
        var registry = Registries.GetValue(fileSystem, static _ => new Registry());
        lock (registry.Sync)
            return registry.Paths.TryGetValue(id, out path!);
    }
}
