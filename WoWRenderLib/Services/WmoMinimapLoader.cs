using System.Numerics;
using System.Text;
using WoWLib;
using WoWLib.Database;
using WoWRenderLib.Database;
using Formats = WoWLib.Formats;

namespace WoWRenderLib.Services;

public sealed record WmoMinimapGroup(int GroupIndex, Vector3 Minimum, Vector3 Maximum,
    byte[] Pixels, int Width, int Height, int OffsetX = 0, int OffsetY = 0);
public sealed record WmoMinimapData(Vector3 Position, Vector3 Rotation, float Scale,
    IReadOnlyList<WmoMinimapGroup> Groups, int MissingTextures);

public interface IWmoMinimapLoader
{
    WmoMinimapData Load(uint wdtFileDataId, CancellationToken token);
    WmoMinimapData Load(string wdtPath, CancellationToken token);
}

/// <summary>CPU-only wowlib adapter. No renderer initialization or UI objects are needed.</summary>
public sealed class WmoMinimapLoader : IWmoMinimapLoader
{
    // Stable DBFilesClient FileDataID used by every build that contains this
    // table (7.3.0.24473 and later, including modern Classic branches).
    private const uint WmoMinimapTextureTableFileDataId = 1323241;
    private const int FirstWmoMinimapTextureBuild = 24473;
    private static readonly object ModernTextureCacheLock = new();
    private static string modernTextureCacheBuild = string.Empty;
    private static IReadOnlyDictionary<uint, WmoMinimapTextureRecord[]>? modernTextureCache;

    internal readonly record struct WmoMinimapTextureRecord(
        uint WmoId,
        int GroupIndex,
        int BlockX,
        int BlockY,
        uint FileDataId);

    public static string GetTexturePath(string wmoPath, int groupIndex, int offsetX = 0, int offsetY = 0)
    {
        var path = wmoPath.Replace('\\', '/');
        // Root names are World/WMO/..., minimap logical names are WMO/....
        if (path.StartsWith("world/", StringComparison.OrdinalIgnoreCase)) path = path[6..];
        if (path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return $"{path}_{groupIndex:D3}_{offsetX:D2}_{offsetY:D2}.blp";
    }

    public static IReadOnlyList<string> GetTextureCandidates(string wmoPath, int groupIndex, int offsetX = 0, int offsetY = 0)
    {
        var paths = new List<string> { GetTexturePath(wmoPath, groupIndex, offsetX, offsetY) };
        // Classic replacement roots retain the original group's minimap filenames.
        if (wmoPath.EndsWith("_classic.wmo", StringComparison.OrdinalIgnoreCase))
            paths.Add(GetTexturePath(wmoPath[..^12] + ".wmo", groupIndex, offsetX, offsetY));
        return paths;
    }

    public WmoMinimapData Load(uint wdtFileDataId, CancellationToken token)
    {
        using var key = WowlibFileSystem.AssetKey(WowlibFileSystem.Current, wdtFileDataId);
        return Load(key, token);
    }

    public WmoMinimapData Load(string wdtPath, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wdtPath);
        using var key = new FileKey(wdtPath);
        return Load(key, token);
    }

