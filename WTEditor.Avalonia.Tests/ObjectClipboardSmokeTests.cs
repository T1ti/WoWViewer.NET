using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class ObjectClipboardSmokeTests
{
    [TestMethod]
    public void BrowserChoiceReplacesCopiedWorldObjectsWithoutChangingWorldSelection()
    {
        var clipboard = new ObjectClipboardService();
        using var editor = new ObjectEditingViewModel(new EmptyCatalog(), clipboard);
        var first = Snapshot("Tree.m2");
        var second = Snapshot("House.wmo");

        editor.SetWorldSelection([first, second]);
        editor.CopyWorldSelection();

        Assert.AreEqual("2 objects", editor.SelectedWorldObjectsLabel);
        Assert.AreEqual("2 objects", editor.ClipboardLabel);
        Assert.AreEqual(2, clipboard.Entries.Count);
        Assert.AreSame(first, clipboard.Entries[0].WorldObject);

        editor.ChooseObjectCommand.Execute(new ObjectBrowserItemViewModel(
            "Tower.wmo", "world/buildings/Tower.wmo", 123u, "WMO"));

        Assert.AreEqual("Tower.wmo", editor.ClipboardLabel);
        Assert.AreEqual("2 objects", editor.SelectedWorldObjectsLabel);
        Assert.AreEqual("world/buildings/Tower.wmo", clipboard.Entries[0].AssetPath);
        Assert.AreEqual(123u, clipboard.Entries[0].FileDataId);

        editor.SetWorldSelection([]);
        editor.CopyWorldSelection();

        Assert.AreEqual("None", editor.SelectedWorldObjectsLabel);
        Assert.AreEqual("Tower.wmo", editor.ClipboardLabel);
    }

    [TestMethod]
    public void FolderSearchKeepsAncestorPathsAndMatchingFilesOnly()
    {
        var index = ObjectAssetIndex.Build(
        [
            new("world/trees/oak.m2", 1u),
            new("world/trees/house.wmo", 2u),
            new("world/rocks/stone.m2", 3u),
            new("world/trees/house_000.wmo", 4u),
            new("world/trees/leaf.blp", 5u)
        ]);
        var world = index.Root.Children.Single();
        var trees = world.Children.Single(folder => folder.Name == "trees");
        var rocks = world.Children.Single(folder => folder.Name == "rocks");

        var fileSearch = index.Filter("oak", true, true);
        Assert.IsTrue(fileSearch.Includes(world));
        Assert.IsTrue(fileSearch.Includes(trees));
        Assert.IsFalse(fileSearch.Includes(rocks));
        CollectionAssert.AreEqual(new[] { "oak.m2" },
            fileSearch.FilesIn(trees).Select(file => file.Name).ToArray());

        var folderSearch = index.Filter("world/trees", true, true);
        CollectionAssert.AreEquivalent(new[] { "oak.m2", "house.wmo" },
            folderSearch.FilesIn(trees).Select(file => file.Name).ToArray());
        Assert.IsFalse(folderSearch.Includes(rocks));
        Assert.AreEqual(2, trees.Files.Count);
    }

    [TestMethod]
    // Avalonia's test dispatcher is not pumped by this smoke suite.
    public void AllFoldersArePagedAndFavoritesCanBeAddedAndRemoved() =>
        Task.Run(VerifyPagingAndFavoritesAsync).GetAwaiter().GetResult();

    private static async Task VerifyPagingAndFavoritesAsync()
    {
        var entries = Enumerable.Range(0, 450)
            .Select(number => new ClientFileCatalogEntry($"world/models/model{number:D3}.m2", (uint)number))
            .ToArray();
        using var editor = new ObjectEditingViewModel(new FixedCatalog(ObjectAssetIndex.Build(entries)),
            new ObjectClipboardService());
        editor.Activate();
        await WaitUntilAsync(() => editor.RootFolders.Count > 0 && !editor.IsFiltering);

        editor.NavigateFolderCommand.Execute(editor.RootFolders[0].Children[0].Children[0]);
        Assert.AreEqual(200, editor.FolderEntries.Count);
        Assert.IsTrue(editor.HasMoreEntries);
        var first = (ObjectBrowserItemViewModel)editor.FolderEntries[0];
        editor.AddFavoriteCommand.Execute(first);
        Assert.AreEqual(1, editor.FavoriteResults.Count);
        editor.LoadMoreEntriesCommand.Execute(null);
        Assert.AreEqual(400, editor.FolderEntries.Count);
        editor.RemoveFavoriteCommand.Execute(first);
        Assert.AreEqual(0, editor.FavoriteResults.Count);

        editor.SearchText = "model449";
        editor.SelectedBrowserTabIndex = 1;
        editor.SelectedBrowserTabIndex = 0;
        Assert.AreEqual(400, editor.FolderEntries.Count,
            "Switching tabs should not bypass the one-second search debounce.");
        await WaitUntilAsync(() => editor.FolderEntries.Count == 1);
        Assert.AreEqual("model449.m2",
            ((ObjectBrowserItemViewModel)editor.FolderEntries[0]).Filename);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(20);
        Assert.IsTrue(condition(), "Timed out waiting for the object browser.");
    }

    private static EditorObjectSnapshot Snapshot(string name) =>
        new(EditorObjectId.New(), name, "Model", ObjectTransform.Identity);

    private sealed class EmptyCatalog : IObjectAssetCatalogService
    {
        public Task<ObjectAssetIndex> GetIndexAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ObjectAssetIndex.Build([]));
    }

    private sealed class FixedCatalog(ObjectAssetIndex index) : IObjectAssetCatalogService
    {
        public Task<ObjectAssetIndex> GetIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(index);
    }
}
