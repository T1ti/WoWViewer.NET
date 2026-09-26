using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>A spatial Light.db2 row used for map and local-volume selection.</summary>
public sealed record WorldLightDefinition(
    int Id,
    int MapId,
    Vector3 WorldPosition,
    float FalloffStart,
    float FalloffEnd,
    IReadOnlyList<int> LightParamIds)
{
    public int ExteriorLightParamId => LightParamIds.Count == 0 ? 0 : LightParamIds[0];
    public bool IsDefault => WorldPosition.LengthSquared() < 0.000001f;
}

/// <summary>A ZoneLight.db2 row and its ordered ZoneLightPoint.db2 polygon.</summary>
public sealed record ZoneLightDefinition(
    int Id,
    string Name,
    int MapId,
    int LightId,
    int Priority,
    float MinimumZ,
    float MaximumZ,
    IReadOnlyList<Vector2> Points);

/// <summary>Static LightParams values consumed by the currently implemented passes.</summary>
public readonly record struct WorldLightParams(
    float WaterShallowAlpha,
    float WaterDeepAlpha,
    float OceanShallowAlpha,
    float OceanDeepAlpha,
    bool HasLiquidAlphaData,
    int LightSkyboxId = 0,
    bool HighlightSky = false);

public enum WorldLightingSourceKind
{
    Global,
    Zone,
    Local
}

/// <summary>One light source and its current contribution to the final blend.</summary>
public readonly record struct WorldLightingContribution(
    WorldLightingSourceKind Kind,
    int LightId,
    int LightParamId,
    float Weight,
    int ZoneLightId = 0,
    string? ZoneName = null);

public sealed record WorldSkyboxDefinition(
    int Id,
    string Name,
    int Flags,
    uint SkyboxFileDataId,
    uint CelestialSkyboxFileDataId);

/// <summary>One skybox model and its evaluated contribution to the current scene.</summary>
public readonly record struct WorldSkyboxLayer(
    uint FileDataId,
    int Flags,
    float Opacity);

public readonly record struct WorldSkyLighting(
    Vector3 TopColor,
    Vector3 MiddleColor,
    Vector3 Band1Color,
    Vector3 Band2Color,
    Vector3 SmogColor,
    Vector3 FogColor,
    bool HasColorData,
    IReadOnlyList<WorldSkyboxLayer> Skyboxes,
    bool HighlightSky)
{
    public Vector3 SunColor { get; init; }
    public Vector3 CloudSunColor { get; init; }
    public Vector3 CloudEmissiveColor { get; init; }
    public Vector3 CloudLayer1AmbientColor { get; init; }
    public Vector3 CloudLayer2AmbientColor { get; init; }
    public bool HasSunCloudData { get; init; }

    public float ShadowOpacity { get; init; }
    public float FogEnd { get; init; }
    public float FogScaler { get; init; }
    public float CloudDensity { get; init; }
    public float FogDensity { get; init; }
    public float FogHeight { get; init; }
    public float FogHeightScaler { get; init; }
    public float FogHeightDensity { get; init; }
    public float FogZScalar { get; init; }
    public float MainFogStartDistance { get; init; }
    public float MainFogEndDistance { get; init; }
    public float SunFogAngle { get; init; }
    public Vector3 EndFogColor { get; init; }
    public float EndFogColorDistance { get; init; }
    public float FogStartOffset { get; init; }
    public Vector3 SunFogColor { get; init; }
    public float SunFogStrength { get; init; }
    public Vector3 FogHeightColor { get; init; }
    public Vector3 EndFogHeightColor { get; init; }
    public Vector3 GroundAmbientColor { get; init; }
    public Vector3 HorizonAmbientColor { get; init; }
    public Vector4 FogHeightCoefficients { get; init; }
    public Vector4 MainFogCoefficients { get; init; }
    public Vector4 HeightDensityFogCoefficients { get; init; }
    public long ColorGradingFileDataId { get; init; }
    public long DarkerColorGradingFileDataId { get; init; }
    public bool HasFogData { get; init; }

    public bool HasSkyboxes => Skyboxes is { Count: > 0 };

    public bool OverrideColorsWithFog
    {
        get
        {
            if (Skyboxes == null)
                return false;

            foreach (var skybox in Skyboxes)
            {
                if (skybox.Opacity > 0f && (skybox.Flags & 0x4) != 0)
                    return true;
            }

            return false;
        }
    }

    public static WorldSkyLighting None { get; } = new(
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero,
        false,
        Array.Empty<WorldSkyboxLayer>(),
        false);
}

