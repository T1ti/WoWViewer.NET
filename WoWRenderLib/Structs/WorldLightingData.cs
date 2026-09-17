using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>
/// Managed snapshot of one LightData row.  The row is intentionally retained
/// as a name/value map while the lighting path is being brought up so fields
/// that differ between client eras are not silently discarded.
/// </summary>
public sealed class WorldLightingData
{
    public WorldLightingData(
        int rowIndex,
        long id,
        long lightParamId,
        long time,
        IReadOnlyDictionary<string, double> numericValues,
        IReadOnlyDictionary<string, string> stringValues)
    {
        RowIndex = rowIndex;
        Id = id;
        LightParamId = lightParamId;
        Time = time;
        NumericValues = new ReadOnlyDictionary<string, double>(
            new Dictionary<string, double>(numericValues, StringComparer.Ordinal));
        StringValues = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(stringValues, StringComparer.Ordinal));

        DirectColorPacked = ReadPackedColor(NumericValues, "direct_color");
        AmbientColorPacked = ReadPackedColor(NumericValues, "ambient_color");
        OceanCloseColorPacked = ReadPackedColor(NumericValues, "ocean_close_color");
        OceanFarColorPacked = ReadPackedColor(NumericValues, "ocean_far_color");
        RiverCloseColorPacked = ReadPackedColor(NumericValues, "river_close_color");
        RiverFarColorPacked = ReadPackedColor(NumericValues, "river_far_color");
        DirectColor = UnpackRgb(DirectColorPacked);
        AmbientColor = UnpackRgb(AmbientColorPacked);
        OceanCloseColor = UnpackRgb(OceanCloseColorPacked);
        OceanFarColor = UnpackRgb(OceanFarColorPacked);
        RiverCloseColor = UnpackRgb(RiverCloseColorPacked);
        RiverFarColor = UnpackRgb(RiverFarColorPacked);
        HasLiquidColorData = HasNumericValue("ocean_close_color") &&
            HasNumericValue("ocean_far_color") &&
            HasNumericValue("river_close_color") &&
            HasNumericValue("river_far_color");
        WaterShallowAlpha = ReadAlpha(NumericValues, "water_shallow_alpha");
        WaterDeepAlpha = ReadAlpha(NumericValues, "water_deep_alpha");
        OceanShallowAlpha = ReadAlpha(NumericValues, "ocean_shallow_alpha");
        OceanDeepAlpha = ReadAlpha(NumericValues, "ocean_deep_alpha");
        HasLiquidAlphaData = HasNumericValue("water_shallow_alpha") &&
            HasNumericValue("water_deep_alpha") &&
            HasNumericValue("ocean_shallow_alpha") &&
            HasNumericValue("ocean_deep_alpha");
    }

    public int RowIndex { get; }
    public long Id { get; }
    public long LightParamId { get; }
    public long Time { get; }

    /// <summary>All numeric values present in the selected LightData row.</summary>
    public IReadOnlyDictionary<string, double> NumericValues { get; }

    /// <summary>All string values present in the selected LightData row.</summary>
    public IReadOnlyDictionary<string, string> StringValues { get; }

    /// <summary>Packed color value from LightData.direct_color.</summary>
    public uint DirectColorPacked { get; }

    /// <summary>Packed color value from LightData.ambient_color.</summary>
    public uint AmbientColorPacked { get; }

    /// <summary>LightData.direct_color converted to normalized RGB for shader inputs.</summary>
    public Vector3 DirectColor { get; }

    /// <summary>LightData.ambient_color converted to normalized RGB for shader inputs.</summary>
    public Vector3 AmbientColor { get; }

    /// <summary>Packed LightData.ocean_close_color value.</summary>
    public uint OceanCloseColorPacked { get; }

    /// <summary>Packed LightData.ocean_far_color value.</summary>
    public uint OceanFarColorPacked { get; }

    /// <summary>Packed LightData.river_close_color value.</summary>
    public uint RiverCloseColorPacked { get; }

    /// <summary>Packed LightData.river_far_color value.</summary>
    public uint RiverFarColorPacked { get; }

    /// <summary>LightData.ocean_close_color converted to normalized RGB.</summary>
    public Vector3 OceanCloseColor { get; }

    /// <summary>LightData.ocean_far_color converted to normalized RGB.</summary>
    public Vector3 OceanFarColor { get; }

    /// <summary>LightData.river_close_color converted to normalized RGB.</summary>
    public Vector3 RiverCloseColor { get; }

    /// <summary>LightData.river_far_color converted to normalized RGB.</summary>
    public Vector3 RiverFarColor { get; }

    /// <summary>
    /// True when all four liquid close/far color columns were present in the
    /// selected row. Presence is tracked separately from the packed value so a
    /// valid black color is not confused with a missing column.
    /// </summary>
    public bool HasLiquidColorData { get; }

    /// <summary>LightParams.WaterShallowAlpha.</summary>
    public float WaterShallowAlpha { get; }

    /// <summary>LightParams.WaterDeepAlpha.</summary>
    public float WaterDeepAlpha { get; }

    /// <summary>LightParams.OceanShallowAlpha.</summary>
    public float OceanShallowAlpha { get; }

    /// <summary>LightParams.OceanDeepAlpha.</summary>
    public float OceanDeepAlpha { get; }

    /// <summary>
    /// True when all four LightParams liquid alpha columns were read. A
    /// missing LightParams table is supported by the renderer's alpha defaults.
    /// </summary>
    public bool HasLiquidAlphaData { get; }

    public bool TryGetNumeric(string name, out double value)
    {
        ArgumentNullException.ThrowIfNull(name);
        return NumericValues.TryGetValue(name, out value);
    }

    private bool HasNumericValue(string name) =>
        NumericValues.TryGetValue(name, out var value) && double.IsFinite(value);

    /// <summary>
    /// Converts the packed color representation used by LightData to normalized
    /// RGB. WoW stores these packed colors in BGRA order, so the high three
    /// bytes are emitted as RGB and the low byte is blue. The high byte is
    /// reserved/alpha for LightData colors and is deliberately ignored here;
    /// liquid alpha comes from LightParams.
    /// </summary>
    public static Vector3 UnpackRgb(uint packed) => new(
        ((packed >> 16) & 0xff) / 255f,
        ((packed >> 8) & 0xff) / 255f,
        (packed & 0xff) / 255f);

    public override string ToString()
    {
        var numeric = string.Join(
            ", ",
            NumericValues
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair =>
                    $"{pair.Key}={pair.Value.ToString(CultureInfo.InvariantCulture)}"));
        var strings = string.Join(
            ", ",
            StringValues
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}=\"{pair.Value}\""));
        return $"LightData row {RowIndex} (Id={Id}, LightParamId={LightParamId}, Time={Time}): " +
            string.Join(", ", new[] { numeric, strings }.Where(static value => value.Length > 0));
    }

    private static uint ReadPackedColor(
        IReadOnlyDictionary<string, double> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value) || !double.IsFinite(value))
            return 0;

        return unchecked((uint)(long)Math.Round(value));
    }

    private static float ReadAlpha(
        IReadOnlyDictionary<string, double> values,
        string name)
    {
        return values.TryGetValue(name, out var value) && double.IsFinite(value)
            ? Math.Clamp((float)value, 0f, 1f)
            : 1f;
    }
}
