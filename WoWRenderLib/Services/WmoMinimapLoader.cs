using System.Numerics;
using System.Text;
using WoWLib;
using Formats = WoWLib.Formats;

namespace WoWRenderLib.Services;

public sealed record WmoMinimapGroup(int GroupIndex, Vector3 Minimum, Vector3 Maximum,
    byte[] Pixels, int Width, int Height, int OffsetX = 0, int OffsetY = 0);
public sealed record WmoMinimapData(Vector3 Position, Vector3 Rotation, float Scale,
    IReadOnlyList<WmoMinimapGroup> Groups, int MissingTextures);

public interface IWmoMinimapLoader
{
    WmoMinimapData Load(uint wdtFileDataId, CancellationToken token);
}

/// <summary>CPU-only wowlib adapter. No renderer initialization or UI objects are needed.</summary>
public sealed class WmoMinimapLoader : IWmoMinimapLoader
{
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
        var fs = WowlibFileSystem.Current;
        using var wdt = Formats.WDT.WDT.ForVersion(fs.Version);
        wdt.Read(fs, new FileKey(new FileDataId(wdtFileDataId)));
        if ((Convert.ToUInt32(wdt.Root.Header.Flags) & 1) == 0 || wdt.Root.GlobalWmo.Count == 0)
            throw new InvalidDataException("The WDT has no global WMO placement.");
        var placement = wdt.Root.GlobalWmo[0];
        var nameBlock = wdt.Root.GlobalWmoName;
        var path = nameBlock.Empty ? string.Empty : nameBlock.At(0);
        var key = string.IsNullOrWhiteSpace(path)
            ? new FileKey(new FileDataId(placement.NameId)) : new FileKey(path);
        if (string.IsNullOrWhiteSpace(path)) Listfile.TryGetFilename(placement.NameId, out path);
        if (string.IsNullOrWhiteSpace(path))
            throw new FileNotFoundException("The global WMO filename is unavailable in MWMO and the listfile.");

        token.ThrowIfCancellationRequested();
        // MOGI contains each source group's local bounds. Avoid loading meshes/materials.
        using var root = Formats.WMO.Root.WMORoot.ForVersion(fs.Version);
        root.Read(fs.ReadFile(key));
        var groups = new List<WmoMinimapGroup>();
        var missing = 0;
        Dictionary<string, string>? translations = null;
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
                        foreach (var candidate in new[] { $"world/minimaps/{logical}", logical })
                            if (fs.Exists(new FileKey(candidate))) { texturePath = candidate; break; }
                        if (texturePath != null) break;
                    }
                    if (texturePath == null)
                    {
                        if (translations == null)
                        {
                            translations = new(StringComparer.OrdinalIgnoreCase);
                            var translationKey = new FileKey("world/minimaps/md5translate.trs");
                            if (fs.Exists(translationKey))
                                translations = ParseTranslations(Encoding.UTF8.GetString(fs.ReadFile(translationKey)));
                        }
                        foreach (var logical in logicalCandidates)
                            if (translations.TryGetValue(logical, out var hashedPath))
                            {
                                texturePath = hashedPath.Contains('/') ? hashedPath : $"world/minimaps/{hashedPath}";
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
