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

    private const string Wrath335ShadowOpacityKey = "legacy_shadow_opacity_color";

    private static readonly string[] Wrath335CloudKeys =
    [
        "legacy_cloud_emissive_color", "legacy_cloud_body_color",
        "legacy_cloud_ambient_color", "legacy_int_band_13"
    ];

    private static readonly string[] FloatKeys =
    [
        "fog_end", "fog_scaler", "celestial_glow_through", "cloud_density",
        "legacy_float_band_4", "legacy_float_band_5"
    ];

    internal sealed record BandRow(int Id, int Num, int[] Time, double[] Data);

    internal sealed record BandDataset(
        IReadOnlyList<WorldLightingData> Snapshots,
        IReadOnlyDictionary<int, IWorldLightingTimeSampler> Samplers);

    private sealed class BandSampler(
        int paramId,
        (string Key, bool Color, (int Time, double Value)[] Keys)[] channels)
        : IWorldLightingTimeSampler
    {
        private readonly object _gate = new();
        private int _cachedTime = -1;
        private WorldLightingData? _cachedData;

        public WorldLightingData Sample(int time)
        {
            time = DayNight.NormalizeTime(time);
            lock (_gate)
            {
                if (_cachedTime != time)
                {
                    _cachedData = BuildSnapshot(paramId, channels, time, -1);
                    _cachedTime = time;
                }
                return _cachedData!;
            }
        }
    }

    internal static bool UsesLegacyBands(string buildName)
    {
        var parts = buildName.Split('.');
        return parts.Length == 4 &&
            int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var build) &&
            build <= LastBandBuild;
    }

    internal static BandDataset LoadDataset(
        IDBCDStorage intStorage,
        IDBCDStorage floatStorage,
        IEnumerable<int> lightParamIds,
        bool wrath335CloudBands) =>
        BuildDataset(ReadRows(intStorage, "LightIntBand"),
            ReadRows(floatStorage, "LightFloatBand"), lightParamIds,
            wrath335CloudBands);

    internal static IReadOnlyList<WorldLightingData> Build(
        IEnumerable<BandRow> intRows,
        IEnumerable<BandRow> floatRows,
        IEnumerable<int> lightParamIds) =>
        BuildDataset(intRows, floatRows, lightParamIds).Snapshots;

    internal static BandDataset BuildDataset(
        IEnumerable<BandRow> intRows,
        IEnumerable<BandRow> floatRows,
        IEnumerable<int> lightParamIds,
        bool wrath335CloudBands = true)
    {
        var colors = intRows.ToDictionary(static row => row.Id);
        var floats = floatRows.ToDictionary(static row => row.Id);
        var snapshots = new List<WorldLightingData>();
        var samplers = new Dictionary<int, IWorldLightingTimeSampler>();

        foreach (var paramId in lightParamIds.Where(static id => id > 0).Distinct())
        {
            var channels = new List<(string Key, bool Color, (int Time, double Value)[] Keys)>();
            AddChannels(colors, paramId, IntBandsPerParam, ColorKeys, true, channels,
                wrath335CloudBands);
            AddChannels(floats, paramId, FloatBandsPerParam, FloatKeys, false, channels);
            var channelArray = channels.ToArray();
            samplers.Add(paramId, new BandSampler(paramId, channelArray));

            var times = channelArray.SelectMany(static channel => channel.Keys.Select(static key => key.Time))
                .Distinct().Order().ToArray();
            if (times.Length == 0)
                Console.Error.WriteLine($"LightParams ID={paramId} has no timed LightIntBand/LightFloatBand entries.");
            foreach (var time in times)
                snapshots.Add(BuildSnapshot(paramId, channelArray, time, snapshots.Count));
        }

        return new BandDataset(snapshots, samplers);
    }

    private static WorldLightingData BuildSnapshot(
        int paramId,
        (string Key, bool Color, (int Time, double Value)[] Keys)[] channels,
        int time,
        int rowIndex)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["light_param_id"] = paramId,
            ["time"] = time
        };
        foreach (var channel in channels)
        {
            var value = channel.Color
                ? InterpolateColor(channel.Keys, time)
                : InterpolateScalar(channel.Keys, time);
            // DayNight_SampleLightFloatBandValue (3.3.5, 0x7EAEF0)
            // converts only float band 0 from client inches to yards.
            values[channel.Key] = channel.Key == "fog_end" && !channel.Color
                ? value / 36d
                : value;
        }
        return new WorldLightingData(
            rowIndex, rowIndex + 1, paramId, time, values,
            new Dictionary<string, string>(StringComparer.Ordinal));
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
        List<(string Key, bool Color, (int Time, double Value)[] Keys)> destination,
        bool wrath335CloudBands = false)
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
                if (time is < 0 or > DayNight.GameDayLength ||
                    !double.IsFinite(row.Data[index]))
                {
                    throw new InvalidDataException($"Light band row {row.Id} has an invalid time or value.");
                }
                samples[time % DayNight.GameDayLength] = row.Data[index];
            }
            var key = color && wrath335CloudBands
                ? band switch
                {
                    8 => Wrath335ShadowOpacityKey,
                    >= 10 and <= 13 => Wrath335CloudKeys[band - 10],
                    _ => keys[band]
                }
                : keys[band];
            destination.Add((key, color, samples.Select(static pair => (pair.Key, pair.Value)).ToArray()));
        }
    }

    private static double InterpolateScalar((int Time, double Value)[] keys, int time)
    {
        if (keys.Length == 1)
            return keys[0].Value;
        var (previous, next, alpha) = AdjacentKeys(keys, time);
        return keys[previous].Value + (keys[next].Value - keys[previous].Value) * alpha;
    }

    private static (int Previous, int Next, float Alpha) AdjacentKeys(
        (int Time, double Value)[] keys, int time)
    {
        var next = Array.FindIndex(keys, key => key.Time > time);
        if (next < 0)
            next = 0;
        var previous = next == 0 ? keys.Length - 1 : next - 1;
        var start = keys[previous].Time;
        var end = keys[next].Time + (next == 0 ? DayNight.GameDayLength : 0);
        var adjustedTime = time < start ? time + DayNight.GameDayLength : time;
        var alpha = (float)((adjustedTime - start) / (double)(end - start));
        return (previous, next, alpha);
    }

    private static double InterpolateColor((int Time, double Value)[] keys, int time)
    {
        var (previous, next, alpha) = keys.Length == 1
            ? (0, 0, 0f)
            : AdjacentKeys(keys, time);
        var start = unchecked((uint)(long)keys[previous].Value);
        var end = unchecked((uint)(long)keys[next].Value);
        var red = InterpolateComponent(start, end, alpha, 16);
        var green = InterpolateComponent(start, end, alpha, 8);
        var blue = InterpolateComponent(start, end, alpha, 0);
        // The client sampler (0x7EB070) always writes opaque alpha, including
        // when the source key's top byte is zero.
        return 0xff000000u | (red << 16) | (green << 8) | blue;
    }

    private static uint InterpolateComponent(uint start, uint end, float alpha, int shift)
    {
        var first = (start >> shift) & 0xff;
        var last = (end >> shift) & 0xff;
        // The client converts the interpolated float to an integer, truncating
        // each non-negative channel before packing it.
        var interpolated = (float)(first + (last - (double)first) * alpha);
        return (uint)Math.Clamp((int)interpolated, 0, 255);
    }
}
