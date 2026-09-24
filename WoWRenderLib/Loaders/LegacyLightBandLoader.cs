using System.Globalization;
using DBCD;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

/// <summary>Converts pre-LightData color and float bands into timed lighting snapshots.</summary>
internal static class LegacyLightBandLoader
{
    internal const int LastBandBuild = 15595;
    private const int IntBandsPerParam = 18;
    private const int FloatBandsPerParam = 6;
    private const int MaxEntries = 16;

    // The pre-5.x band order is documented by wowdev.wiki/DB/LightIntBand.
    // WoWDBDefs supplies the actual DBC column names: ID, Num, Time, Data.
    private static readonly string[] ColorKeys =
    [
        "direct_color", "ambient_color", "sky_top_color", "sky_middle_color",
        "sky_band_1_color", "sky_band_2_color", "sky_smog_color", "sky_fog_color",
        "shadow_opacity", "sun_color", "cloud_sun_color", "cloud_emissive_color",
        "cloud_layer_1_ambient_color", "cloud_layer_2_ambient_color",
        "ocean_close_color", "ocean_far_color", "river_close_color", "river_far_color"
    ];

    private static readonly string[] FloatKeys =
    [
        "fog_end", "fog_scaler", "celestial_glow_through", "cloud_density",
        "legacy_float_band_4", "legacy_float_band_5"
    ];

    internal sealed record BandRow(int Id, int Num, int[] Time, double[] Data);