    private static WmoMinimapData Load(FileKey wdtKey, CancellationToken token)
    {
        var fs = WowlibFileSystem.Current;
        using var wdt = Formats.WDT.WDT.ForVersion(fs.Version);
        wdt.Read(fs, wdtKey);
        if ((Convert.ToUInt32(wdt.Root.Header.Flags) & 1) == 0 || wdt.Root.GlobalWmo.Count == 0)
            throw new InvalidDataException("The WDT has no global WMO placement.");
        var placement = wdt.Root.GlobalWmo[0];
        var nameBlock = wdt.Root.GlobalWmoName;
        var path = nameBlock.Empty ? string.Empty : nameBlock.At(0);

        token.ThrowIfCancellationRequested();
        // MOGI contains each source group's local bounds. Avoid loading meshes/materials.
        using var root = Formats.WMO.Root.WMORoot.ForVersion(fs.Version);
        var useModernTextures = fs.Kind == StorageKind.Casc && UsesModernTextureTable(CASC.BuildName);
        if (useModernTextures)
            root.Read(CascFileReader.ReadFile(placement.NameId));
        else
        {
            if (fs.Kind == StorageKind.Mpq && string.IsNullOrWhiteSpace(path))
                throw new FileNotFoundException("The MPQ WDT has no global WMO path in MWMO.");
            using var key = string.IsNullOrWhiteSpace(path)
                ? new FileKey(new FileDataId(placement.NameId))
                : new FileKey(path);
            if (string.IsNullOrWhiteSpace(path))
                Listfile.TryGetFilename(placement.NameId, out path);
            if (string.IsNullOrWhiteSpace(path))
                throw new FileNotFoundException("The global WMO filename is unavailable in MWMO and the listfile.");
            root.Read(fs.ReadFile(key));
        }

        if (useModernTextures)
            return LoadModern(root, placement, token);

        var groups = new List<WmoMinimapGroup>();
        var missing = 0;
        Dictionary<string, string>? translations = null;
        var textureDirectory = fs.Kind == StorageKind.Mpq ? "textures/Minimap" : "world/minimaps";
        for (var index = 0; index < root.GroupInfos.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var bounds = root.GroupInfos[index].BoundingBox;
            foreach (var (offsetX, offsetY) in GetTileOffsets(ToVector(bounds.Min), ToVector(bounds.Max)))
            {
                token.ThrowIfCancellationRequested();
                var logicalPath = GetTexturePath(path, index, offsetX, offsetY);
                try
                {
                    string? texturePath = null;
                    var logicalCandidates = GetTextureCandidates(path, index, offsetX, offsetY);
                    foreach (var logical in logicalCandidates)
                    {
                        foreach (var candidate in new[] { $"{textureDirectory}/{logical}", logical })
                            if (fs.Exists(new FileKey(candidate))) { texturePath = candidate; break; }
                        if (texturePath != null) break;
                    }
                    if (texturePath == null)
                    {
                        if (translations == null)
                        {
                            translations = new(StringComparer.OrdinalIgnoreCase);
                            using var translationKey = new FileKey($"{textureDirectory}/md5translate.trs");
                            if (fs.Exists(translationKey))
                                translations = ParseTranslations(Encoding.UTF8.GetString(fs.ReadFile(translationKey)));
                        }
                        foreach (var logical in logicalCandidates)
                            if (translations.TryGetValue(logical, out var hashedPath))
                            {
                                texturePath = fs.Kind == StorageKind.Mpq
                                    ? $"{textureDirectory}/{hashedPath}"
                                    : hashedPath.Contains('/') ? hashedPath : $"{textureDirectory}/{hashedPath}";
                                break;
                            }
                    }
                    if (texturePath == null) { missing++; continue; }
                    using var blp = new Formats.BLP.BLP();
                    blp.Read(fs, new FileKey(texturePath));
                    using var image = blp.Decode(0);
                    groups.Add(new(index, ToVector(bounds.Min), ToVector(bounds.Max),
                        image.Pixels.AsSpan().ToArray(), (int)image.Width, (int)image.Height, offsetX, offsetY));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    missing++;
                    System.Diagnostics.Debug.WriteLine($"WMO minimap {logicalPath}: {exception.Message}");
                }
            }
        }
        token.ThrowIfCancellationRequested();
        var scale = (placement.Flags & (uint)Formats.Common.MapObjDefFlags.has_scale) != 0
            ? placement.Scale / 1024f : 1f;
        return new(ToVector(placement.Position), ToVector(placement.Rotation), scale, groups, missing);
    }

    private static WmoMinimapData LoadModern(
        Formats.WMO.Root.WMORoot root,
        Formats.Common.SmMapObjDef placement,
        CancellationToken token)
    {
        var records = GetModernTextureRecords(root.Header.WmoId);
        var groups = new List<WmoMinimapGroup>(records.Count);
        var missing = 0;
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            if (record.GroupIndex < 0 || record.GroupIndex >= root.GroupInfos.Count || record.FileDataId == 0)
            {
                missing++;
                continue;
            }

            var bounds = root.GroupInfos[record.GroupIndex].BoundingBox;
            try
            {
                using var blp = new Formats.BLP.BLP();
                blp.Read(CascFileReader.ReadFile(record.FileDataId));
                using var image = blp.Decode(0);
                groups.Add(new(record.GroupIndex, ToVector(bounds.Min), ToVector(bounds.Max),
                    image.Pixels.AsSpan().ToArray(), (int)image.Width, (int)image.Height,
                    record.BlockX, record.BlockY));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                missing++;
                System.Diagnostics.Debug.WriteLine(
                    $"WMO minimap FileDataID {record.FileDataId}: {exception.Message}");
            }
        }

