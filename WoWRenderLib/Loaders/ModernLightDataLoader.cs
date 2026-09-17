using System.Reflection;
using DBCD.IO;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Database;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

/// <summary>
/// Reads LightData when the installed WowLib schema is older than the payload.
/// DBCD.IO supplies the structural WDC5 decoder. The renderer currently uses
/// only the prefix through the four liquid colors needed by the renderer. The
/// remaining version-specific fields after that prefix are deliberately
/// ignored. This keeps the compatibility path usable when a client adds,
/// removes, or renames fields that the renderer does not consume.
/// </summary>
internal static class ModernLightDataLoader
{
    // LightData 1.60.1.69876 stores these values in the first 20 inline cells.
    // The eleven sky/color cells between AmbientColor and OceanCloseColor are
    // structural slots: DBCD.IO must consume them to reach the four liquid
    // colors, but they are not semantic requirements for this renderer.
    private const int RequiredInlineFieldCount = 20;

    private static readonly string[] PrefixMemberNames =
    [
        "Id",
        "LightParamId",
        "Time",
        "DirectColor",
        "AmbientColor",
        "SkyTopColor",
        "SkyMiddleColor",
        "SkyBand1Color",
        "SkyBand2Color",
        "SkySmogColor",
        "SkyFogColor",
        "SunColor",
        "CloudSunColor",
        "CloudEmissiveColor",
        "CloudLayer1AmbientColor",
        "CloudLayer2AmbientColor",
        "OceanCloseColor",
        "OceanFarColor",
        "RiverCloseColor",
        "RiverFarColor"
    ];

    private static readonly string[] UsedColumnNames =
    [
        "id",
        "light_param_id",
        "time",
        "direct_color",
        "ambient_color",
        "ocean_close_color",
        "ocean_far_color",
        "river_close_color",
        "river_far_color"
    ];

    static ModernLightDataLoader() => ValidateManagedLayout();

    public static bool TryLoad(
        Fs.FileSystem fileSystem,
        long lightParamId,
        long time,
        out WorldLightingData? lighting,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var bytes = Db2TableLoader.TryReadBytes(
            fileSystem,
            "LightData",
            out var readDiagnostic);
        if (bytes == null)
        {
            lighting = null;
            diagnostic = readDiagnostic;
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var parser = new DBParser(stream);
            ValidatePayload(parser);

            var rows = new Dictionary<int, ModernLightDataRow>();
            parser.PopulateRecords(rows);

            var rowIndex = 0;
            foreach (var row in rows.Values)
            {
                // LightParamID and Time are u16 in the LightData DBD layouts.
                // DBCD.IO preserves the 32-bit palette cell; the high word is
                // the cell's type marker, so compare the actual u16 value.
                var rowLightParamId = unchecked((ushort)row.LightParamId);
                var rowTime = unchecked((ushort)row.Time);
                if (rowLightParamId != lightParamId || rowTime != time)
                {
                    rowIndex++;
                    continue;
                }

                var numeric = ReadNumericValues(row);
                if (UsedColumnNames
                    .Skip(1)
                    .Any(columnName => !numeric.ContainsKey(columnName)))
                {
                    throw new InvalidDataException(
                        "LightData compatibility row is missing required " +
                        $"numeric columns [{string.Join(", ", UsedColumnNames.Skip(1))}]. " +
                        "Update the version-specific LightData prefix mapping.");
                }

                lighting = new WorldLightingData(
                    rowIndex,
                    row.Id,
                    rowLightParamId,
                    rowTime,
                    numeric,
                    new Dictionary<string, string>(StringComparer.Ordinal));
                diagnostic = "Loaded client database table 'LightData'.";
                return true;
            }

            lighting = null;
            diagnostic =
                $"LightData compatibility reader accepted WDC5 layout hash " +
                $"0x{parser.LayoutHash:X8} ({parser.RecordsCount} rows) but did not " +
                $"find light_param_id={lightParamId}, time={time}.";
            return false;
        }
        catch (Exception exception)
        {
            lighting = null;
            diagnostic =
                $"LightData compatibility reader rejected the payload read by " +
                $"{readDiagnostic}: {FormatException(exception)}";
            return false;
        }
    }