/// <summary>One evaluated, renderer-independent world-lighting sample.</summary>
public readonly record struct WorldLightingSample(
    long LightParamId,
    int Time,
    Vector3 LightDirection,
    Vector3 AmbientColor,
    Vector3 DirectColor,
    Vector3 OceanCloseColor,
    Vector3 OceanFarColor,
    Vector3 RiverCloseColor,
    Vector3 RiverFarColor,
    float WaterShallowAlpha,
    float WaterDeepAlpha,
    float OceanShallowAlpha,
    float OceanDeepAlpha,
    bool HasLiquidColorData,
    bool HasLiquidAlphaData,
    WorldSkyLighting Sky,
    IReadOnlyList<WorldLightingContribution>? ActiveLightContributions = null);

/// <summary>
/// Immutable client-lighting catalogue. Evaluation follows WoW's two-stage
/// selection: a map/global default, polygonal ZoneLight volumes, then radial
/// local Light volumes. Each selected LightParam is interpolated over the
/// circular 0..2880 game-day timeline before spatial blending.
/// </summary>
public sealed class WorldLightingCatalog
{
    public const int GameDayLength = 2880;
    public const float ZoneTransitionDistance = 50f;
    public static Vector3 DefaultNoonSpecularColor { get; } = new(1f, 0.969f, 0.871f);

    // Wisp receives a time-resolved specular color from its caller. LightData
    // supplies our sun tint; the inverse night-glow ramp keeps the noon fallback
    // and sparse/bright sun bands from illuminating WMO highlights at night.
    public static Vector3 ResolveWmoSpecularColor(WorldSkyLighting sky, long worldTime)
    {
        var sun = sky.HasSunCloudData ? sky.SunColor : DefaultNoonSpecularColor;
        return Vector3.Clamp(sun, Vector3.Zero, Vector3.One) *
            (1f - CalculateWmoSidnPulse(worldTime));
    }

    /// <summary>Night glow for WMO SIDN materials on the 0..2880 world clock.</summary>
    public static float CalculateWmoSidnPulse(long worldTime)
    {
        var time = worldTime % GameDayLength;
        if (time < 0)
            time += GameDayLength;

        var hours = time / 120f;
        if (hours < 6f)
            return 1f;
        if (hours < 7f)
            return 7f - hours;
        if (hours < 20.5f)
            return 0f;
        if (hours < 21.5f)
            return hours - 20.5f;
        return 1f;
    }

    private readonly WorldLightDefinition[] _lights;
    private readonly ZoneLightDefinition[] _zoneLights;
    private readonly IReadOnlyDictionary<int, WorldLightDefinition> _lightsById;
    private readonly IReadOnlyDictionary<int, WorldLightingData[]> _dataByParamId;
    private readonly IReadOnlyDictionary<int, WorldLightParams> _paramsById;
    private readonly IReadOnlyDictionary<int, WorldSkyboxDefinition> _skyboxesById;

