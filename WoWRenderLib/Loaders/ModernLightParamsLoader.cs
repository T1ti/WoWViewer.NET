using DBCD.IO;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Database;

namespace WoWRenderLib.Loaders;

/// <summary>
/// Reads the liquid-alpha prefix of LightParams when WowLib's generated
/// schema does not match the installed WDC5 payload. The compatibility reader
/// is intentionally prefix-based: only the ID and four alpha fields needed by
/// the liquid renderer are bound, while later client-specific fields are
/// ignored.
/// </summary>
internal static class ModernLightParamsLoader
{
    // This layout is used by the Classic client currently supported by the
    // renderer (layout hash 0x51C96BAD). The first two fields are arrays and
    // therefore count as one structural field each in WDC5 metadata.
    private const uint CurrentLayoutHash = 0x51C96BAD;
    private const int CurrentIdFieldIndex = 2;
    private const int CurrentRequiredPrefixFields = 11;

    public static bool TryLoad(
        Fs.FileSystem fileSystem,
        long lightParamId,
        out IReadOnlyDictionary<string, double>? values,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var bytes = Db2TableLoader.TryReadBytes(
            fileSystem,
            "LightParams",
            out var readDiagnostic);
        if (bytes == null)
        {
            values = null;
            diagnostic = readDiagnostic;
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var parser = new DBParser(stream);
            ValidatePayload(parser);

            var rows = new Dictionary<int, LightParamsPrefix>();
            parser.PopulateRecords(rows);
            if (!rows.TryGetValue(checked((int)lightParamId), out var row))
            {
                values = null;
                diagnostic =
                    $"LightParams compatibility reader accepted layout hash " +
                    $"0x{parser.LayoutHash:X8} ({parser.RecordsCount} rows) but " +
                    $"did not find id={lightParamId}.";
                return false;
            }

            values = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["water_shallow_alpha"] = row.WaterShallowAlpha,
                ["water_deep_alpha"] = row.WaterDeepAlpha,
                ["ocean_shallow_alpha"] = row.OceanShallowAlpha,
                ["ocean_deep_alpha"] = row.OceanDeepAlpha
            };
            diagnostic = "Loaded client database table 'LightParams'.";
            return true;
        }
        catch (Exception exception)
        {
            values = null;
            diagnostic =
                $"LightParams compatibility reader rejected the payload read by " +
                $"{readDiagnostic}: {FormatException(exception)}";
            return false;
        }
    }

    private static void ValidatePayload(DBParser parser)
    {
        if (!string.Equals(parser.Identifier, "WDC5", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"LightParams compatibility reader expected WDC5, found " +
                $"'{parser.Identifier}'.");

        if (parser.LayoutHash != CurrentLayoutHash)
        {
            throw new InvalidDataException(
                $"LightParams compatibility reader has no explicit mapping for " +
                $"layout hash 0x{parser.LayoutHash:X8}; expected one of " +
                $"0x{CurrentLayoutHash:X8} for the supported client layout.");
        }

        if (parser.IdFieldIndex != CurrentIdFieldIndex)
        {
            throw new InvalidDataException(
                $"LightParams layout 0x{parser.LayoutHash:X8} reports ID field " +
                $"index {parser.IdFieldIndex}, but the explicit mapping requires " +
                $"index {CurrentIdFieldIndex}.");
        }

        if (parser.FieldsCount < CurrentRequiredPrefixFields)
        {
            throw new InvalidDataException(
                $"LightParams layout 0x{parser.LayoutHash:X8} stores " +
                $"{parser.FieldsCount} inline fields, but the alpha prefix " +
                $"requires {CurrentRequiredPrefixFields}.");
        }
    }

    private static string FormatException(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current != null; current = current.InnerException)
            parts.Add($"{current.GetType().Name}: {current.Message}");

        return string.Join(" -> ", parts);
    }

#pragma warning disable CS0649
    private sealed class LightParamsPrefix
    {
        public float[] OverrideCelestialSphere = new float[3];
        public float[] OverrideSunPosition = new float[3];
        public int Id;
        public int HighlightSky;
        public int LightSkyboxId;
        public int CloudTypeId;
        public float Glow;
        public float WaterShallowAlpha;
        public float WaterDeepAlpha;
        public float OceanShallowAlpha;
        public float OceanDeepAlpha;
    }
#pragma warning restore CS0649
}