    private static void ValidatePayload(DBParser parser)
    {
        if (!string.Equals(parser.Identifier, "WDC5", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"LightData compatibility reader expected WDC5, found " +
                $"'{parser.Identifier}'.");

        if (parser.IdFieldIndex != 0)
        {
            throw new InvalidDataException(
                "LightData compatibility reader requires the version-specific " +
                $"LightData prefix to have ID field index 0, but the payload " +
                $"reports ID field index {parser.IdFieldIndex}. Add an explicit " +
                "prefix mapping for this client layout.");
        }

        if (parser.FieldsCount < RequiredInlineFieldCount)
        {
            throw new InvalidDataException(
                "LightData compatibility reader requires at least " +
                $"{RequiredInlineFieldCount} inline fields for the columns " +
                $"[{string.Join(", ", UsedColumnNames)}], but the payload " +
                $"has {parser.FieldsCount}. Add an explicit prefix mapping for " +
                "this client layout.");
        }
    }

    private static Dictionary<string, double> ReadNumericValues(
        ModernLightDataRow row)
    {
        // DBCD.IO exposes the u16 LightParamID/Time cells as 32-bit values
        // with a palette marker in the high word. Normalize those two values
        // before exposing them to the renderer; colors remain raw 32-bit
        // packed values so WorldLightingData can unpack their RGB channels.
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [UsedColumnNames[1]] = unchecked((ushort)row.LightParamId),
            [UsedColumnNames[2]] = unchecked((ushort)row.Time),
            [UsedColumnNames[3]] = row.DirectColor,
            [UsedColumnNames[4]] = row.AmbientColor,
            [UsedColumnNames[5]] = row.OceanCloseColor,
            [UsedColumnNames[6]] = row.OceanFarColor,
            [UsedColumnNames[7]] = row.RiverCloseColor,
            [UsedColumnNames[8]] = row.RiverFarColor
        };
    }

    private static void ValidateManagedLayout()
    {
        var fields = typeof(ModernLightDataRow).GetFields(
            BindingFlags.Instance | BindingFlags.Public);
        if (PrefixMemberNames.Length != RequiredInlineFieldCount)
        {
            throw new InvalidDataException(
                "LightData compatibility schema mapping must declare exactly " +
                $"{RequiredInlineFieldCount} structural prefix fields, but declares " +
                $"{PrefixMemberNames.Length}.");
        }

        if (fields.Length != RequiredInlineFieldCount)
        {
            throw new InvalidDataException(
                "LightData compatibility schema mapping declares " +
                $"{RequiredInlineFieldCount} structural prefix fields, but the " +
                $"managed row has {fields.Length}. Update the version-specific " +
                "prefix mapping.");
        }

        for (var index = 0; index < fields.Length; index++)
        {
            if (!string.Equals(
                    fields[index].Name,
                    PrefixMemberNames[index],
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "LightData compatibility schema field " +
                    $"{index} is '{fields[index].Name}', expected " +
                    $"'{PrefixMemberNames[index]}'. Update the version-specific " +
                    "field mapping instead of resolving names automatically.");
            }
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
    private sealed class ModernLightDataRow
    {
        public int Id;
        public int LightParamId;
        public int Time;
        public int DirectColor;
        public int AmbientColor;
        public int SkyTopColor;
        public int SkyMiddleColor;
        public int SkyBand1Color;
        public int SkyBand2Color;
        public int SkySmogColor;
        public int SkyFogColor;
        public int SunColor;
        public int CloudSunColor;
        public int CloudEmissiveColor;
        public int CloudLayer1AmbientColor;
        public int CloudLayer2AmbientColor;
        public int OceanCloseColor;
        public int OceanFarColor;
        public int RiverCloseColor;
        public int RiverFarColor;
    }
#pragma warning restore CS0649
}
