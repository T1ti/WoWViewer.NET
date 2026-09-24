using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class WorldLightingSmokeTests
{
    [TestMethod]
    public void LegacyLightBandsUseBuildBoundaryAndIndependentKeyTimes()
    {
        Assert.IsTrue(LegacyLightBandLoader.UsesLegacyBands("3.3.5.12340"));
        Assert.IsTrue(LegacyLightBandLoader.UsesLegacyBands("4.3.4.15595"));
        Assert.IsFalse(LegacyLightBandLoader.UsesLegacyBands("5.0.1.15596"));
        Assert.AreEqual(Vector3.Zero, WorldLightingCatalogLoader.LegacyLightPosition(Vector3.Zero));
        var localPosition = WorldLightingCatalogLoader.LegacyLightPosition(
            new Vector3(612096, 3600, 998400));
        Assert.AreEqual(17066.666f - 998400f / 36f, localPosition.X, 0.001f);
        Assert.AreEqual(17066.666f - 612096f / 36f, localPosition.Y, 0.001f);
        Assert.AreEqual(100f, localPosition.Z, 0.001f);

        const int paramId = 2;
        var intRows = Enumerable.Range(19, 18)
            .Select(id => new LegacyLightBandLoader.BandRow(id, 1, [0], [0x00112233]))
            .ToArray();
        intRows[0] = new(19, 2, [0, 1440], [0x00ff0000, 0x000000ff]);
        intRows[1] = new(20, 2, [720, 2160], [0x0000ff00, 0x000000ff]);
        intRows[2] = new(21, 1, [0], [unchecked((int)0xff112233)]);
        intRows[14] = new(33, 1, [0], [0x00123456]);
        intRows[17] = new(36, 1, [0], [0x00abcdef]);

        var floatRows = Enumerable.Range(7, 6)
            .Select(id => new LegacyLightBandLoader.BandRow(id, 1, [0], [0.5]))
            .ToArray();
        floatRows[0] = new(7, 2, [0, 1440], [100, 300]);
        floatRows[3] = new(10, 1, [0], [0.75]);

        var data = LegacyLightBandLoader.Build(intRows, floatRows, [paramId]);
        Assert.AreEqual(4, data.Count);
        var noon = data.Single(row => row.Time == 1440);
        var morning = data.Single(row => row.Time == 720);
        var midnight = data.Single(row => row.Time == 0);

        Assert.AreEqual(paramId, noon.LightParamId);
        Assert.AreEqual(0x000000ffu, noon.DirectColorPacked);
        Assert.AreEqual(0x00800080u, morning.DirectColorPacked);
        Assert.AreEqual(0x00008080u, midnight.AmbientColorPacked);
        Assert.AreEqual(new Vector3(0x11 / 255f, 0x22 / 255f, 0x33 / 255f), noon.SkyTopColor);
        Assert.AreEqual(0x00123456u, noon.OceanCloseColorPacked);
        Assert.AreEqual(0x00abcdefu, noon.RiverFarColorPacked);
        Assert.AreEqual(200f, morning.FogEnd, 0.001f);
        Assert.AreEqual(0.75f, noon.CloudDensity, 0.001f);
        Assert.IsTrue(noon.HasSkyColorData);
        Assert.IsTrue(noon.HasLiquidColorData);
    }

    [TestMethod]
    public void LegacyLightBandRejectsInvalidEntryCount()
    {
        var bad = new LegacyLightBandLoader.BandRow(1, 17, new int[16], new double[16]);
        Assert.ThrowsException<InvalidDataException>(() =>
            LegacyLightBandLoader.Build([bad], [], [1]));
    }

    [TestMethod]
    public void LightDataSnapshotPreservesValuesAndUnpacksWowColors()
    {
        var snapshot = new WorldLightingData(
            rowIndex: 7,
            id: 123,
            lightParamId: 12,
            time: 1440,
            numericValues: new Dictionary<string, double>
            {
                ["direct_color"] = unchecked((int)0xff336699),
                ["ambient_color"] = unchecked((int)0xff112233),
                ["ocean_close_color"] = unchecked((int)0xff102030),
                ["ocean_far_color"] = unchecked((int)0xff405060),
                ["river_close_color"] = unchecked((int)0xff708090),
                ["river_far_color"] = unchecked((int)0xffa0b0c0),
                ["water_shallow_alpha"] = 0.25,
                ["water_deep_alpha"] = 0.75,
                ["ocean_shallow_alpha"] = 0.4,
                ["ocean_deep_alpha"] = 0.9,
                ["fog_end"] = 512f
            },
            stringValues: new Dictionary<string, string>
            {
                ["debug_name"] = "temporary-profile"
            });

        Assert.AreEqual(12, snapshot.LightParamId);
        Assert.AreEqual(1440, snapshot.Time);
        Assert.AreEqual(0xff336699u, snapshot.DirectColorPacked);
        Assert.AreEqual(0xff112233u, snapshot.AmbientColorPacked);
        Assert.AreEqual(0x33 / 255f, snapshot.DirectColor.X, 0.0001f);
        Assert.AreEqual(0x66 / 255f, snapshot.DirectColor.Y, 0.0001f);
        Assert.AreEqual(0x99 / 255f, snapshot.DirectColor.Z, 0.0001f);
        Assert.AreEqual(0x10 / 255f, snapshot.OceanCloseColor.X, 0.0001f);
        Assert.AreEqual(0x50 / 255f, snapshot.OceanFarColor.Y, 0.0001f);
        Assert.AreEqual(0x90 / 255f, snapshot.RiverCloseColor.Z, 0.0001f);
        Assert.AreEqual(0xa0 / 255f, snapshot.RiverFarColor.X, 0.0001f);
        Assert.IsTrue(snapshot.HasLiquidColorData);
        Assert.IsTrue(snapshot.HasLiquidAlphaData);
        Assert.AreEqual(0.25f, snapshot.WaterShallowAlpha, 0.0001f);
        Assert.AreEqual(0.75f, snapshot.WaterDeepAlpha, 0.0001f);
        Assert.AreEqual(0.4f, snapshot.OceanShallowAlpha, 0.0001f);
        Assert.AreEqual(0.9f, snapshot.OceanDeepAlpha, 0.0001f);
        Assert.AreEqual(512d, snapshot.NumericValues["fog_end"], 0.0001d);
        Assert.AreEqual("temporary-profile", snapshot.StringValues["debug_name"]);
    }

    [TestMethod]
    public void LightDataSnapshotUsesExactWowlibColumnNames()
    {
        var snapshot = new WorldLightingData(
            rowIndex: 0,
            id: 1,
            lightParamId: 12,
            time: 1440,
            numericValues: new Dictionary<string, double>
            {
                ["direct_color"] = 0x00112233,
                ["ambient_color"] = 0x00445566
            },
            stringValues: new Dictionary<string, string>());

        Assert.IsTrue(snapshot.TryGetNumeric("direct_color", out _));
        Assert.IsFalse(snapshot.TryGetNumeric("DirectColor", out _));
        Assert.IsFalse(snapshot.HasLiquidColorData);
        Assert.AreEqual(0x11 / 255f, snapshot.DirectColor.X, 0.0001f);
        Assert.AreEqual(0x22 / 255f, snapshot.DirectColor.Y, 0.0001f);
        Assert.AreEqual(0x33 / 255f, snapshot.DirectColor.Z, 0.0001f);
    }

    [TestMethod]
    public void AllZeroLightDataLiquidPaletteKeepsMaterialColorFallback()
    {
        var snapshot = new WorldLightingData(
            rowIndex: 0,
            id: 1,
            lightParamId: 7245,
            time: 1440,
            numericValues: new Dictionary<string, double>
            {
                ["ocean_close_color"] = 0,
                ["ocean_far_color"] = 0,
                ["river_close_color"] = 0,
                ["river_far_color"] = 0
            },
            stringValues: new Dictionary<string, string>());

        Assert.IsFalse(snapshot.HasLiquidColorData);
        Assert.AreEqual(Vector3.Zero, snapshot.OceanCloseColor);
        Assert.AreEqual(Vector3.Zero, snapshot.RiverFarColor);
    }

    [TestMethod]
    public void LightingCatalogPublicationRetainsOneRenderThreadRefresh()
    {
        var publication = new WorldLightingCatalogPublication();
        var catalog = CreateCatalog([], [], []);

        Assert.IsNull(publication.Current);
        Assert.IsFalse(publication.ConsumeRefreshRequest());

        publication.Publish(catalog);

        Assert.AreSame(catalog, publication.Current);
        Assert.IsTrue(publication.ConsumeRefreshRequest());
        Assert.IsFalse(publication.ConsumeRefreshRequest());
    }

    [TestMethod]
    public void FirstNavigationIsRetainedUntilContentInitializationCompletes()
    {
        var publication = new WorldNavigationPublication();
        var first = new WorldNavigationTarget(0, 123, 31.5, 29.25, false);
        var latest = new WorldNavigationTarget(1, 456, 10, 11, true);

        publication.Publish(first);

        Assert.IsNull(publication.ConsumeWhenReady(isReady: false));

        publication.Publish(latest);
        Assert.AreSame(latest, publication.ConsumeWhenReady(isReady: true));
        Assert.IsNull(publication.ConsumeWhenReady(isReady: true));
    }

    [TestMethod]
    public void DefaultNavigationDoesNotReplaceAQueuedMapSelection()
    {
        var publication = new WorldNavigationPublication();
        var selected = new WorldNavigationTarget(1, 456, 10, 11, true);
        var defaultMap = new WorldNavigationTarget(
            0, 123, 35.5, 24.5, false, PreserveCameraPosition: true);

        publication.PublishIfEmpty(defaultMap);
        Assert.AreSame(defaultMap, publication.ConsumeWhenReady(isReady: true));
        Assert.AreEqual(0, defaultMap.MapId);
        Assert.IsTrue(defaultMap.PreserveCameraPosition);

        publication.Publish(selected);
        publication.PublishIfEmpty(defaultMap);
        Assert.AreSame(selected, publication.ConsumeWhenReady(isReady: true));
        Assert.IsFalse(selected.PreserveCameraPosition);
    }

    [TestMethod]
    public void CatalogResolvesAllZeroLiquidColumnsToTheEffectiveMaterialPalette()
    {
        var catalog = CreateCatalog(
            [new WorldLightDefinition(1, 42, Vector3.Zero, 0, 0, [7])],
            [],
            [CreateData(1, 7, 0, 0)]);

        var sample = catalog.Evaluate(42, Vector3.Zero, 0);

        Assert.IsTrue(sample.HasValue);
        Assert.IsTrue(sample.Value.HasLiquidColorData);
        Assert.AreEqual(WorldLiquidColorDefaults.OceanClose, sample.Value.OceanCloseColor);
        Assert.AreEqual(WorldLiquidColorDefaults.OceanFar, sample.Value.OceanFarColor);
        Assert.AreEqual(WorldLiquidColorDefaults.RiverClose, sample.Value.RiverCloseColor);
        Assert.AreEqual(WorldLiquidColorDefaults.RiverFar, sample.Value.RiverFarColor);
        Assert.AreNotEqual(Vector3.Zero, sample.Value.OceanCloseColor);
        Assert.AreNotEqual(Vector3.Zero, sample.Value.RiverFarColor);
    }

    [TestMethod]
    public void TemporaryProfileOceanColorRetainsItsBlueChannel()
    {
        // LightData profile 12 at time 1440 in Classic 1.60.1.69876.
        var oceanClose = WorldLightingData.UnpackRgb(0x00114B59);

        Assert.AreEqual(0x11 / 255f, oceanClose.X, 0.0001f);
        Assert.AreEqual(0x4B / 255f, oceanClose.Y, 0.0001f);
        Assert.AreEqual(0x59 / 255f, oceanClose.Z, 0.0001f);
        Assert.IsTrue(oceanClose.Z > oceanClose.X);
    }

    [TestMethod]
    public void DynamicTimeInterpolatesAcrossMidnightAndUpdatesLightDirection()
    {
        var catalog = CreateCatalog(
            [new WorldLightDefinition(1, 42, Vector3.Zero, 0, 0, [7])],
            [],
            [
                CreateData(1, 7, 240, Pack(255, 0, 0)),
                CreateData(2, 7, 2640, Pack(0, 0, 255))
            ]);

        var sample = catalog.Evaluate(42, new Vector3(100, 100, 10), 0);

        Assert.IsTrue(sample.HasValue);
        Assert.AreEqual(0.5f, sample.Value.AmbientColor.X, 0.0001f);
        Assert.AreEqual(0.5f, sample.Value.AmbientColor.Z, 0.0001f);
        Assert.AreEqual(1f, sample.Value.LightDirection.Length(), 0.0001f);
        Assert.IsTrue(sample.Value.LightDirection.Z > 0f);
        Assert.AreEqual(0, sample.Value.Time);
    }

    [TestMethod]
    public void NoonSunDirectionPointsTowardLightInRendererAxes()
    {
        var noon = WorldLightingCatalog.CalculateLightDirection(1440);

        Assert.AreEqual(0.5613f, noon.X, 0.01f);
        Assert.AreEqual(0.5613f, noon.Y, 0.01f);
        Assert.AreEqual(0.6082f, noon.Z, 0.01f);
        Assert.IsTrue(Vector3.Dot(noon, WorldLightingSettings.Defaults.LightDirection) > 0.99f);

        var catalog = new WorldLightingCatalog(
            [new WorldLightDefinition(1, 42, Vector3.Zero, 0, 0, [7])],
            [],
            [CreateData(1, 7, 1440, Pack(255, 255, 255))],
            new Dictionary<int, WorldLightParams> { [7] = new(1, 1, 1, 1, true) });
        var sample = catalog.Evaluate(42, Vector3.Zero, 1440);
        Assert.IsTrue(sample.HasValue);
        Assert.AreEqual(noon, sample.Value.LightDirection);
    }

    [TestMethod]
    public void RadialLocalLightUsesNamedFalloffDistances()
    {
        var catalog = CreateCatalog(
            [
                new WorldLightDefinition(1, 42, Vector3.Zero, 0, 0, [1]),
                new WorldLightDefinition(2, 42, new Vector3(100, 0, 0), 0, 100, [2])
            ],
            [],
            [
                CreateData(1, 1, 0, Pack(0, 0, 0)),
                CreateData(2, 2, 0, Pack(255, 255, 255))
            ]);

        var sample = catalog.Evaluate(42, new Vector3(50, 0, 0), 0);

        Assert.IsTrue(sample.HasValue);
        Assert.AreEqual(0.5f, sample.Value.AmbientColor.X, 0.0001f);
        Assert.AreEqual(0.5f, sample.Value.DirectColor.Y, 0.0001f);
        Assert.AreEqual(2, sample.Value.ActiveLightContributions!.Count);
        Assert.AreEqual(WorldLightingSourceKind.Global, sample.Value.ActiveLightContributions[0].Kind);
        Assert.AreEqual(1, sample.Value.ActiveLightContributions[0].LightId);
        Assert.AreEqual(0.5f, sample.Value.ActiveLightContributions[0].Weight, 0.0001f);
        Assert.AreEqual(WorldLightingSourceKind.Local, sample.Value.ActiveLightContributions[1].Kind);
        Assert.AreEqual(2, sample.Value.ActiveLightContributions[1].LightId);
        Assert.AreEqual(0.5f, sample.Value.ActiveLightContributions[1].Weight, 0.0001f);

        var nearLocalLight = catalog.Evaluate(42, new Vector3(80, 0, 0), 0);

        Assert.IsTrue(nearLocalLight.HasValue);
        Assert.AreEqual(0.8f, nearLocalLight.Value.ActiveLightContributions![0].Weight, 0.0001f);
        Assert.AreEqual(0.2f, nearLocalLight.Value.ActiveLightContributions[1].Weight, 0.0001f);
    }

    [TestMethod]
    public void RendererWorldPositionIsUsedDirectlyForLocalLightSelection()
    {
        var localLightPosition = new Vector3(-8833f, 628f, 99f);

        Assert.AreEqual(
            localLightPosition,
            WowViewerEngine.RendererToWorldLightingPosition(localLightPosition));
    }

    [TestMethod]
    public void RenderingDefaultsExposeTheSameEffectiveLiquidPaletteAsMaterials()
    {
        var normalized = WorldLightingSettings.Defaults.NormalizeForRendering();

        Assert.IsTrue(normalized.HasLiquidColorData);
        Assert.IsFalse(normalized.HasLiquidAlphaData);
        Assert.AreEqual(WorldLiquidColorDefaults.OceanClose, normalized.OceanCloseColor);
        Assert.AreEqual(WorldLiquidColorDefaults.RiverFar, normalized.RiverFarColor);
    }

    [TestMethod]
    public void ZoneLightBlendsPolygonAndVerticalBounds()
    {
        var catalog = CreateCatalog(
            [
                new WorldLightDefinition(1, 42, Vector3.Zero, 0, 0, [1]),
                new WorldLightDefinition(9, 999, new Vector3(1, 1, 1), 0, 0, [9])
            ],
            [new ZoneLightDefinition(
                3,
                "Interior",
                42,
                9,
                1,
                -100,
                100,
                [new(-100, -100), new(100, -100), new(100, 100), new(-100, 100)])],
            [
                CreateData(1, 1, 0, Pack(0, 0, 0)),
                CreateData(9, 9, 0, Pack(0, 255, 0))
            ]);

        var center = catalog.Evaluate(42, Vector3.Zero, 0);
        var boundary = catalog.Evaluate(42, new Vector3(100, 0, 0), 0);
        var outside = catalog.Evaluate(42, new Vector3(151, 0, 0), 0);

        Assert.AreEqual(1f, center!.Value.AmbientColor.Y, 0.0001f);
        Assert.AreEqual(0.5f, boundary!.Value.AmbientColor.Y, 0.0001f);
        Assert.AreEqual(0f, outside!.Value.AmbientColor.Y, 0.0001f);
    }

    [TestMethod]
    public void LocalClockMapsToWowDayUnits()
    {
        Assert.AreEqual(0, WorldLightingCatalog.FromLocalTime(TimeSpan.Zero));
        Assert.AreEqual(720, WorldLightingCatalog.FromLocalTime(TimeSpan.FromHours(6)));
        Assert.AreEqual(2879, WorldLightingCatalog.FromLocalTime(
            new TimeSpan(23, 59, 30)));
    }

    [TestMethod]
    public void SkyColorsInterpolateAndLightParamsSelectSkyboxModel()
    {
        var catalog = new WorldLightingCatalog(
            [new WorldLightDefinition(1, 42, Vector3.Zero, 0, 0, [7])],
            [],
            [
                CreateData(1, 7, 0, Pack(0, 0, 0)),
                CreateData(2, 7, 1440, Pack(100, 120, 140))
            ],
            new Dictionary<int, WorldLightParams>
            {
                [7] = new(1, 1, 1, 1, true, LightSkyboxId: 9, HighlightSky: true)
            },
            new Dictionary<int, WorldSkyboxDefinition>
            {
                [9] = new(9, "Test sky", 5, 123456, 654321)
            });

        var sample = catalog.Evaluate(42, Vector3.Zero, 720);

        Assert.IsTrue(sample.HasValue);
        Assert.IsTrue(sample.Value.Sky.HasColorData);
        Assert.AreEqual(50f / 255f, sample.Value.Sky.TopColor.X, 0.0001f);
        Assert.AreEqual(60f / 255f, sample.Value.Sky.Band1Color.Y, 0.0001f);
        Assert.AreEqual(70f / 255f, sample.Value.Sky.FogColor.Z, 0.0001f);
        Assert.AreEqual(1, sample.Value.Sky.Skyboxes.Count);
        Assert.AreEqual(123456u, sample.Value.Sky.Skyboxes[0].FileDataId);
        Assert.AreEqual(5, sample.Value.Sky.Skyboxes[0].Flags);
        Assert.AreEqual(1f, sample.Value.Sky.Skyboxes[0].Opacity, 0.0001f);
        Assert.IsTrue(sample.Value.Sky.OverrideColorsWithFog);
        Assert.IsTrue(sample.Value.Sky.HighlightSky);
    }

    [TestMethod]
    public void LocalLightFadesItsSkyboxOverrideAcrossTheFalloffVolume()
    {
        var catalog = new WorldLightingCatalog(
            [
                new WorldLightDefinition(1, 42, Vector3.Zero, 0, 0, [1]),
                new WorldLightDefinition(2, 42, new Vector3(100, 0, 0), 0, 100, [2])
            ],
            [],
            [
                CreateData(1, 1, 0, Pack(0, 0, 0)),
                CreateData(2, 2, 0, Pack(255, 255, 255))
            ],
            new Dictionary<int, WorldLightParams>
            {
                [1] = new(1, 1, 1, 1, true),
                [2] = new(1, 1, 1, 1, true, LightSkyboxId: 9)
            },
            new Dictionary<int, WorldSkyboxDefinition>
            {
                [9] = new(9, "Local sky", 0, 123456, 0)
            });

        var sample = catalog.Evaluate(42, new Vector3(50, 0, 0), 0);

        Assert.IsTrue(sample.HasValue);
        Assert.AreEqual(1, sample.Value.Sky.Skyboxes.Count);
        Assert.AreEqual(123456u, sample.Value.Sky.Skyboxes[0].FileDataId);
        Assert.AreEqual(0.5f, sample.Value.Sky.Skyboxes[0].Opacity, 0.0001f);
    }

    [TestMethod]
    public void DistinctSkyboxesCrossfadeWithInterpolatedSpatialWeights()
    {
        var catalog = new WorldLightingCatalog(
            [
                new WorldLightDefinition(1, 42, Vector3.Zero, 0, 0, [1]),
                new WorldLightDefinition(2, 42, new Vector3(100, 0, 0), 0, 100, [2])
            ],
            [],
            [
                CreateData(1, 1, 0, Pack(0, 0, 0)),
                CreateData(2, 2, 0, Pack(255, 255, 255))
            ],
            new Dictionary<int, WorldLightParams>
            {
                [1] = new(1, 1, 1, 1, true, LightSkyboxId: 8),
                [2] = new(1, 1, 1, 1, true, LightSkyboxId: 9)
            },
            new Dictionary<int, WorldSkyboxDefinition>
            {
                [8] = new(8, "Exterior sky", 0, 111111, 0),
                [9] = new(9, "Local sky", 0, 222222, 0)
            });

        var sample = catalog.Evaluate(42, new Vector3(75, 0, 0), 0);

        Assert.IsTrue(sample.HasValue);
        Assert.AreEqual(2, sample.Value.Sky.Skyboxes.Count);
        Assert.AreEqual(111111u, sample.Value.Sky.Skyboxes[0].FileDataId);
        Assert.AreEqual(0.25f, sample.Value.Sky.Skyboxes[0].Opacity, 0.0001f);
        Assert.AreEqual(222222u, sample.Value.Sky.Skyboxes[1].FileDataId);
        Assert.AreEqual(0.75f, sample.Value.Sky.Skyboxes[1].Opacity, 0.0001f);
    }

    private static WorldLightingCatalog CreateCatalog(
        IReadOnlyList<WorldLightDefinition> lights,
        IReadOnlyList<ZoneLightDefinition> zones,
        IReadOnlyList<WorldLightingData> data) => new(
        lights,
        zones,
        data,
        new Dictionary<int, WorldLightParams>
        {
            [1] = new(1, 1, 1, 1, true),
            [2] = new(1, 1, 1, 1, true),
            [7] = new(1, 1, 1, 1, true),
            [9] = new(1, 1, 1, 1, true)
        });

    private static WorldLightingData CreateData(
        int id,
        int lightParamId,
        int time,
        uint color) => new(
        id,
        id,
        lightParamId,
        time,
        new Dictionary<string, double>
        {
            ["direct_color"] = color,
            ["ambient_color"] = color,
            ["sky_top_color"] = color,
            ["sky_middle_color"] = color,
            ["sky_band_1_color"] = color,
            ["sky_band_2_color"] = color,
            ["sky_smog_color"] = color,
            ["sky_fog_color"] = color,
            ["ocean_close_color"] = color,
            ["ocean_far_color"] = color,
            ["river_close_color"] = color,
            ["river_far_color"] = color
        },
        new Dictionary<string, string>());

    private static uint Pack(byte red, byte green, byte blue) =>
        ((uint)red << 16) | ((uint)green << 8) | blue;
}