    public WorldLightingCatalog(
        IEnumerable<WorldLightDefinition> lights,
        IEnumerable<ZoneLightDefinition> zoneLights,
        IEnumerable<WorldLightingData> timedData,
        IReadOnlyDictionary<int, WorldLightParams> lightParams,
        IReadOnlyDictionary<int, WorldSkyboxDefinition>? skyboxes = null)
    {
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(zoneLights);
        ArgumentNullException.ThrowIfNull(timedData);
        ArgumentNullException.ThrowIfNull(lightParams);

        _lights = lights.ToArray();
        _zoneLights = zoneLights.ToArray();
        _lightsById = _lights.ToDictionary(static light => light.Id);
        _dataByParamId = timedData
            .GroupBy(static row => checked((int)row.LightParamId))
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static row => row.Time).ToArray());
        _paramsById = lightParams;
        _skyboxesById = skyboxes ?? new Dictionary<int, WorldSkyboxDefinition>();
    }

    public int LightCount => _lights.Length;
    public int ZoneLightCount => _zoneLights.Length;
    public int TimedDataCount => _dataByParamId.Values.Sum(static rows => rows.Length);

    public WorldLightingSample? Evaluate(int mapId, Vector3 worldPosition, int time)
    {
        var normalizedTime = NormalizeTime(time);
        WorldLightingSample? result = null;
        var activeLights = new List<WorldLightingContribution>();

        var defaultLight = _lights
            .Where(light => light.IsDefault && (light.MapId == mapId || light.MapId == 0))
            .OrderByDescending(light => light.MapId == mapId)
            .ThenByDescending(static light => light.Id)
            .FirstOrDefault();
        if (defaultLight != null &&
            TryEvaluateParam(defaultLight.ExteriorLightParamId, normalizedTime, out var defaultSample))
        {
            result = defaultSample;
            activeLights.Add(new WorldLightingContribution(
                WorldLightingSourceKind.Global,
                defaultLight.Id,
                checked((int)defaultSample.LightParamId),
                1f));
        }

        foreach (var zone in _zoneLights
                     .Where(zone => zone.MapId == mapId)
                     .Select(zone => (Zone: zone, Alpha: CalculateZoneBlend(zone, worldPosition)))
                     .Where(static candidate => candidate.Alpha > 0f)
                     .OrderBy(static candidate => candidate.Zone.Priority)
                     .ThenByDescending(static candidate => candidate.Zone.LightId))
        {
            if (!_lightsById.TryGetValue(zone.Zone.LightId, out var light) ||
                !TryEvaluateParam(light.ExteriorLightParamId, normalizedTime, out var sample))
            {
                continue;
            }

            if (result.HasValue)
            {
                result = Blend(result.Value, sample, zone.Alpha);
                BlendActiveLights(
                    activeLights,
                    new WorldLightingContribution(
                        WorldLightingSourceKind.Zone,
                        light.Id,
                        checked((int)sample.LightParamId),
                        zone.Alpha,
                        zone.Zone.Id,
                        zone.Zone.Name));
            }
            else
            {
                result = sample;
                activeLights.Add(new WorldLightingContribution(
                    WorldLightingSourceKind.Zone,
                    light.Id,
                    checked((int)sample.LightParamId),
                    1f,
                    zone.Zone.Id,
                    zone.Zone.Name));
            }
        }

        foreach (var local in _lights
                     .Where(light => light.MapId == mapId && !light.IsDefault)
                     .Select(light => (Light: light, Alpha: CalculateRadialBlend(light, worldPosition)))
                     .Where(static candidate => candidate.Alpha > 0f)
                     .OrderByDescending(static candidate => candidate.Alpha)
                     .ThenByDescending(static candidate => candidate.Light.Id))
        {
            if (!TryEvaluateParam(local.Light.ExteriorLightParamId, normalizedTime, out var sample))
                continue;

            if (result.HasValue)
            {
                result = Blend(result.Value, sample, local.Alpha);
                BlendActiveLights(
                    activeLights,
                    new WorldLightingContribution(
                        WorldLightingSourceKind.Local,
                        local.Light.Id,
                        checked((int)sample.LightParamId),
                        local.Alpha));
            }
            else
            {
                result = sample;
                activeLights.Add(new WorldLightingContribution(
                    WorldLightingSourceKind.Local,
                    local.Light.Id,
                    checked((int)sample.LightParamId),
                    1f));
            }
        }

        return result is { } evaluated
            ? evaluated with { ActiveLightContributions = NormalizeActiveLights(activeLights) }
            : null;
    }

    public static int NormalizeTime(int time)
    {
        var normalized = time % GameDayLength;
        return normalized < 0 ? normalized + GameDayLength : normalized;
    }

    /// <summary>Maps local wall-clock time onto WoW's 0..2880 day.</summary>
    public static int FromLocalTime(TimeSpan timeOfDay) => NormalizeTime(
        (timeOfDay.Hours * 120) +
        (timeOfDay.Minutes * 2) +
        (timeOfDay.Seconds / 30));

    /// <summary>
    /// Reproduces the directional-light polar curve used by the reference
    /// renderer. The reference vector follows the light ray; the DX11 shaders
    /// use a vector toward the light, so all three components are inverted.
    /// </summary>
    public static Vector3 CalculateLightDirection(int time)
    {
        var dayProgress = NormalizeTime(time) / (float)GameDayLength;
        ReadOnlySpan<Vector2> phiTable =
        [
            new(0f, 2.2165682f),
            new(0.25f, 1.9198622f),
            new(0.5f, 2.2165682f),
            new(0.75f, 1.9198622f)
        ];
        const float theta = 3.9269907f;
        var phi = InterpolateCircularTable(phiTable, dayProgress);
        var sinPhi = MathF.Sin(phi);
        var clientDirection = new Vector3(
            sinPhi * MathF.Cos(theta),
            sinPhi * MathF.Sin(theta),
            MathF.Cos(phi));
        return Vector3.Normalize(new Vector3(
            -clientDirection.X,
            -clientDirection.Y,
            -clientDirection.Z));
    }

    private bool TryEvaluateParam(int lightParamId, int time, out WorldLightingSample sample)
    {
        if (lightParamId <= 0 ||
            !_dataByParamId.TryGetValue(lightParamId, out var rows) ||
            rows.Length == 0)
        {
            sample = default;
            return false;
        }

        var nextIndex = Array.FindIndex(rows, row => row.Time > time);
        if (nextIndex < 0)
            nextIndex = 0;
        var previousIndex = nextIndex == 0 ? rows.Length - 1 : nextIndex - 1;
        var previous = rows[previousIndex];
        var next = rows[nextIndex];

        var previousTime = checked((int)previous.Time);
        var nextTime = checked((int)next.Time);
        var adjustedTime = time;
        if (nextIndex == 0)
            nextTime += GameDayLength;
        if (adjustedTime < previousTime)
            adjustedTime += GameDayLength;
        var duration = nextTime - previousTime;
        var alpha = duration <= 0
            ? 0f
            : Math.Clamp((adjustedTime - previousTime) / (float)duration, 0f, 1f);

        _paramsById.TryGetValue(lightParamId, out var parameters);
        _skyboxesById.TryGetValue(parameters.LightSkyboxId, out var skybox);
        var previousSample = ToSample(previous, parameters, skybox, time);
        var nextSample = ToSample(next, parameters, skybox, time);
        sample = Blend(previousSample, nextSample, alpha) with
        {
            LightParamId = lightParamId,
            Time = time,
            LightDirection = CalculateLightDirection(time)
        };
        return true;
    }

    private static WorldLightingSample ToSample(
        WorldLightingData data,
        WorldLightParams parameters,
        WorldSkyboxDefinition? skybox,
        int time)
    {
        // Some current client profiles keep the named legacy liquid columns
        // but encode all four as zero. Zero is an absent override, not a black
        // water palette. Resolve the renderer's client-material defaults here
        // so the renderer snapshot and the editor controls see identical,
        // usable colors.
        var oceanClose = data.HasLiquidColorData
            ? data.OceanCloseColor
            : WorldLiquidColorDefaults.OceanClose;
        var oceanFar = data.HasLiquidColorData
            ? data.OceanFarColor
            : WorldLiquidColorDefaults.OceanFar;
        var riverClose = data.HasLiquidColorData
            ? data.RiverCloseColor
            : WorldLiquidColorDefaults.RiverClose;
        var riverFar = data.HasLiquidColorData
            ? data.RiverFarColor
            : WorldLiquidColorDefaults.RiverFar;

        return new WorldLightingSample(
            data.LightParamId,
            time,
            CalculateLightDirection(time),
            data.AmbientColor,
            data.DirectColor,
            oceanClose,
            oceanFar,
            riverClose,
            riverFar,
            parameters.HasLiquidAlphaData ? parameters.WaterShallowAlpha : 1f,
            parameters.HasLiquidAlphaData ? parameters.WaterDeepAlpha : 1f,
            parameters.HasLiquidAlphaData ? parameters.OceanShallowAlpha : 1f,
            parameters.HasLiquidAlphaData ? parameters.OceanDeepAlpha : 1f,
            true,
            parameters.HasLiquidAlphaData,
            new WorldSkyLighting(
                data.SkyTopColor,
                data.SkyMiddleColor,
                data.SkyBand1Color,
                data.SkyBand2Color,
                data.SkySmogColor,
                data.SkyFogColor,
                data.HasSkyColorData,
                skybox is { SkyboxFileDataId: > 0 }
                    ? [new WorldSkyboxLayer(skybox.SkyboxFileDataId, skybox.Flags, 1f)]
                    : Array.Empty<WorldSkyboxLayer>(),
                parameters.HighlightSky)
            {
                SunColor = data.SunColor,
                CloudSunColor = data.CloudSunColor,
                CloudEmissiveColor = data.CloudEmissiveColor,
                CloudLayer1AmbientColor = data.CloudLayer1AmbientColor,
                CloudLayer2AmbientColor = data.CloudLayer2AmbientColor,
                HasSunCloudData = data.HasSunCloudData,
                ShadowOpacity = data.ShadowOpacity,
                FogEnd = data.FogEnd,
                FogScaler = data.FogScaler,
                CloudDensity = data.CloudDensity,
                FogDensity = data.FogDensity,
                FogHeight = data.FogHeight,
                FogHeightScaler = data.FogHeightScaler,
                FogHeightDensity = data.FogHeightDensity,
                FogZScalar = data.FogZScalar,
                MainFogStartDistance = data.MainFogStartDistance,
                MainFogEndDistance = data.MainFogEndDistance,
                SunFogAngle = data.SunFogAngle,
                EndFogColor = data.EndFogColor,
                EndFogColorDistance = data.EndFogColorDistance,
                FogStartOffset = data.FogStartOffset,
                SunFogColor = data.SunFogColor,
                SunFogStrength = data.SunFogStrength,
                FogHeightColor = data.FogHeightColor,
                EndFogHeightColor = data.EndFogHeightColor,
                GroundAmbientColor = data.GroundAmbientColor,
                HorizonAmbientColor = data.HorizonAmbientColor,
                FogHeightCoefficients = data.FogHeightCoefficients,
                MainFogCoefficients = data.MainFogCoefficients,
                HeightDensityFogCoefficients = data.HeightDensityFogCoefficients,
                ColorGradingFileDataId = data.ColorGradingFileDataId,
                DarkerColorGradingFileDataId = data.DarkerColorGradingFileDataId,
                HasFogData = data.HasFogData
            });
    }

    private static WorldLightingSample Blend(
        WorldLightingSample current,
        WorldLightingSample incoming,
        float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        return new WorldLightingSample(
            alpha >= 0.5f ? incoming.LightParamId : current.LightParamId,
            incoming.Time,
            Vector3.Normalize(Vector3.Lerp(current.LightDirection, incoming.LightDirection, alpha)),
            Vector3.Lerp(current.AmbientColor, incoming.AmbientColor, alpha),
            Vector3.Lerp(current.DirectColor, incoming.DirectColor, alpha),
            Vector3.Lerp(current.OceanCloseColor, incoming.OceanCloseColor, alpha),
            Vector3.Lerp(current.OceanFarColor, incoming.OceanFarColor, alpha),
            Vector3.Lerp(current.RiverCloseColor, incoming.RiverCloseColor, alpha),
            Vector3.Lerp(current.RiverFarColor, incoming.RiverFarColor, alpha),
            Lerp(current.WaterShallowAlpha, incoming.WaterShallowAlpha, alpha),
            Lerp(current.WaterDeepAlpha, incoming.WaterDeepAlpha, alpha),
            Lerp(current.OceanShallowAlpha, incoming.OceanShallowAlpha, alpha),
            Lerp(current.OceanDeepAlpha, incoming.OceanDeepAlpha, alpha),
            current.HasLiquidColorData || incoming.HasLiquidColorData,
            current.HasLiquidAlphaData || incoming.HasLiquidAlphaData,
            BlendSky(current.Sky, incoming.Sky, alpha));
    }

    private static void BlendActiveLights(
        List<WorldLightingContribution> activeLights,
        WorldLightingContribution incoming)
    {
        var alpha = Math.Clamp(incoming.Weight, 0f, 1f);
        for (var index = 0; index < activeLights.Count; index++)
        {
            var existing = activeLights[index];
            activeLights[index] = existing with { Weight = existing.Weight * (1f - alpha) };
        }

        activeLights.Add(incoming with { Weight = alpha });
    }

    private static IReadOnlyList<WorldLightingContribution> NormalizeActiveLights(
        List<WorldLightingContribution> activeLights)
    {
        var grouped = activeLights
            .Where(static light => light.Weight > 0.0001f)
            .GroupBy(static light => (
                light.Kind,
                light.LightId,
                light.LightParamId,
                light.ZoneLightId,
                light.ZoneName))
            .Select(static group =>
            {
                var first = group.First();
                return first with { Weight = group.Sum(static light => light.Weight) };
            })
            .OrderByDescending(static light => light.Weight)
            .ThenBy(static light => light.Kind)
            .ThenBy(static light => light.LightId)
            .ToArray();

        var total = grouped.Sum(static light => light.Weight);
        if (total <= 0.0001f || MathF.Abs(total - 1f) <= 0.0001f)
            return grouped;

        return grouped
            .Select(light => light with { Weight = light.Weight / total })
            .ToArray();
    }

    private static WorldSkyLighting BlendSky(
        WorldSkyLighting current,
        WorldSkyLighting incoming,
        float alpha)
    {
        return new WorldSkyLighting(
            Vector3.Lerp(current.TopColor, incoming.TopColor, alpha),
            Vector3.Lerp(current.MiddleColor, incoming.MiddleColor, alpha),
            Vector3.Lerp(current.Band1Color, incoming.Band1Color, alpha),
            Vector3.Lerp(current.Band2Color, incoming.Band2Color, alpha),
            Vector3.Lerp(current.SmogColor, incoming.SmogColor, alpha),
            Vector3.Lerp(current.FogColor, incoming.FogColor, alpha),
            current.HasColorData || incoming.HasColorData,
            BlendSkyboxes(current.Skyboxes, incoming.Skyboxes, alpha),
            alpha >= 0.5f ? incoming.HighlightSky : current.HighlightSky)
        {
            SunColor = Vector3.Lerp(current.SunColor, incoming.SunColor, alpha),
            CloudSunColor = Vector3.Lerp(current.CloudSunColor, incoming.CloudSunColor, alpha),
            CloudEmissiveColor = Vector3.Lerp(current.CloudEmissiveColor, incoming.CloudEmissiveColor, alpha),
            CloudLayer1AmbientColor = Vector3.Lerp(
                current.CloudLayer1AmbientColor,
                incoming.CloudLayer1AmbientColor,
                alpha),
            CloudLayer2AmbientColor = Vector3.Lerp(
                current.CloudLayer2AmbientColor,
                incoming.CloudLayer2AmbientColor,
                alpha),
            HasSunCloudData = current.HasSunCloudData || incoming.HasSunCloudData,
            ShadowOpacity = Lerp(current.ShadowOpacity, incoming.ShadowOpacity, alpha),
            FogEnd = Lerp(current.FogEnd, incoming.FogEnd, alpha),
            FogScaler = Lerp(current.FogScaler, incoming.FogScaler, alpha),
            CloudDensity = Lerp(current.CloudDensity, incoming.CloudDensity, alpha),
            FogDensity = Lerp(current.FogDensity, incoming.FogDensity, alpha),
            FogHeight = Lerp(current.FogHeight, incoming.FogHeight, alpha),
            FogHeightScaler = Lerp(current.FogHeightScaler, incoming.FogHeightScaler, alpha),
            FogHeightDensity = Lerp(current.FogHeightDensity, incoming.FogHeightDensity, alpha),
            FogZScalar = Lerp(current.FogZScalar, incoming.FogZScalar, alpha),
            MainFogStartDistance = Lerp(current.MainFogStartDistance, incoming.MainFogStartDistance, alpha),
            MainFogEndDistance = Lerp(current.MainFogEndDistance, incoming.MainFogEndDistance, alpha),
            SunFogAngle = Lerp(current.SunFogAngle, incoming.SunFogAngle, alpha),
            EndFogColor = Vector3.Lerp(current.EndFogColor, incoming.EndFogColor, alpha),
            EndFogColorDistance = Lerp(current.EndFogColorDistance, incoming.EndFogColorDistance, alpha),
            FogStartOffset = Lerp(current.FogStartOffset, incoming.FogStartOffset, alpha),
            SunFogColor = Vector3.Lerp(current.SunFogColor, incoming.SunFogColor, alpha),
            SunFogStrength = Lerp(current.SunFogStrength, incoming.SunFogStrength, alpha),
            FogHeightColor = Vector3.Lerp(current.FogHeightColor, incoming.FogHeightColor, alpha),
            EndFogHeightColor = Vector3.Lerp(current.EndFogHeightColor, incoming.EndFogHeightColor, alpha),
            GroundAmbientColor = Vector3.Lerp(current.GroundAmbientColor, incoming.GroundAmbientColor, alpha),
            HorizonAmbientColor = Vector3.Lerp(current.HorizonAmbientColor, incoming.HorizonAmbientColor, alpha),
            FogHeightCoefficients = Vector4.Lerp(
                current.FogHeightCoefficients,
                incoming.FogHeightCoefficients,
                alpha),
            MainFogCoefficients = Vector4.Lerp(
                current.MainFogCoefficients,
                incoming.MainFogCoefficients,
                alpha),
            HeightDensityFogCoefficients = Vector4.Lerp(
                current.HeightDensityFogCoefficients,
                incoming.HeightDensityFogCoefficients,
                alpha),
            ColorGradingFileDataId = alpha >= 0.5f
                ? incoming.ColorGradingFileDataId
                : current.ColorGradingFileDataId,
            DarkerColorGradingFileDataId = alpha >= 0.5f
                ? incoming.DarkerColorGradingFileDataId
                : current.DarkerColorGradingFileDataId,
            HasFogData = current.HasFogData || incoming.HasFogData
        };
    }

    private static IReadOnlyList<WorldSkyboxLayer> BlendSkyboxes(
        IReadOnlyList<WorldSkyboxLayer>? current,
        IReadOnlyList<WorldSkyboxLayer>? incoming,
        float alpha)
    {
        var currentScale = 1f - alpha;
        var currentCount = current?.Count ?? 0;
        var incomingCount = incoming?.Count ?? 0;
        if (currentCount == 0 && incomingCount == 0)
            return Array.Empty<WorldSkyboxLayer>();

        var result = new List<WorldSkyboxLayer>(currentCount + incomingCount);
        AddScaledSkyboxes(result, current, currentScale);
        AddScaledSkyboxes(result, incoming, alpha);
        return result.Count == 0 ? Array.Empty<WorldSkyboxLayer>() : result.ToArray();
    }

    private static void AddScaledSkyboxes(
        List<WorldSkyboxLayer> result,
        IReadOnlyList<WorldSkyboxLayer>? source,
        float scale)
    {
        if (source == null || scale <= 0f)
            return;

        foreach (var layer in source)
        {
            if (layer.FileDataId == 0)
                continue;

            var opacity = Math.Clamp(layer.Opacity * scale, 0f, 1f);
            if (opacity <= 0.0001f)
                continue;

            var existingIndex = -1;
            for (var index = 0; index < result.Count; index++)
            {
                if (result[index].FileDataId == layer.FileDataId)
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                result.Add(layer with { Opacity = opacity });
                continue;
            }

            var existing = result[existingIndex];
            result[existingIndex] = existing with
            {
                Flags = existing.Flags | layer.Flags,
                Opacity = Math.Clamp(existing.Opacity + opacity, 0f, 1f)
            };
        }
    }

    private static float CalculateRadialBlend(
        WorldLightDefinition light,
        Vector3 worldPosition)
    {
        var distance = Vector3.Distance(worldPosition, light.WorldPosition);
        if (distance <= light.FalloffStart)
            return 1f;
        if (distance >= light.FalloffEnd || light.FalloffEnd <= light.FalloffStart)
            return 0f;
        return 1f - ((distance - light.FalloffStart) /
            (light.FalloffEnd - light.FalloffStart));
    }

    private static float CalculateZoneBlend(
        ZoneLightDefinition zone,
        Vector3 worldPosition)
    {
        if (zone.Points.Count < 3)
            return 0f;

        var point = new Vector2(worldPosition.X, worldPosition.Y);
        var inside = IsPointInsidePolygon(point, zone.Points);
        var horizontalDistance = DistanceToPolygon(point, zone.Points);
        if (inside)
            horizontalDistance = -horizontalDistance;

        float verticalDistance;
        if (worldPosition.Z >= zone.MinimumZ && worldPosition.Z <= zone.MaximumZ)
        {
            verticalDistance = -MathF.Min(
                worldPosition.Z - zone.MinimumZ,
                zone.MaximumZ - worldPosition.Z);
        }
        else
        {
            verticalDistance = worldPosition.Z < zone.MinimumZ
                ? zone.MinimumZ - worldPosition.Z
                : worldPosition.Z - zone.MaximumZ;
        }

        var signedDistance = MathF.Max(horizontalDistance, verticalDistance);
        return Math.Clamp(
            (ZoneTransitionDistance - signedDistance) /
            (ZoneTransitionDistance * 2f),
            0f,
            1f);
    }

    private static bool IsPointInsidePolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        var inside = false;
        for (var current = 0; current < polygon.Count; current++)
        {
            var previous = current == 0 ? polygon.Count - 1 : current - 1;
            var a = polygon[current];
            var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < ((b.X - a.X) * (point.Y - a.Y) /
                    ((b.Y - a.Y) + float.Epsilon)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static float DistanceToPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        var minimum = float.MaxValue;
        for (var current = 0; current < polygon.Count; current++)
        {
            var next = (current + 1) % polygon.Count;
            minimum = MathF.Min(minimum, DistanceToSegment(point, polygon[current], polygon[next]));
        }

        return minimum;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon)
            return Vector2.Distance(point, start);
        var amount = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, start + (segment * amount));
    }

    private static float InterpolateCircularTable(ReadOnlySpan<Vector2> table, float time)
    {
        var next = 0;
        while (next < table.Length && time > table[next].X)
            next++;
        if (next == table.Length)
            next = 0;
        var previous = next == 0 ? table.Length - 1 : next - 1;
        var startTime = table[previous].X;
        var endTime = table[next].X;
        if (next == 0)
            endTime += 1f;
        if (time < startTime)
            time += 1f;
        var alpha = (time - startTime) / (endTime - startTime);
        return Lerp(table[previous].Y, table[next].Y, alpha);
    }

    private static float Lerp(float start, float end, float amount) =>
        start + ((end - start) * amount);
}
