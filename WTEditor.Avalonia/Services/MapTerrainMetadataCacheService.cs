using WTEditor.Application.Geometry;
using WoWLib;
using WoWRenderLib.Services;
using WTEditor.Avalonia.Models;
using Formats = WoWLib.Formats;

namespace WTEditor.Avalonia.Services;

public interface IMapTerrainMetadataCacheService
{
    Task<IReadOnlyDictionary<int, WorldMapWdtMetadata>> CacheAllAsync(
        string buildName,
        IReadOnlyList<WorldMapRecord> maps,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-wide cache of the WDT flags and active tiles for each map in the active build.
/// WDTs are read through wowlib, then immediately disposed after their flags
/// and tile coordinates have been recorded.
/// </summary>
public sealed class MapTerrainMetadataCacheService : IMapTerrainMetadataCacheService
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private string? _cachedBuildName;
    private IReadOnlyDictionary<int, WorldMapWdtMetadata> _cachedMetadata =
        new Dictionary<int, WorldMapWdtMetadata>();

    public async Task<IReadOnlyDictionary<int, WorldMapWdtMetadata>> CacheAllAsync(
        string buildName,
        IReadOnlyList<WorldMapRecord> maps,
        CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_cachedBuildName, buildName, StringComparison.Ordinal)
                && _cachedMetadata.Count == maps.Count)
            {
                return _cachedMetadata;
            }

            var metadata = await Task.Run(() => ReadAllMaps(maps, cancellationToken), cancellationToken);
            _cachedBuildName = buildName;
            _cachedMetadata = metadata;
            return _cachedMetadata;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private static IReadOnlyDictionary<int, WorldMapWdtMetadata> ReadAllMaps(
        IReadOnlyList<WorldMapRecord> maps,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<int, WorldMapWdtMetadata>(maps.Count);
        foreach (var map in maps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            metadata[map.Id] = ReadWdtMetadata(map.WdtFileDataId);
        }

        return metadata;
    }

    private static WorldMapWdtMetadata ReadWdtMetadata(uint fileDataId)
    {
        if (fileDataId == 0)
            return new WorldMapWdtMetadata(0, 0);

        try
        {
            var fileSystem = WowlibFileSystem.Current;
            using var format = Formats.WDT.WDT.ForVersion(fileSystem.Version);
            format.Read(fileSystem, new FileKey(new FileDataId(fileDataId)));
            return new WorldMapWdtMetadata(
                fileDataId,
                Convert.ToUInt32(format.Root.Header.Flags))
            {
                GlobalWmoBounds = ReadGlobalWmoBounds(format.Root),
                ActiveTiles = Enumerable.Range(0, Math.Min(4096, format.Root.Tiles.Count))
                    .Where(index => (Convert.ToUInt32(format.Root.Tiles[index].Flags) & 1) != 0)
                    .Select(index => new WorldMapTile(index % 64, index / 64))
                    .ToArray()
            };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unable to read WDT {fileDataId}: {exception.Message}");
            return new WorldMapWdtMetadata(fileDataId, 0);
        }
    }

    internal static TileBounds? ReadGlobalWmoBounds(Formats.WDT.Root.WDTRoot root)
    {
        if ((Convert.ToUInt32(root.Header.Flags) & 1) == 0 || root.GlobalWmo.Count == 0)
            return null;
        var bounds = root.GlobalWmo[0].Extents;
        return MapCoordinates.PlacementBoundsToTile(bounds.Min.X, bounds.Min.Z, bounds.Max.X, bounds.Max.Z);
    }
}