    internal static bool UsesLegacyBands(string buildName)
    {
        var parts = buildName.Split('.');
        return parts.Length == 4 &&
            int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var build) &&
            build <= LastBandBuild;
    }

    internal static IReadOnlyList<WorldLightingData> Load(
        IDBCDStorage intStorage,
        IDBCDStorage floatStorage,
        IEnumerable<int> lightParamIds) =>
        Build(ReadRows(intStorage, "LightIntBand"), ReadRows(floatStorage, "LightFloatBand"), lightParamIds);

    internal static IReadOnlyList<WorldLightingData> Build(
        IEnumerable<BandRow> intRows,
        IEnumerable<BandRow> floatRows,
        IEnumerable<int> lightParamIds)
    {
        var colors = intRows.ToDictionary(static row => row.Id);
        var floats = floatRows.ToDictionary(static row => row.Id);
        var snapshots = new List<WorldLightingData>();

        foreach (var paramId in lightParamIds.Where(static id => id > 0).Distinct())
        {
            var channels = new List<(string Key, bool Color, (int Time, double Value)[] Keys)>();
            AddChannels(colors, paramId, IntBandsPerParam, ColorKeys, true, channels);
            AddChannels(floats, paramId, FloatBandsPerParam, FloatKeys, false, channels);

            var times = channels.SelectMany(static channel => channel.Keys.Select(static key => key.Time))
                .Distinct().Order().ToArray();
            if (times.Length == 0)
                Console.Error.WriteLine($"LightParams ID={paramId} has no timed LightIntBand/LightFloatBand entries.");
            foreach (var time in times)
            {
                var values = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["light_param_id"] = paramId,
                    ["time"] = time
                };
                foreach (var channel in channels)
                    values[channel.Key] = channel.Color
                        ? InterpolateColor(channel.Keys, time)
                        : InterpolateScalar(channel.Keys, time);

                var rowIndex = snapshots.Count;
                snapshots.Add(new WorldLightingData(
                    rowIndex, rowIndex + 1, paramId, time, values,
                    new Dictionary<string, string>(StringComparer.Ordinal)));
            }
        }

        return snapshots;
    }

    private static IReadOnlyList<BandRow> ReadRows(IDBCDStorage storage, string tableName)
    {
        var names = storage.AvailableColumns.ToDictionary(
            static name => name, static name => name, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "ID", "Num", "Time", "Data" })
        {
            if (!names.ContainsKey(required))
                throw new InvalidDataException($"{tableName} is missing WoWDBDefs column '{required}'.");
        }

        return storage.Values.Select(row =>
        {
            try
            {
                var id = Convert.ToInt32(row[names["ID"]], CultureInfo.InvariantCulture);
                var num = Convert.ToInt32(row[names["Num"]], CultureInfo.InvariantCulture);
                var times = ((Array)row[names["Time"]]).Cast<object>()
                    .Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture)).ToArray();
                var data = ((Array)row[names["Data"]]).Cast<object>()
                    .Select(value => Convert.ToDouble(value, CultureInfo.InvariantCulture)).ToArray();
                return new BandRow(id, num, times, data);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"{tableName} entry {row.ID} has an unreadable ID, Num, Time, or Data column: " +
                    exception.Message, exception);
            }
        }).ToArray();
    }

    private static void AddChannels(
        IReadOnlyDictionary<int, BandRow> rows,
        int paramId,
        int count,
        IReadOnlyList<string> keys,
        bool color,
        List<(string Key, bool Color, (int Time, double Value)[] Keys)> destination)
    {
        var firstId = checked(paramId * count - (count - 1));
        for (var band = 0; band < count; band++)
        {
            if (!rows.TryGetValue(firstId + band, out var row))
            {
                Console.Error.WriteLine(
                    $"{(color ? "LightIntBand" : "LightFloatBand")} entry ID={firstId + band} " +
                    $"is missing for LightParams ID={paramId}.");
                continue;
            }
            if (row.Num is < 0 or > MaxEntries ||
                row.Time.Length < row.Num || row.Data.Length < row.Num)
            {
                throw new InvalidDataException($"Light band row {row.Id} has invalid Num/Time/Data lengths.");
            }
            if (row.Num == 0)
                continue;

            var samples = new SortedDictionary<int, double>();
            for (var index = 0; index < row.Num; index++)
            {
                var time = row.Time[index];
                if (time is < 0 or > WorldLightingCatalog.GameDayLength ||
                    !double.IsFinite(row.Data[index]))
                {
                    throw new InvalidDataException($"Light band row {row.Id} has an invalid time or value.");
                }
                samples[time % WorldLightingCatalog.GameDayLength] = row.Data[index];
            }
            destination.Add((keys[band], color, samples.Select(static pair => (pair.Key, pair.Value)).ToArray()));
        }
    }

    private static double InterpolateScalar((int Time, double Value)[] keys, int time)
    {
        if (keys.Length == 1)
            return keys[0].Value;
        var (previous, next, alpha) = AdjacentKeys(keys, time);
        return keys[previous].Value + (keys[next].Value - keys[previous].Value) * alpha;
    }

    private static (int Previous, int Next, double Alpha) AdjacentKeys(
        (int Time, double Value)[] keys, int time)
    {
        var next = Array.FindIndex(keys, key => key.Time > time);
        if (next < 0)
            next = 0;
        var previous = next == 0 ? keys.Length - 1 : next - 1;
        var start = keys[previous].Time;
        var end = keys[next].Time + (next == 0 ? WorldLightingCatalog.GameDayLength : 0);
        var adjustedTime = time < start ? time + WorldLightingCatalog.GameDayLength : time;
        var alpha = (adjustedTime - start) / (double)(end - start);
        return (previous, next, alpha);
    }

    private static double InterpolateColor((int Time, double Value)[] keys, int time)
    {
        var (previous, next, alpha) = keys.Length == 1
            ? (0, 0, 0d)
            : AdjacentKeys(keys, time);
        var start = unchecked((uint)(long)keys[previous].Value);
        var end = unchecked((uint)(long)keys[next].Value);
        var red = InterpolateComponent(start, end, alpha, 16);
        var green = InterpolateComponent(start, end, alpha, 8);
        var blue = InterpolateComponent(start, end, alpha, 0);
        return (red << 16) | (green << 8) | blue;
    }

    private static uint InterpolateComponent(uint start, uint end, double alpha, int shift)
    {
        var first = (start >> shift) & 0xff;
        var last = (end >> shift) & 0xff;
        return (uint)Math.Clamp(Math.Round(first + (last - (double)first) * alpha), 0d, 255d);
    }
}