        token.ThrowIfCancellationRequested();
        var scale = (placement.Flags & (uint)Formats.Common.MapObjDefFlags.has_scale) != 0
            ? placement.Scale / 1024f
            : 1f;
        return new(ToVector(placement.Position), ToVector(placement.Rotation), scale, groups, missing);
    }

    private static IReadOnlyList<WmoMinimapTextureRecord> GetModernTextureRecords(uint wmoId)
    {
        var buildName = CASC.BuildName;
        lock (ModernTextureCacheLock)
        {
            if (modernTextureCache == null ||
                !string.Equals(modernTextureCacheBuild, buildName, StringComparison.Ordinal))
            {
                modernTextureCache = ReadModernTextureTable();
                modernTextureCacheBuild = buildName;
            }

            return modernTextureCache.TryGetValue(wmoId, out var records)
                ? records
                : [];
        }
    }

    private static IReadOnlyDictionary<uint, WmoMinimapTextureRecord[]> ReadModernTextureTable()
    {
        const string tableName = "WMOMinimapTexture";
        var fs = WowlibFileSystem.Current;
        using var lineage = fs.Version.FormatLineage;
        using var table = Table.Open(tableName, lineage);
        table.Read(CascFileReader.ReadFile(WmoMinimapTextureTableFileDataId));

        var wmoIdColumn = Db2Schema.RequireColumn(table, tableName, "wmoid");
        var groupColumn = Db2Schema.RequireColumn(table, tableName, "group_num");
        var blockXColumn = Db2Schema.RequireColumn(table, tableName, "block_x");
        var blockYColumn = Db2Schema.RequireColumn(table, tableName, "block_y");
        var fileDataIdColumn = Db2Schema.RequireColumn(table, tableName, "file_data_id");
        var records = new Dictionary<uint, List<WmoMinimapTextureRecord>>();

        for (var row = 0UL; row < table.RowCount; row++)
        {
            var wmoIdValue = table.GetInt(row, wmoIdColumn, 0);
            var fileDataIdValue = table.GetInt(row, fileDataIdColumn, 0);
            if (wmoIdValue <= 0 || wmoIdValue > uint.MaxValue ||
                fileDataIdValue <= 0 || fileDataIdValue > uint.MaxValue)
                continue;

            var wmoId = (uint)wmoIdValue;
            if (!records.TryGetValue(wmoId, out var wmoRecords))
                records.Add(wmoId, wmoRecords = []);
            wmoRecords.Add(new(
                wmoId,
                checked((int)table.GetInt(row, groupColumn, 0)),
                checked((int)table.GetInt(row, blockXColumn, 0)),
                checked((int)table.GetInt(row, blockYColumn, 0)),
                (uint)fileDataIdValue));
        }

        return records.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    internal static bool UsesModernTextureTable(string buildName)
    {
        if (string.IsNullOrWhiteSpace(buildName))
            return false;
        var separator = buildName.LastIndexOf('.');
        return separator >= 0 &&
            int.TryParse(buildName.AsSpan(separator + 1), out var build) &&
            build >= FirstWmoMinimapTextureBuild;
    }

    /// <summary>The generator splits group bounds into ceil(width/128) by ceil(height/128) tiles.</summary>
    public static IEnumerable<(int X, int Y)> GetTileOffsets(Vector3 minimum, Vector3 maximum)
    {
        var width = (double)maximum.X - minimum.X;
        var height = (double)maximum.Y - minimum.Y;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            yield break;
        var countX = checked((int)Math.Ceiling(width / 128));
        var countY = checked((int)Math.Ceiling(height / 128));
        for (var x = 0; x < countX; x++)
        for (var y = 0; y < countY; y++)
            yield return (x, y);
    }

    public static Dictionary<string, string> ParseTranslations(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
                result[parts[0].Replace('\\', '/')] = parts[1].Replace('\\', '/');
        }
        return result;
    }

    private static Vector3 ToVector(Formats.Common.C3Vector value) => new(value.X, value.Y, value.Z);
}
