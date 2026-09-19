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
        SkyTopColor = ReadColor(NumericValues, "sky_top_color");
        SkyMiddleColor = ReadColor(NumericValues, "sky_middle_color");
        SkyBand1Color = ReadColor(NumericValues, "sky_band_1_color");
        SkyBand2Color = ReadColor(NumericValues, "sky_band_2_color");
        SkySmogColor = ReadColor(NumericValues, "sky_smog_color");
        SkyFogColor = ReadColor(NumericValues, "sky_fog_color");
        SunColor = ReadColor(NumericValues, "sun_color");
        CloudSunColor = ReadColor(NumericValues, "cloud_sun_color");
        CloudEmissiveColor = ReadColor(NumericValues, "cloud_emissive_color");
        CloudLayer1AmbientColor = ReadColor(NumericValues, "cloud_layer_1_ambient_color");
        CloudLayer2AmbientColor = ReadColor(NumericValues, "cloud_layer_2_ambient_color");
        HasSkyColorData = HasNumericValue("sky_top_color") &&
            HasNumericValue("sky_middle_color") &&
            HasNumericValue("sky_band_1_color") &&
            HasNumericValue("sky_band_2_color") &&
            HasNumericValue("sky_smog_color") &&
            HasNumericValue("sky_fog_color");
        HasLiquidColorData = HasNumericValue("ocean_close_color") &&
            HasNumericValue("ocean_far_color") &&
            HasNumericValue("river_close_color") &&
            HasNumericValue("river_far_color") &&
            (OceanCloseColorPacked != 0 ||
             OceanFarColorPacked != 0 ||
             RiverCloseColorPacked != 0 ||
             RiverFarColorPacked != 0);
        HasSunCloudData = HasAllNumericValues(
            "sun_color",
            "cloud_sun_color",
            "cloud_emissive_color",
            "cloud_layer_1_ambient_color",
            "cloud_layer_2_ambient_color");
        ShadowOpacity = ReadScalar(NumericValues, "shadow_opacity");
        FogEnd = ReadScalar(NumericValues, "fog_end");
        FogScaler = ReadScalar(NumericValues, "fog_scaler");
        CloudDensity = ReadScalar(NumericValues, "cloud_density");
        FogDensity = ReadScalar(NumericValues, "fog_density");
        FogHeight = ReadScalar(NumericValues, "fog_height");
        FogHeightScaler = ReadScalar(NumericValues, "fog_height_scaler");
        FogHeightDensity = ReadScalar(NumericValues, "fog_height_density");
        FogZScalar = ReadScalar(NumericValues, "fog_z_scalar");
        MainFogStartDistance = ReadScalar(NumericValues, "main_fog_start_dist");
        MainFogEndDistance = ReadScalar(NumericValues, "main_fog_end_dist");
        SunFogAngle = ReadScalar(NumericValues, "sun_fog_angle");
        EndFogColor = ReadColor(NumericValues, "end_fog_color");
        EndFogColorDistance = ReadScalar(NumericValues, "end_fog_color_distance");
        FogStartOffset = ReadScalar(NumericValues, "fog_start_offset");
        SunFogColor = ReadColor(NumericValues, "sun_fog_color");
        SunFogStrength = ReadScalar(NumericValues, "sun_fog_strength");
        FogHeightColor = ReadColor(NumericValues, "fog_height_color");
        EndFogHeightColor = ReadColor(NumericValues, "end_fog_height_color");
        GroundAmbientColor = ReadColor(NumericValues, "ground_ambient_color");
        HorizonAmbientColor = ReadColor(NumericValues, "horizon_ambient_color");
        FogHeightCoefficients = ReadVector4(NumericValues, "fog_height_coefficients");
        MainFogCoefficients = ReadVector4(NumericValues, "main_fog_coefficients");
        HeightDensityFogCoefficients = ReadVector4(NumericValues, "height_density_fog_coeff");
        ColorGradingFileDataId = ReadInteger(NumericValues, "color_grading_file_data_id");
        DarkerColorGradingFileDataId = ReadInteger(NumericValues, "darker_color_grading_file_data_id");
        HasFogData = HasAnyNumericValue(
            "fog_end", "fog_scaler", "cloud_density", "fog_density", "fog_height",
            "fog_height_scaler", "fog_height_density", "fog_z_scalar",
            "main_fog_start_dist", "main_fog_end_dist", "sun_fog_angle",
            "end_fog_color", "end_fog_color_distance", "fog_start_offset",
            "sun_fog_color", "sun_fog_strength", "fog_height_color",
            "end_fog_height_color", "fog_height_coefficients_0",
            "main_fog_coefficients_0", "height_density_fog_coeff_0");
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
    /// selected row and at least one value is non-zero. Current clients retain
    /// these columns but use an all-zero quartet to mean that no LightData
    /// palette overrides the liquid material colors.
    /// </summary>
    public bool HasLiquidColorData { get; }

    public Vector3 SkyTopColor { get; }
    public Vector3 SkyMiddleColor { get; }
    public Vector3 SkyBand1Color { get; }
    public Vector3 SkyBand2Color { get; }
    public Vector3 SkySmogColor { get; }
    public Vector3 SkyFogColor { get; }
    public bool HasSkyColorData { get; }
    public Vector3 SunColor { get; }
    public Vector3 CloudSunColor { get; }
    public Vector3 CloudEmissiveColor { get; }
    public Vector3 CloudLayer1AmbientColor { get; }
    public Vector3 CloudLayer2AmbientColor { get; }
    public bool HasSunCloudData { get; }

    public float ShadowOpacity { get; }
    public float FogEnd { get; }
    public float FogScaler { get; }
    public float CloudDensity { get; }
    public float FogDensity { get; }
    public float FogHeight { get; }
    public float FogHeightScaler { get; }
    public float FogHeightDensity { get; }
    public float FogZScalar { get; }
    public float MainFogStartDistance { get; }
    public float MainFogEndDistance { get; }
    public float SunFogAngle { get; }
    public Vector3 EndFogColor { get; }
    public float EndFogColorDistance { get; }
    public float FogStartOffset { get; }
    public Vector3 SunFogColor { get; }
    public float SunFogStrength { get; }
    public Vector3 FogHeightColor { get; }
    public Vector3 EndFogHeightColor { get; }
    public Vector3 GroundAmbientColor { get; }
    public Vector3 HorizonAmbientColor { get; }
    public Vector4 FogHeightCoefficients { get; }
    public Vector4 MainFogCoefficients { get; }
    public Vector4 HeightDensityFogCoefficients { get; }
    public long ColorGradingFileDataId { get; }
    public long DarkerColorGradingFileDataId { get; }
    public bool HasFogData { get; }

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

    private bool HasAllNumericValues(params string[] names) =>
        names.All(HasNumericValue);

    private bool HasAnyNumericValue(params string[] names) =>
        names.Any(HasNumericValue);

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

    private static Vector3 ReadColor(
        IReadOnlyDictionary<string, double> values,
        string name) => UnpackRgb(ReadPackedColor(values, name));

    private static float ReadScalar(
        IReadOnlyDictionary<string, double> values,
        string name) => values.TryGetValue(name, out var value) && double.IsFinite(value)
            ? (float)value
            : 0f;

    private static long ReadInteger(
        IReadOnlyDictionary<string, double> values,
        string name) => values.TryGetValue(name, out var value) && double.IsFinite(value)
            ? checked((long)Math.Round(value))
            : 0L;

    private static Vector4 ReadVector4(
        IReadOnlyDictionary<string, double> values,
        string name) => new(
        ReadScalar(values, $"{name}_0"),
        ReadScalar(values, $"{name}_1"),
        ReadScalar(values, $"{name}_2"),
        ReadScalar(values, $"{name}_3"));

    private static float ReadAlpha(
        IReadOnlyDictionary<string, double> values,
        string name)
    {
        return values.TryGetValue(name, out var value) && double.IsFinite(value)
            ? Math.Clamp((float)value, 0f, 1f)
            : 1f;
    }
}
