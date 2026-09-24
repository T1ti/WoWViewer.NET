using System.Globalization;
using System.Numerics;
using DBCD;
using DBCD.Providers;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Providers;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

/// <summary>
/// Loads the complete world-lighting catalogue through DBCD and accesses every
/// field by its WoWDBDefs column name. No field offsets or positional managed
/// layouts are used by this path.
/// </summary>
public static class WorldLightingCatalogLoader
{
    public static WorldLightingCatalog Load(
        Fs.FileSystem fileSystem,
        string buildName)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildName);

        var database = new DBCD.DBCD(
            new WowlibDBCProvider(fileSystem),
            new GithubDBDProvider(useCache: true));

        var legacyBands = LegacyLightBandLoader.UsesLegacyBands(buildName);
        var lights = ReadLights(database.Load("Light", buildName), legacyBands);
        var timedData = legacyBands
            ? LegacyLightBandLoader.Load(
                database.Load("LightIntBand", buildName),
                database.Load("LightFloatBand", buildName),
                lights.SelectMany(static light => light.LightParamIds))
            : ReadLightData(database.Load("LightData", buildName));
        var parameters = ReadLightParams(database.Load("LightParams", buildName));
        var skyboxes = ReadLightSkyboxes(database, fileSystem, buildName);
        var zones = ReadZoneLights(database, buildName);

        foreach (var paramId in lights.SelectMany(static light => light.LightParamIds)
                     .Where(static id => id > 0).Distinct())
        {
            if (!parameters.ContainsKey(paramId))
                Console.Error.WriteLine($"LightParams entry ID={paramId} referenced by Light is missing.");
        }
        foreach (var skyboxId in parameters.Values.Select(static value => value.LightSkyboxId)
                     .Where(static id => id > 0).Distinct())
        {
            if (!skyboxes.ContainsKey(skyboxId))
                Console.Error.WriteLine($"LightSkybox entry ID={skyboxId} referenced by LightParams is missing.");
            else if (skyboxes[skyboxId].SkyboxFileDataId == 0)
                Console.Error.WriteLine($"LightSkybox entry ID={skyboxId} has no readable model asset.");
        }
        var lightIds = lights.Select(static light => light.Id).ToHashSet();
        foreach (var zone in zones)
        {
            if (!lightIds.Contains(zone.LightId))
                Console.Error.WriteLine(
                    $"Light entry ID={zone.LightId} referenced by ZoneLight ID={zone.Id} is missing.");
            if (zone.Points.Count < 3)
                Console.Error.WriteLine(
                    $"ZoneLight ID={zone.Id} has fewer than three ZoneLightPoint entries.");
        }

        var catalog = new WorldLightingCatalog(
            lights, zones, timedData, parameters, skyboxes);
        Console.WriteLine(
            $"Loaded dynamic world lighting by column name: {catalog.LightCount} Light rows, " +
            $"{catalog.ZoneLightCount} ZoneLight volumes, {catalog.TimedDataCount} " +
            $"{(legacyBands ? "LightIntBand/LightFloatBand" : "LightData")} keys.");
        return catalog;
    }

    private static IReadOnlyList<WorldLightDefinition> ReadLights(IDBCDStorage storage, bool legacyBands)
    {
        var columns = new NamedColumns(storage, "Light");
        columns.Require("ContinentID", "GameCoords", "GameFalloffStart", "GameFalloffEnd", "LightParamsID");
        return storage.Values.Select(row => new WorldLightDefinition(
            columns.ReadId(row),
            columns.ReadInt(row, "ContinentID"),
            legacyBands
                ? LegacyLightPosition(columns.ReadVector3(row, "GameCoords"))
                : columns.ReadVector3(row, "GameCoords"),
            columns.ReadFloat(row, "GameFalloffStart") / (legacyBands ? 36f : 1f),
            columns.ReadFloat(row, "GameFalloffEnd") / (legacyBands ? 36f : 1f),
            columns.ReadIntArray(row, "LightParamsID"))).ToArray();
    }

    internal static Vector3 LegacyLightPosition(Vector3 dbcPosition)
    {
        if (dbcPosition == Vector3.Zero)
            return Vector3.Zero;
        // Pre-LightData Light.dbc stores inches from the map's northwest
        // corner in X/Z/Y order. Terrain and the camera use center-origin XYZ.
        const float worldOrigin = 17066.666f;
        return new Vector3(
            worldOrigin - dbcPosition.Z / 36f,
            worldOrigin - dbcPosition.X / 36f,
            dbcPosition.Y / 36f);
    }

    private static IReadOnlyList<WorldLightingData> ReadLightData(IDBCDStorage storage)
    {
        var columns = new NamedColumns(storage, "LightData");
        columns.Require(
            "LightParamID", "Time", "DirectColor", "AmbientColor",
            "SkyTopColor", "SkyMiddleColor", "SkyBand1Color", "SkyBand2Color",
            "SkySmogColor", "SkyFogColor",
            "OceanCloseColor", "OceanFarColor", "RiverCloseColor", "RiverFarColor");

        var rowIndex = 0;
        return storage.Values.Select(row =>
        {
            var numeric = new Dictionary<string, double>(StringComparer.Ordinal);
            columns.AddNumeric(row, numeric, "light_param_id", "LightParamID");
            columns.AddNumeric(row, numeric, "time", "Time");
            foreach (var (key, column) in LightDataNumericColumns)
                columns.AddNumeric(row, numeric, key, column);
            foreach (var (key, column) in LightDataArrayColumns)
                columns.AddNumericArray(row, numeric, key, column, 4);
            return new WorldLightingData(
                rowIndex++,
                columns.ReadId(row),
                checked((long)numeric["light_param_id"]),
                checked((long)numeric["time"]),
                numeric,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }).ToArray();
    }

    private static readonly (string Key, string Column)[] LightDataNumericColumns =
    [
        ("direct_color", "DirectColor"),
        ("ambient_color", "AmbientColor"),
        ("sky_top_color", "SkyTopColor"),
        ("sky_middle_color", "SkyMiddleColor"),
        ("sky_band_1_color", "SkyBand1Color"),
        ("sky_band_2_color", "SkyBand2Color"),
        ("sky_smog_color", "SkySmogColor"),
        ("sky_fog_color", "SkyFogColor"),
        ("sun_color", "SunColor"),
        ("cloud_sun_color", "CloudSunColor"),
        ("cloud_emissive_color", "CloudEmissiveColor"),
        ("cloud_layer_1_ambient_color", "CloudLayer1AmbientColor"),
        ("cloud_layer_2_ambient_color", "CloudLayer2AmbientColor"),
        ("ocean_close_color", "OceanCloseColor"),
        ("ocean_far_color", "OceanFarColor"),
        ("river_close_color", "RiverCloseColor"),
        ("river_far_color", "RiverFarColor"),
        ("shadow_opacity", "ShadowOpacity"),
        ("fog_end", "FogEnd"),
        ("fog_scaler", "FogScaler"),
        ("cloud_density", "CloudDensity"),
        ("fog_density", "FogDensity"),
        ("color_grading_file_data_id", "ColorGradingFileDataID"),
        ("end_fog_color", "EndFogColor"),
        ("end_fog_color_distance", "EndFogColorDistance"),
        ("fog_height", "FogHeight"),
        ("fog_height_color", "FogHeightColor"),
        ("fog_height_density", "FogHeightDensity"),
        ("fog_height_scaler", "FogHeightScaler"),
        ("ground_ambient_color", "GroundAmbientColor"),
        ("horizon_ambient_color", "HorizonAmbientColor"),
        ("sun_fog_angle", "SunFogAngle"),
        ("sun_fog_color", "SunFogColor"),
        ("sun_fog_strength", "SunFogStrength"),
        ("fog_z_scalar", "FogZScalar"),
        ("darker_color_grading_file_data_id", "DarkerColorGradingFileDataID"),
        ("main_fog_start_dist", "MainFogStartDist"),
        ("main_fog_end_dist", "MainFogEndDist"),
        ("fog_start_offset", "FogStartOffset"),
        ("end_fog_height_color", "EndFogHeightColor")
    ];

    private static readonly (string Key, string Column)[] LightDataArrayColumns =
    [
        ("fog_height_coefficients", "FogHeightCoefficients"),
        ("main_fog_coefficients", "MainFogCoefficients"),
        ("height_density_fog_coeff", "HeightDensityFogCoeff")
    ];

    private static IReadOnlyDictionary<int, WorldLightParams> ReadLightParams(IDBCDStorage storage)
    {
        var columns = new NamedColumns(storage, "LightParams");
        columns.Require(
            "WaterShallowAlpha", "WaterDeepAlpha", "OceanShallowAlpha", "OceanDeepAlpha",
            "LightSkyboxID", "HighlightSky");
        return storage.Values.ToDictionary(
            columns.ReadId,
            row => new WorldLightParams(
                Math.Clamp(columns.ReadFloat(row, "WaterShallowAlpha"), 0f, 1f),
                Math.Clamp(columns.ReadFloat(row, "WaterDeepAlpha"), 0f, 1f),
                Math.Clamp(columns.ReadFloat(row, "OceanShallowAlpha"), 0f, 1f),
                Math.Clamp(columns.ReadFloat(row, "OceanDeepAlpha"), 0f, 1f),
                true,
                columns.ReadInt(row, "LightSkyboxID"),
                columns.ReadInt(row, "HighlightSky") != 0));
    }

    private static IReadOnlyDictionary<int, WorldSkyboxDefinition> ReadLightSkyboxes(
        DBCD.DBCD database,
        Fs.FileSystem fileSystem,
        string buildName)
    {
        try
        {
            var storage = database.Load("LightSkybox", buildName);
            var columns = new NamedColumns(storage, "LightSkybox");
            if (LegacyLightBandLoader.UsesLegacyBands(buildName))
            {
                columns.Require("Name");
                return storage.Values.ToDictionary(
                    columns.ReadId,
                    row => new WorldSkyboxDefinition(
                        columns.ReadId(row),
                        columns.ReadString(row, "Name"),
                        columns.TryReadInt(row, "Flags", 0),
                        ResolveLegacySkybox(fileSystem, columns.ReadString(row, "Name")),
                        0));
            }

            columns.Require("SkyboxFileDataID");
            return storage.Values.ToDictionary(
                columns.ReadId,
                row => new WorldSkyboxDefinition(
                    columns.ReadId(row),
                    columns.TryReadString(row, "Name", string.Empty),
                    columns.TryReadInt(row, "Flags", 0),
                    checked((uint)columns.ReadLong(row, "SkyboxFileDataID")),
                    checked((uint)columns.TryReadLong(row, "CelestialSkyboxFileDataID", 0))));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"LightSkybox model overrides are unavailable for build {buildName}: {exception.Message}");
            return new Dictionary<int, WorldSkyboxDefinition>();
        }
    }

    private static uint ResolveLegacySkybox(Fs.FileSystem fileSystem, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;
        var path = name.Trim().Replace('\\', '/');
        var id = WowlibFileSystem.ResolveAssetId(fileSystem, path);
        if (id == 0 && path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
            id = WowlibFileSystem.ResolveAssetId(fileSystem, Path.ChangeExtension(path, ".m2"));
        if (id == 0)
            Console.Error.WriteLine($"LightSkybox model '{name}' is missing from the client files.");
        return id;
    }

    private static IReadOnlyList<ZoneLightDefinition> ReadZoneLights(
        DBCD.DBCD database,
        string buildName)
    {
        try
        {
            var zoneStorage = database.Load("ZoneLight", buildName);
            var pointStorage = database.Load("ZoneLightPoint", buildName);
            var zones = new NamedColumns(zoneStorage, "ZoneLight");
            var points = new NamedColumns(pointStorage, "ZoneLightPoint");
            zones.Require("Name", "MapID", "LightID");
            points.Require("ZoneLightID", "Pos", "PointOrder");

            var pointsByZone = pointStorage.Values
                .Select(row => new
                {
                    ZoneId = points.ReadInt(row, "ZoneLightID"),
                    Order = points.ReadInt(row, "PointOrder"),
                    Position = points.ReadVector2(row, "Pos")
                })
                .GroupBy(static point => point.ZoneId)
                .ToDictionary(
                    static group => group.Key,
                    static group => (IReadOnlyList<Vector2>)group
                        .OrderByDescending(static point => point.Order)
                        .Select(static point => point.Position)
                        .ToArray());

            return zoneStorage.Values.Select(row =>
            {
                var id = zones.ReadId(row);
                pointsByZone.TryGetValue(id, out var polygon);
                return new ZoneLightDefinition(
                    id,
                    zones.ReadString(row, "Name"),
                    zones.ReadInt(row, "MapID"),
                    zones.ReadInt(row, "LightID"),
                    zones.TryReadInt(row, "TransitionType", 0),
                    zones.TryReadFloat(row, "Zmin", float.MinValue),
                    zones.TryReadFloat(row, "Zmax", float.MaxValue),
                    polygon ?? []);
            }).ToArray();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"ZoneLight local volumes are unavailable for build {buildName}: {exception.Message}");
            return [];
        }
    }

    private sealed class NamedColumns
    {
        private readonly string _tableName;
        private readonly Dictionary<string, string> _actualNames;

        public NamedColumns(IDBCDStorage storage, string tableName)
        {
            _tableName = tableName;
            _actualNames = storage.AvailableColumns.ToDictionary(
                static name => name,
                static name => name,
                StringComparer.OrdinalIgnoreCase);
        }

        public void Require(params string[] names)
        {
            var missing = names.Where(name => !_actualNames.ContainsKey(name)).ToArray();
            if (missing.Length == 0)
                return;
            throw new InvalidDataException(
                $"{_tableName} is missing required named columns [{string.Join(", ", missing)}]. " +
                $"Available columns: [{string.Join(", ", _actualNames.Values)}].");
        }

        public int ReadId(DBCDRow row) => _actualNames.ContainsKey("ID")
            ? ReadInt(row, "ID")
            : row.ID;

        public int ReadInt(DBCDRow row, string name) =>
            Convert.ToInt32(Read(row, name), CultureInfo.InvariantCulture);

        public long ReadLong(DBCDRow row, string name) =>
            Convert.ToInt64(Read(row, name), CultureInfo.InvariantCulture);

        public float ReadFloat(DBCDRow row, string name) =>
            Convert.ToSingle(Read(row, name), CultureInfo.InvariantCulture);

        public string ReadString(DBCDRow row, string name) =>
            Convert.ToString(Read(row, name), CultureInfo.InvariantCulture) ?? string.Empty;

        public int TryReadInt(DBCDRow row, string name, int fallback) =>
            _actualNames.ContainsKey(name) ? ReadInt(row, name) : fallback;

        public long TryReadLong(DBCDRow row, string name, long fallback) =>
            _actualNames.ContainsKey(name) ? ReadLong(row, name) : fallback;

        public string TryReadString(DBCDRow row, string name, string fallback) =>
            _actualNames.ContainsKey(name) ? ReadString(row, name) : fallback;

        public float TryReadFloat(DBCDRow row, string name, float fallback) =>
            _actualNames.ContainsKey(name) ? ReadFloat(row, name) : fallback;

        public void AddNumeric(
            DBCDRow row,
            IDictionary<string, double> destination,
            string key,
            string column)
        {
            if (_actualNames.ContainsKey(column))
                destination[key] = Convert.ToDouble(Read(row, column), CultureInfo.InvariantCulture);
        }

        public void AddNumericArray(
            DBCDRow row,
            IDictionary<string, double> destination,
            string key,
            string column,
            int count)
        {
            if (!_actualNames.ContainsKey(column))
                return;

            var values = ReadArray(row, column);
            for (var index = 0; index < Math.Min(count, values.Length); index++)
            {
                destination[$"{key}_{index}"] = Convert.ToDouble(
                    values.GetValue(index),
                    CultureInfo.InvariantCulture);
            }
        }

        public Vector2 ReadVector2(DBCDRow row, string name)
        {
            var values = ReadArray(row, name);
            if (values.Length < 2)
                throw InvalidArray(name, 2, values.Length);
            return new Vector2(ToFloat(values.GetValue(0)), ToFloat(values.GetValue(1)));
        }

        public Vector3 ReadVector3(DBCDRow row, string name)
        {
            var values = ReadArray(row, name);
            if (values.Length < 3)
                throw InvalidArray(name, 3, values.Length);
            return new Vector3(
                ToFloat(values.GetValue(0)),
                ToFloat(values.GetValue(1)),
                ToFloat(values.GetValue(2)));
        }

        public IReadOnlyList<int> ReadIntArray(DBCDRow row, string name) =>
            ReadArray(row, name)
                .Cast<object?>()
                .Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture))
                .ToArray();

        private object Read(DBCDRow row, string name)
        {
            if (!_actualNames.TryGetValue(name, out var actualName))
                throw new InvalidDataException($"{_tableName} has no named column '{name}'.");
            return row[actualName];
        }

        private Array ReadArray(DBCDRow row, string name) => Read(row, name) as Array
            ?? throw new InvalidDataException($"{_tableName}.{name} is not an array column.");

        private InvalidDataException InvalidArray(string name, int expected, int actual) => new(
            $"{_tableName}.{name} contains {actual} values; at least {expected} are required.");

        private static float ToFloat(object? value) =>
            Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }
}
