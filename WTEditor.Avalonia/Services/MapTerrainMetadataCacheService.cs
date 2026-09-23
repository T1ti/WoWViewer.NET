using WTEditor.Application.Geometry;
using System.Globalization;
using WoWLib;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;
using WoWRenderLib.Diagnostics;
using WTEditor.Avalonia.Models;
using Fs = WoWLib.Filesystem;
using Formats = WoWLib.Formats;
using WdtHeaderFlags = WoWLib.Formats.WDT.Root.Chunks.MapHeaderFlags;
using WmoPlacementFlags = WoWLib.Formats.Common.MapObjDefFlags;

namespace WTEditor.Avalonia.Services;

public interface IMapTerrainMetadataCacheService
{
    Task<IReadOnlyDictionary<int, WorldMapWdtMetadata>> CacheAllAsync(
        string buildName,
        IReadOnlyList<WorldMapRecord> maps,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, WorldMapWdtMetadata>> CacheMpqAsync(
        string buildName,
        IReadOnlyList<WorldMapRecord> maps,
        Fs.FileSystem fileSystem,
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
    private readonly Func<uint, WorldMapWdtMetadata> _metadataReader;
    private string? _cachedBuildName;
    private MapIdentity[] _cachedMaps = [];
    private IReadOnlyDictionary<int, WorldMapWdtMetadata> _cachedMetadata =
        new Dictionary<int, WorldMapWdtMetadata>();

    public MapTerrainMetadataCacheService() : this(ReadWdtMetadata)
    {
    }

    internal MapTerrainMetadataCacheService(Func<uint, WorldMapWdtMetadata> metadataReader)
    {
        ArgumentNullException.ThrowIfNull(metadataReader);
        _metadataReader = metadataReader;
    }

    public Task<IReadOnlyDictionary<int, WorldMapWdtMetadata>> CacheAllAsync(
        string buildName,
        IReadOnlyList<WorldMapRecord> maps,
        CancellationToken cancellationToken = default) =>
        CacheCoreAsync(buildName, maps, map => _metadataReader(map.WdtFileDataId), cancellationToken);

    public Task<IReadOnlyDictionary<int, WorldMapWdtMetadata>> CacheMpqAsync(
        string buildName,
        IReadOnlyList<WorldMapRecord> maps,
        Fs.FileSystem fileSystem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return CacheCoreAsync(buildName, maps, map => ReadMpqWdtMetadata(fileSystem, map), cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, WorldMapWdtMetadata>> CacheCoreAsync(
        string buildName,
        IReadOnlyList<WorldMapRecord> maps,
        Func<WorldMapRecord, WorldMapWdtMetadata> reader,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildName);
        ArgumentNullException.ThrowIfNull(maps);
        var mapSnapshot = maps.ToArray();
        var requestedMaps = mapSnapshot
            .Select(static map => new MapIdentity(map.Id, map.WdtFileDataId, map.WdtPath))
            .OrderBy(static map => map.Id)
            .ThenBy(static map => map.WdtFileDataId)
            .ToArray();

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(_cachedBuildName, buildName, StringComparison.Ordinal)
                && _cachedMaps.AsSpan().SequenceEqual(requestedMaps))
            {
                return _cachedMetadata;
            }

            var metadata = await Task.Run(
                () => ReadAllMaps(mapSnapshot, reader, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            _cachedBuildName = buildName;
            _cachedMaps = requestedMaps;
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
        Func<WorldMapRecord, WorldMapWdtMetadata> metadataReader,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<int, WorldMapWdtMetadata>(maps.Count);
        foreach (var map in maps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            metadata[map.Id] = metadataReader(map);
        }

        return metadata;
    }

    private static WorldMapWdtMetadata ReadWdtMetadata(uint fileDataId)
    {
        if (fileDataId == 0)
        {
            return new WorldMapWdtMetadata(0, 0)
            {
                Error = "This map does not reference a WDT FileDataID.",
                Settings = [new WorldMapWdtSetting("Status", "No WDT FileDataID")]
            };
        }

        try
        {
            var fileSystem = WowlibFileSystem.Current;
            var bytes = CascFileReader.ReadFile(fileDataId);
            return ParseWdtMetadata(fileSystem, fileDataId, string.Empty, bytes);
        }
        catch (Exception exception)
        {
            var error = $"Unable to read WDT {fileDataId}: {exception.Message}";
            LoadDiagnostics.Error($"Reading WDT {fileDataId}", exception);
            return new WorldMapWdtMetadata(fileDataId, 0)
            {
                Error = error,
                Settings = [new WorldMapWdtSetting("Error", error)]
            };
        }
    }

    private static WorldMapWdtMetadata ReadMpqWdtMetadata(Fs.FileSystem fileSystem, WorldMapRecord map)
    {
        if (string.IsNullOrWhiteSpace(map.WdtPath))
            return new WorldMapWdtMetadata(0, 0) { Error = "This map has no WDT path." };

        try
        {
            return ParseWdtMetadata(fileSystem, map.WdtFileDataId, map.WdtPath, fileSystem.ReadFile(map.WdtPath));
        }
        catch (Exception exception)
        {
            var error = $"Unable to read {map.WdtPath}: {exception.Message}";
            return new WorldMapWdtMetadata(map.WdtFileDataId, 0)
            {
                Path = map.WdtPath,
                Error = error,
                Settings = [new WorldMapWdtSetting("Error", error)]
            };
        }
    }

    private static WorldMapWdtMetadata ParseWdtMetadata(
        Fs.FileSystem fileSystem,
        uint fileDataId,
        string path,
        byte[] bytes)
    {
            if (fileSystem.Kind == StorageKind.Casc && fileDataId != 0)
                WdtChunkDiagnostics.WarnAboutUnhandledChunks(fileDataId, bytes);
            using var root = Formats.WDT.Root.WDTRoot.ForVersion(fileSystem.Version);
            root.Read(bytes);
            var tiles = ReadTileData(root);
            var activeTiles = tiles
                .Where(tile => tile.IsActive)
                .Select(tile => tile.Position)
                .ToArray();
            var globalWmoName = ReadGlobalWmoName(root);
            var globalWmo = ReadGlobalWmo(root, globalWmoName);
            var globalWmoBounds = ReadGlobalWmoBounds(root);
            return new WorldMapWdtMetadata(
                fileDataId,
                Convert.ToUInt32(root.Header.Flags))
            {
                Path = path,
                Version = root.Mver,
                Settings = CreateWdtSettings(
                    fileDataId,
                    path,
                    bytes.Length,
                    root,
                    tiles,
                    globalWmoName,
                    globalWmo,
                    globalWmoBounds),
                Tiles = tiles,
                ActiveTiles = activeTiles,
                MinimapTextureFileDataIds = ReadMinimapTextureFileDataIds(root),
                GlobalWmoName = globalWmoName,
                GlobalWmo = globalWmo,
                GlobalWmoBounds = globalWmoBounds,
                LegacyHeaderValue = root.Header is Formats.WDT.Root.Chunks.WDTHeaderVanillaToLegion legacy
                    ? legacy.Something
                    : 0,
                LegacyHeaderUnusedValues = root.Header is Formats.WDT.Root.Chunks.WDTHeaderVanillaToLegion legacyHeader
                    ? legacyHeader.Unused.ToArray()
                    : []
            };
    }

    private static IReadOnlyList<WorldMapWdtTileData> ReadTileData(
        Formats.WDT.Root.WDTRoot root)
    {
        var mainCount = Math.Min(4096, root.Tiles.Count);
        var maid = GetMapFileDataIds(root);
        var maidCount = Math.Min(4096, maid.Length);
        var count = Math.Max(mainCount, maidCount);
        var result = new List<WorldMapWdtTileData>(count);

        for (var index = 0; index < count; index++)
        {
            var mainFlags = index < mainCount
                ? Convert.ToUInt32(root.Tiles[index].Flags)
                : 0;
            var files = index < maidCount ? maid[index] : default;
            var tile = new WorldMapWdtTileData(
                new WorldMapTile(index % 64, index / 64),
                mainFlags)
            {
                RootAdtFileDataId = files.RootAdt,
                Obj0AdtFileDataId = files.Obj0Adt,
                Obj1AdtFileDataId = files.Obj1Adt,
                Tex0AdtFileDataId = files.Tex0Adt,
                LodAdtFileDataId = files.LodAdt,
                MapTextureFileDataId = files.MapTexture,
                MapTextureNormalFileDataId = files.MapTextureN,
                MinimapTextureFileDataId = files.MinimapTexture
            };

            // Empty MAIN/MAID slots carry no information useful to the
            // inspector, so retain only present records while preserving their
            // original coordinates.
            if (mainFlags != 0 || tile.HasFileData)
                result.Add(tile);
        }

        return result;
    }

    private static MapFileDataIds[] GetMapFileDataIds(Formats.WDT.Root.WDTRoot root) => root switch
    {
        Formats.WDT.Root.WDTRootBfa value => CopyMapFileDataIds(value.MapFdids.AsDataSpan()),
        Formats.WDT.Root.WDTRootShadowlandsPlus value => CopyMapFileDataIds(value.MapFdids.AsDataSpan()),
        _ => []
    };

    private static MapFileDataIds[] CopyMapFileDataIds(
        ReadOnlySpan<Formats.WDT.Root.Chunks.MapFileDataIDs.Data> source)
    {
        var result = new MapFileDataIds[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var value = source[i];
            result[i] = new MapFileDataIds(
                value.RootAdt,
                value.Obj0Adt,
                value.Obj1Adt,
                value.Tex0Adt,
                value.LodAdt,
                value.MapTexture,
                value.MapTextureN,
                value.MinimapTexture);
        }

        return result;
    }

    private static string ReadGlobalWmoName(Formats.WDT.Root.WDTRoot root)
    {
        if (root.GlobalWmoName.Empty)
            return string.Empty;

        return root.GlobalWmoName.At(0);
    }

    private static WorldMapWdtGlobalWmoData? ReadGlobalWmo(
        Formats.WDT.Root.WDTRoot root,
        string globalWmoName)
    {
        if (root.GlobalWmo.Count == 0)
            return null;

        var placement = root.GlobalWmo[0];
        var flags = Convert.ToUInt32(placement.Flags);
        return new WorldMapWdtGlobalWmoData
        {
            Name = globalWmoName,
            NameFileDataId = placement.NameId,
            UniqueId = placement.UniqueId,
            PositionX = placement.Position.X,
            PositionY = placement.Position.Y,
            PositionZ = placement.Position.Z,
            RotationX = placement.Rotation.X,
            RotationY = placement.Rotation.Y,
            RotationZ = placement.Rotation.Z,
            ExtentsMinX = placement.Extents.Min.X,
            ExtentsMinY = placement.Extents.Min.Y,
            ExtentsMinZ = placement.Extents.Min.Z,
            ExtentsMaxX = placement.Extents.Max.X,
            ExtentsMaxY = placement.Extents.Max.Y,
            ExtentsMaxZ = placement.Extents.Max.Z,
            Flags = placement.Flags,
            DoodadSet = placement.DoodadSet,
            NameSet = placement.NameSet,
            RawScale = placement.Scale,
            EffectiveScale = (flags & (uint)WmoPlacementFlags.has_scale) != 0
                ? placement.Scale / 1024f
                : 1f
        };
    }

    private static IReadOnlyList<WorldMapWdtSetting> CreateWdtSettings(
        uint fileDataId,
        string path,
        int byteLength,
        Formats.WDT.Root.WDTRoot root,
        IReadOnlyList<WorldMapWdtTileData> tiles,
        string globalWmoName,
        WorldMapWdtGlobalWmoData? globalWmo,
        TileBounds? globalWmoBounds)
    {
        var mainCount = Math.Min(4096, root.Tiles.Count);
        var maid = GetMapFileDataIds(root);
        var settings = new List<WorldMapWdtSetting>
        {
            string.IsNullOrWhiteSpace(path)
                ? new("WDT.FileDataID", FormatFileDataId(fileDataId))
                : new("WDT.Path", path),
            new("WDT.ByteLength", byteLength.ToString(CultureInfo.InvariantCulture)),
            new("WDT.HasTerrain", (root.Header.Flags & 0x1) == 0 ? "Yes" : "No"),
            new("MVER.Version", root.Mver.ToString(CultureInfo.InvariantCulture)),
            new("MPHD.Flags", FormatHeaderFlags(root.Header.Flags)),
            new("MAIN.RecordCount", mainCount.ToString(CultureInfo.InvariantCulture)),
            new("MAIN.PresentTiles", tiles.Count.ToString(CultureInfo.InvariantCulture)),
            new("MAIN.ActiveTerrainTiles", tiles.Count(tile => tile.IsActive)
                .ToString(CultureInfo.InvariantCulture)),
            new("MWMO.Name", string.IsNullOrWhiteSpace(globalWmoName) ? "—" : globalWmoName),
            new("MWMO.Size", root.GlobalWmoName.Size.ToString(CultureInfo.InvariantCulture)),
            new("MODF.RecordCount", root.GlobalWmo.Count.ToString(CultureInfo.InvariantCulture))
        };

        switch (root.Header)
        {
            case Formats.WDT.Root.Chunks.WDTHeaderBfaPlus modern:
                settings.Add(new("MPHD.LgtFdid", FormatFileDataId(modern.LgtFdid)));
                settings.Add(new("MPHD.OccFdid", FormatFileDataId(modern.OccFdid)));
                settings.Add(new("MPHD.FogsFdid", FormatFileDataId(modern.FogsFdid)));
                settings.Add(new("MPHD.MpvFdid", FormatFileDataId(modern.MpvFdid)));
                settings.Add(new("MPHD.TexFdid", FormatFileDataId(modern.TexFdid)));
                settings.Add(new("MPHD.WdlFdid", FormatFileDataId(modern.WdlFdid)));
                settings.Add(new("MPHD.Pd4Fdid", FormatFileDataId(modern.Pd4Fdid)));
                settings.Add(new("MAID.RecordCount", maid.Length.ToString(CultureInfo.InvariantCulture)));
                settings.Add(new("MAID.PresentTiles", tiles.Count(tile => tile.HasFileData)
                    .ToString(CultureInfo.InvariantCulture)));
                break;
            case Formats.WDT.Root.Chunks.WDTHeaderVanillaToLegion legacy:
                settings.Add(new("MPHD.Something", FormatUInt32(legacy.Something)));
                settings.Add(new("MPHD.Unused", FormatUInt32Array(legacy.Unused.ToArray())));
                break;
        }

        if (root is Formats.WDT.Root.WDTRootShadowlandsPlus shadowlands)
        {
            settings.Add(new("MANM.Size", shadowlands.MapAnima.Size.ToString(CultureInfo.InvariantCulture)));
        }

        if (globalWmo is not null)
        {
            if (string.IsNullOrWhiteSpace(path))
                settings.Add(new("MODF.NameFileDataID", FormatFileDataId(globalWmo.NameFileDataId)));
            settings.Add(new("MODF.UniqueID", FormatUInt32(globalWmo.UniqueId)));
            settings.Add(new("MODF.Position", FormatVector(
                globalWmo.PositionX, globalWmo.PositionY, globalWmo.PositionZ)));
            settings.Add(new("MODF.Rotation", FormatVector(
                globalWmo.RotationX, globalWmo.RotationY, globalWmo.RotationZ)));
            settings.Add(new("MODF.Extents.Min", FormatVector(
                globalWmo.ExtentsMinX, globalWmo.ExtentsMinY, globalWmo.ExtentsMinZ)));
            settings.Add(new("MODF.Extents.Max", FormatVector(
                globalWmo.ExtentsMaxX, globalWmo.ExtentsMaxY, globalWmo.ExtentsMaxZ)));
            settings.Add(new("MODF.Flags", FormatWmoFlags(globalWmo.Flags)));
            settings.Add(new("MODF.DoodadSet", globalWmo.DoodadSet.ToString(CultureInfo.InvariantCulture)));
            settings.Add(new("MODF.NameSet", globalWmo.NameSet.ToString(CultureInfo.InvariantCulture)));
            settings.Add(new("MODF.RawScale", globalWmo.RawScale.ToString(CultureInfo.InvariantCulture)));
            settings.Add(new("MODF.EffectiveScale", FormatFloat(globalWmo.EffectiveScale)));
        }

        if (globalWmoBounds is { } bounds)
        {
            settings.Add(new("MODF.TileBounds", FormatTileBounds(bounds)));
        }

        return settings;
    }

    private static string FormatHeaderFlags(uint value)
    {
        var activeFlags = Enum.GetValues<WdtHeaderFlags>()
            .Select(flag => (Flag: flag, Mask: Convert.ToUInt32(flag)))
            .Where(item => item.Mask != 0 && (value & item.Mask) == item.Mask)
            .Select(item => item.Flag.ToString())
            .ToArray();
        var knownBits = Enum.GetValues<WdtHeaderFlags>()
            .Aggregate(0u, (known, flag) => known | Convert.ToUInt32(flag));
        var unknownBits = value & ~knownBits;
        if (unknownBits != 0)
            activeFlags = [.. activeFlags, $"Unknown (0x{unknownBits:X})"];

        var names = activeFlags.Length == 0 ? "None" : string.Join(", ", activeFlags);
        return $"{names} (0x{value:X8})";
    }

    private static string FormatWmoFlags(ushort value)
    {
        var activeFlags = Enum.GetValues<WmoPlacementFlags>()
            .Select(flag => (Flag: flag, Mask: Convert.ToUInt32(flag)))
            .Where(item => item.Mask != 0 && (value & item.Mask) == item.Mask)
            .Select(item => item.Flag.ToString())
            .ToArray();
        var knownBits = Enum.GetValues<WmoPlacementFlags>()
            .Aggregate(0u, (known, flag) => known | Convert.ToUInt32(flag));
        var unknownBits = value & ~knownBits;
        if (unknownBits != 0)
            activeFlags = [.. activeFlags, $"Unknown (0x{unknownBits:X})"];

        var names = activeFlags.Length == 0 ? "None" : string.Join(", ", activeFlags);
        return $"{names} (0x{value:X4})";
    }

    private static string FormatFileDataId(uint value) =>
        value == 0 ? "0" : $"{value.ToString(CultureInfo.InvariantCulture)} (0x{value:X8})";

    private static string FormatUInt32(uint value) =>
        $"{value.ToString(CultureInfo.InvariantCulture)} (0x{value:X8})";

    private static string FormatUInt32Array(IReadOnlyList<uint> values) =>
        values.Count == 0 ? "[]" : $"[{string.Join(", ", values.Select(FormatUInt32))}]";

    private static string FormatFloat(float value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FormatVector(float x, float y, float z) =>
        $"({FormatFloat(x)}, {FormatFloat(y)}, {FormatFloat(z)})";

    private static string FormatTileBounds(TileBounds bounds) =>
        $"({FormatFloat((float)bounds.MinX)}, {FormatFloat((float)bounds.MinY)}) → " +
        $"({FormatFloat((float)bounds.MaxX)}, {FormatFloat((float)bounds.MaxY)})";

    internal static TileBounds? ReadGlobalWmoBounds(Formats.WDT.Root.WDTRoot root)
    {
        if ((Convert.ToUInt32(root.Header.Flags) & 1) == 0 || root.GlobalWmo.Count == 0)
            return null;
        var bounds = root.GlobalWmo[0].Extents;
        return MapCoordinates.PlacementBoundsToTile(bounds.Min.X, bounds.Min.Z, bounds.Max.X, bounds.Max.Z);
    }

    internal static IReadOnlyDictionary<WorldMapTile, uint> ReadMinimapTextureFileDataIds(
        Formats.WDT.Root.WDTRoot root)
    {
        ReadOnlySpan<Formats.WDT.Root.Chunks.MapFileDataIDs.Data> entries = root switch
        {
            Formats.WDT.Root.WDTRootBfa value => value.MapFdids.AsDataSpan(),
            Formats.WDT.Root.WDTRootShadowlandsPlus value => value.MapFdids.AsDataSpan(),
            _ => []
        };

        var result = new Dictionary<WorldMapTile, uint>();
        for (var index = 0; index < Math.Min(4096, entries.Length); index++)
        {
            var fileDataId = entries[index].MinimapTexture;
            if (fileDataId != 0)
                result[new WorldMapTile(index % 64, index / 64)] = fileDataId;
        }

        return result;
    }

    private readonly record struct MapIdentity(int Id, uint WdtFileDataId, string WdtPath);
}
