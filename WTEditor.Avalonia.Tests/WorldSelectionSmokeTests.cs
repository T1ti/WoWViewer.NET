using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Models;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class WorldSelectionSmokeTests
{
    [TestMethod]
    public void FirstWorldNavigationIsRetainedWithoutAnAttachedRendererSubscriber()
    {
        var session = new EditorSession(new MemorySettingsStore());
        using var viewport = new Editor3DViewModel(session);
        var expected = new WorldNavigationRequest(
            42,
            123456,
            new WTEditor.Application.Geometry.TilePoint(12.5, 34.25),
            false);

        // This is the startup ordering that previously lost the first map:
        // selection publishes before Dx11View has an engine to receive it.
        viewport.RequestWorldNavigation(expected);

        Assert.AreSame(expected, viewport.CurrentWorldNavigation);

        WorldNavigationRequest? published = null;
        viewport.WorldNavigationRequested += (_, navigation) => published = navigation;
        viewport.RequestWorldNavigation(expected);
        Assert.AreSame(expected, published);

        session.Reload(session.Current);
        Assert.IsNull(viewport.CurrentWorldNavigation);
    }

    [TestMethod]
    public void WdtGlobalWmoFlag_IndicatesMapWithoutTerrain()
    {
        Assert.IsTrue(new WorldMapWdtMetadata(1, 0).HasTerrain);
        Assert.IsFalse(new WorldMapWdtMetadata(1, 0x1).HasTerrain);
    }

    [TestMethod]
    public void MapSettings_ProjectsMapDbFieldsAndWdtTileFiles()
    {
        var map = new WorldMapRecord(42, "Test map", "TestMap", 123, 4, 1)
        {
            Settings =
            [
                new WorldMapDbSetting("ID", "42", "Int32"),
                new WorldMapDbSetting("MapName_lang", "Test map", "String"),
                new WorldMapDbSetting("Flags", "[1, 2]", "UInt32[]")
            ]
        };
        var tile = new WorldMapWdtTileData(new WorldMapTile(3, 7), 1)
        {
            RootAdtFileDataId = 456,
            MinimapTextureFileDataId = 789
        };
        var entry = new WorldMapCatalogEntry(
            map,
            new WorldMapWdtMetadata(123, 0)
            {
                Version = 18,
                Settings = [new WorldMapWdtSetting("MVER.Version", "18")],
                Tiles = [tile],
                ActiveTiles = [tile.Position]
            });

        var viewModel = new MapSettingsViewModel();
        viewModel.SetMap(entry);

        Assert.IsTrue(viewModel.HasSelectedMap);
        CollectionAssert.AreEqual(
            new[] { "ID", "MapName_lang", "Flags" },
            viewModel.MapDbSettings.Select(setting => setting.Label).ToArray());
        Assert.AreEqual("MVER.Version", viewModel.WdtSettings[0].Label);
        Assert.AreEqual("(3, 7)", viewModel.WdtTiles[0].Position);
        StringAssert.Contains(viewModel.WdtTiles[0].FileDataIds, "Root=456");
        StringAssert.Contains(viewModel.WdtTiles[0].FileDataIds, "Minimap=789");

        viewModel.SetMap(null);
        Assert.IsTrue(viewModel.HasNoSelectedMap);
        Assert.AreEqual(0, viewModel.MapDbSettings.Count);
        Assert.AreEqual(0, viewModel.WdtTiles.Count);
    }

    [TestMethod]
    public async Task MapFilters_CombineTextExpansionAndMapType()
    {
        var session = new EditorSession(new MemorySettingsStore());
        using var viewport = new Editor3DViewModel(session);
        viewport.UpdateRendererStatus(new RendererStatus(RendererLifecycleState.Ready, "Ready"));

        using var viewModel = new WorldSelectionViewModel(
            new TestMapCatalogService(
            [
                CreateMap(4, "Temple of the Jade Serpent", expansionId: 4, instanceType: 1, hasTerrain: true),
                CreateMap(1, "Elwynn Forest", expansionId: 0, instanceType: 0, hasTerrain: true),
                CreateMap(3, "The Jade Forest", expansionId: 4, instanceType: 0, hasTerrain: true),
                CreateMap(2, "Ragefire Chasm", expansionId: 0, instanceType: 1, hasTerrain: false)
            ]),
            viewport);

        await viewModel.ActivateAsync();

        Assert.AreEqual(3, viewModel.FilteredMaps.Count);
        CollectionAssert.AreEqual(new[] { 1, 3, 4 }, viewModel.FilteredMaps.Select(map => map.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "All expansions", "Classic", "Mists of Pandaria" },
            viewModel.ExpansionFilters.Select(filter => filter.DisplayName).ToArray());

        viewModel.ShowMapsWithoutTerrain = true;
        CollectionAssert.AreEqual(new[] { 2 }, viewModel.FilteredMaps.Select(map => map.Id).ToArray());
        viewModel.ShowMapsWithoutTerrain = false;

        viewModel.SearchText = "jade";
        viewModel.SelectedExpansion = viewModel.ExpansionFilters.Single(filter => filter.Value == 4);
        viewModel.SelectedMapType = viewModel.MapTypeFilters.Single(filter => filter.Value == 1);

        Assert.AreEqual(1, viewModel.FilteredMaps.Count);
        Assert.AreEqual(4, viewModel.FilteredMaps[0].Id);
        Assert.AreEqual("Mists of Pandaria", viewModel.FilteredMaps[0].ExpansionName);
        Assert.AreEqual("Dungeon", viewModel.FilteredMaps[0].MapType);
    }

    [TestMethod]
    public async Task TerrainMetadataCache_UsesMapIdentityRatherThanOnlyMapCount()
    {
        var reads = new List<uint>();
        var cache = new MapTerrainMetadataCacheService(fileDataId =>
        {
            reads.Add(fileDataId);
            return new WorldMapWdtMetadata(fileDataId, 0);
        });
        WorldMapRecord[] original =
        [
            new(1, "One", "One", 101, 0, 0),
            new(2, "Two", "Two", 202, 0, 0)
        ];

        var first = await cache.CacheAllAsync("1.2.3.4", original).ConfigureAwait(false);
        var reordered = await cache.CacheAllAsync(
            "1.2.3.4",
            original.Reverse().ToArray()).ConfigureAwait(false);
        var changed = await cache.CacheAllAsync(
            "1.2.3.4",
            [original[0], original[1] with { WdtFileDataId = 303 }]).ConfigureAwait(false);

        Assert.AreSame(first, reordered);
        Assert.AreNotSame(first, changed);
        Assert.AreEqual(303u, changed[2].FileDataId);
        CollectionAssert.AreEqual(new uint[] { 101, 202, 101, 303 }, reads);
    }

    [TestMethod]
    public async Task DisposalCancelsCatalogLoadAndDiscardsLateResult()
    {
        var session = new EditorSession(new MemorySettingsStore());
        using var viewport = new Editor3DViewModel(session);
        viewport.UpdateRendererStatus(new RendererStatus(RendererLifecycleState.Ready, "Ready"));
        var catalog = new DeferredMapCatalogService();
        var viewModel = new WorldSelectionViewModel(catalog, viewport);

        var activation = viewModel.ActivateAsync();
        viewModel.Dispose();

        Assert.IsTrue(catalog.Token.IsCancellationRequested);
        catalog.Completion.SetResult([CreateMap(1, "Late map", 0, 0, hasTerrain: true)]);
        await activation.ConfigureAwait(false);

        Assert.AreEqual(0, viewModel.FilteredMaps.Count);
    }

    private static WorldMapCatalogEntry CreateMap(
        int id,
        string name,
        int expansionId,
        int instanceType,
        bool hasTerrain) =>
        new(
            new WorldMapRecord(id, name, name.Replace(" ", string.Empty), (uint)id, expansionId, instanceType),
            new WorldMapWdtMetadata((uint)id, hasTerrain ? 0 : 0x1u));

    private sealed class TestMapCatalogService(IReadOnlyList<WorldMapCatalogEntry> maps) : IMapCatalogService
    {
        public Task<IReadOnlyList<WorldMapCatalogEntry>> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(maps);
        }
    }

    private sealed class DeferredMapCatalogService : IMapCatalogService
    {
        public TaskCompletionSource<IReadOnlyList<WorldMapCatalogEntry>> Completion { get; } = new();
        public CancellationToken Token { get; private set; }

        public Task<IReadOnlyList<WorldMapCatalogEntry>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Completion.Task;
        }
    }

    private sealed class MemorySettingsStore : IEditorSettingsStore
    {
        public EditorSettingsSnapshot Load() => new();

        public void Save(EditorSettingsSnapshot settings)
        {
        }
    }
}
