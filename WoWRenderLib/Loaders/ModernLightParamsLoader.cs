using DBCD.IO;
using DBCD.IO.Attributes;
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
    // The first two fields are arrays and therefore count as one structural
    // field each in WDC5 metadata. Only validate the prefix consumed below;
    // client versions may append or alter unrelated fields after it.
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

            var alphas = ReadAlphaRow(parser, checked((int)lightParamId));
            if (alphas == null)
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
                ["water_shallow_alpha"] = alphas.Value.WaterShallowAlpha,
                ["water_deep_alpha"] = alphas.Value.WaterDeepAlpha,
                ["ocean_shallow_alpha"] = alphas.Value.OceanShallowAlpha,
                ["ocean_deep_alpha"] = alphas.Value.OceanDeepAlpha
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

        if (parser.FieldsCount < CurrentRequiredPrefixFields)
        {
            throw new InvalidDataException(
                $"LightParams layout 0x{parser.LayoutHash:X8} stores " +
                $"{parser.FieldsCount} inline fields, but the alpha prefix " +
                $"requires {CurrentRequiredPrefixFields}.");
        }
    }

    private static LiquidAlphas? ReadAlphaRow(DBParser parser, int lightParamId)
    {
        if (parser.Flags.HasFlag(DB2Flags.Index) && parser.IdFieldIndex == 0)
        {
            var rows = new Dictionary<int, NonInlineIdLightParamsPrefix>();
            parser.PopulateRecords(rows);
            return rows.TryGetValue(lightParamId, out var row)
                ? new LiquidAlphas(
                    row.WaterShallowAlpha,
                    row.WaterDeepAlpha,
                    row.OceanShallowAlpha,
                    row.OceanDeepAlpha)
                : null;
        }

        if (!parser.Flags.HasFlag(DB2Flags.Index) && parser.IdFieldIndex == 2)
        {
            var rows = new Dictionary<int, InlineIdLightParamsPrefix>();
            parser.PopulateRecords(rows);
            return rows.TryGetValue(lightParamId, out var row)
                ? new LiquidAlphas(
                    row.WaterShallowAlpha,
                    row.WaterDeepAlpha,
                    row.OceanShallowAlpha,
                    row.OceanDeepAlpha)
                : null;
        }

        throw new InvalidDataException(
            $"LightParams layout 0x{parser.LayoutHash:X8} has unsupported ID " +
            $"storage (id_field_index={parser.IdFieldIndex}, flags={parser.Flags}). " +
            "Supported mappings are a non-inline ID at field 0 and an inline " +
            "ID after the two override-vector fields.");
    }

    private static string FormatException(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current != null; current = current.InnerException)
            parts.Add($"{current.GetType().Name}: {current.Message}");

        return string.Join(" -> ", parts);
    }

#pragma warning disable CS0649
    private readonly record struct LiquidAlphas(
        float WaterShallowAlpha,
        float WaterDeepAlpha,
        float OceanShallowAlpha,
        float OceanDeepAlpha);

    private sealed class InlineIdLightParamsPrefix
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

    private sealed class NonInlineIdLightParamsPrefix
    {
        [Index(true)]
        public int Id;
        public float[] OverrideCelestialSphere = new float[3];
        public float[] OverrideSunPosition = new float[3];
        public byte HighlightSky;
        public ushort LightSkyboxId;
        public byte CloudTypeId;
        public float Glow;
        public float WaterShallowAlpha;
        public float WaterDeepAlpha;
        public float OceanShallowAlpha;
        public float OceanDeepAlpha;
    }
#pragma warning restore CS0649
}
