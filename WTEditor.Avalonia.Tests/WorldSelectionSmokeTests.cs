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
    public void WdtGlobalWmoFlag_IndicatesMapWithoutTerrain()
    {
        Assert.IsTrue(new WorldMapWdtMetadata(1, 0).HasTerrain);
        Assert.IsFalse(new WorldMapWdtMetadata(1, 0x1).HasTerrain);
    }

    [TestMethod]
    public async Task MapFilters_CombineTextExpansionAndMapType()
    {
        var session = new EditorSession(new MemorySettingsStore());
        var viewport = new Editor3DViewModel(session);
        viewport.UpdateRendererStatus(new RendererStatus(RendererLifecycleState.Ready, "Ready"));

        var viewModel = new WorldSelectionViewModel(
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

    private sealed class MemorySettingsStore : IEditorSettingsStore
    {
        public EditorSettingsSnapshot Load() => new();

        public void Save(EditorSettingsSnapshot settings)
        {
        }
    }
}
