using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Controls;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

public sealed partial class EditorSettingsSmokeTests
{
    [TestMethod]
    public void TexturePaletteNames_HideTilesetPathAndExtensionButPreserveStoredSuffix()
    {
        Assert.AreEqual(
            "elwynngrass01_s",
            TexturePaletteNaming.FromPath(@"tileset\elwynn\elwynngrass01_s.blp"));
        Assert.AreEqual(
            "rock_detail",
            TexturePaletteNaming.FromPath("tileset/outland/rock_detail.blp"));
        Assert.AreEqual("FDID 123", TexturePaletteNaming.FromPath("FDID 123"));
    }

    [TestMethod]
    public async Task TexturePreviewCommand_UsesExactSelectedTextureIdentity()
    {
        var previews = new RecordingTexturePreviewService();
        var editor = new TextureEditingViewModel(previewService: previews);
        var texture = new TexturePaletteItemViewModel(
            "fdid:187127",
            "ElwynnGrassBase_s",
            FileDataId: 187127);

        editor.AddOrSelectTexture(texture);
        await texture.PreviewCommand.ExecuteAsync(null);

        Assert.AreEqual((187127u, "ElwynnGrassBase_s"), previews.LastRequest);
    }

    [TestMethod]
    public void TextureBrowserSelectionAndPickerUseTheActiveTextureWorkflow()
    {
        var chosen = new TexturePaletteItemViewModel("fdid:42", "chosen", FileDataId: 42);
        var editor = new TextureEditingViewModel(thumbnailService: new NullTextureThumbnailService());

        editor.ShowTextureBrowserCommand.Execute(null);
        editor.IsPickerModeActive = true;
        Assert.IsNotNull(editor.Browser);
        editor.Browser.SelectedTexture = chosen;

        Assert.AreSame(chosen, editor.SelectedTexture);
        Assert.IsTrue(editor.IsBrowserVisible);
        Assert.IsTrue(editor.IsBrowserExpanded);
        Assert.IsTrue(editor.IsPickerModeActive);
        editor.ToggleBrowserExpandedCommand.Execute(null);
        Assert.IsFalse(editor.IsBrowserExpanded);
        Assert.AreEqual(44d, editor.BrowserHeight);
        editor.ToggleBrowserExpandedCommand.Execute(null);
        Assert.IsTrue(editor.IsBrowserExpanded);
        Assert.IsTrue(editor.BrowserHeight >= 260d);
        editor.SelectTerrainTexture(new TerrainChunkTextureLayer(0, 43));
        Assert.IsFalse(editor.IsPickerModeActive);
        Assert.IsTrue(TextureBrowserViewModel.IsBrowsableTerrainTexture("tileset/elwynn/grass.blp"));
        Assert.IsFalse(TextureBrowserViewModel.IsBrowsableTerrainTexture("tileset/elwynn/grass_s.blp"));
        Assert.IsTrue(TextureBrowserViewModel.IsBrowsableTerrainTexture(
            "tileset/elwynn/grass_s.blp",
            includeSpecular: true));
        Assert.IsFalse(TextureBrowserViewModel.IsBrowsableTerrainTexture(
            "tileset/elwynn/grass_h.blp",
            includeSpecular: true));
        Assert.IsFalse(TextureBrowserViewModel.IsBrowsableTerrainTexture("world/model/skin.blp"));
    }

    [TestMethod]
    public async Task TextureBrowser_DisposalCancelsCatalogAndDiscardsLateResult()
    {
        var catalog = new DeferredClientFileCatalogService();
        var browser = new TextureBrowserViewModel(new NullTextureThumbnailService(), catalog);
        var activation = browser.ActivateAsync();

        browser.Dispose();

        Assert.IsTrue(catalog.Token.IsCancellationRequested);
        catalog.Completion.SetResult(
        [
            new ClientFileCatalogEntry("tileset/elwynn/grass.blp", 42)
        ]);
        await activation.ConfigureAwait(false);

        Assert.AreEqual(0, browser.Results.Count);
        Assert.ThrowsException<ObjectDisposedException>(browser.Activate);
    }

    [TestMethod]
    public void TexturePreviewChannelModes_PreserveColorOrExposeAlphaAtFullOpacity()
    {
        byte[] colorPixels = [10, 20, 30, 40, 50, 60, 70, 80];
        TerrainTextureImageLoader.ApplyChannelMode(colorPixels, TexturePreviewChannelMode.Color);
        CollectionAssert.AreEqual(
            new byte[] { 10, 20, 30, 255, 50, 60, 70, 255 },
            colorPixels);

        byte[] alphaPixels = [10, 20, 30, 40, 50, 60, 70, 80];
        TerrainTextureImageLoader.ApplyChannelMode(alphaPixels, TexturePreviewChannelMode.Alpha);
        CollectionAssert.AreEqual(
            new byte[] { 40, 40, 40, 255, 80, 80, 80, 255 },
            alphaPixels);
    }

    [TestMethod]
    public void TextureBrowserFolderTree_PreservesHierarchyAndFullPaths()
    {
        var root = TextureBrowserViewModel.BuildFolderTree([
            new TextureBrowserCatalogEntry(10, @"tileset\elwynn\grass.blp"),
            new TextureBrowserCatalogEntry(11, "tileset/elwynn/road/stone.blp"),
            new TextureBrowserCatalogEntry(12, "tileset/outland/rock.blp")
        ]);

        Assert.AreEqual("tileset", root.FullPath);
        CollectionAssert.AreEqual(
            new[] { "elwynn", "outland" },
            root.Children.Select(folder => folder.Name).ToArray());
        var elwynn = root.Children[0];
        Assert.AreSame(root, elwynn.Parent);
        Assert.AreEqual("tileset/elwynn", elwynn.FullPath);
        Assert.AreEqual("tileset/elwynn/grass.blp", elwynn.Textures.Single().Path);
        var road = elwynn.Children.Single();
        Assert.AreSame(elwynn, road.Parent);
        Assert.AreEqual("tileset/elwynn/road", road.FullPath);

        var item = new TexturePaletteItemViewModel(
            "fdid:10",
            "grass",
            FileDataId: 10,
            FullPath: "tileset/elwynn/grass.blp");
        StringAssert.Contains(item.Tooltip, "tileset/elwynn/grass.blp");
    }

    [TestMethod]
    public void TextureBrowserVariantBadges_OnlyDescribeExistingBaseTextureVariants()
    {
        IReadOnlySet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tileset/elwynn/grass.blp",
            "tileset/elwynn/grass_s.blp",
            "tileset/elwynn/grass_h.blp",
            "tileset/elwynn/road.blp"
        };

        var grass = TextureBrowserViewModel.GetVariantAvailability(
            @"tileset\elwynn\grass.blp",
            files);
        Assert.IsTrue(grass.HasSpecular);
        Assert.IsTrue(grass.HasHeight);

        Assert.AreEqual(
            default,
            TextureBrowserViewModel.GetVariantAvailability("tileset/elwynn/grass_s.blp", files));
        Assert.AreEqual(
            default,
            TextureBrowserViewModel.GetVariantAvailability("tileset/elwynn/grass_h.blp", files));

        var road = TextureBrowserViewModel.GetVariantAvailability("tileset/elwynn/road.blp", files);
        Assert.IsFalse(road.HasSpecular);
        Assert.IsFalse(road.HasHeight);
    }

    [TestMethod]
    public void TextureBrowserFolderFavorite_ReplacesFavoritesWithDirectTexturesOnly()
    {
        using var editor = new TextureEditingViewModel(
            thumbnailService: new NullTextureThumbnailService());
        var root = TextureBrowserViewModel.BuildFolderTree([
            new TextureBrowserCatalogEntry(10, "tileset/elwynn/grass.blp"),
            new TextureBrowserCatalogEntry(11, "tileset/elwynn/road/stone.blp")
        ]);
        var elwynn = root.Children.Single();
        editor.Favorites.Add(new TexturePaletteItemViewModel("old", "old", FileDataId: 99));

        editor.Browser!.SetFolderAsFavoritesCommand.Execute(elwynn);

        Assert.AreEqual(1, editor.Favorites.Count);
        Assert.AreEqual(10u, editor.Favorites.Single().FileDataId);
        Assert.IsFalse(editor.Favorites.Any(texture => texture.FileDataId == 11));
    }

    [TestMethod]
    public void TextureDragGesture_RequiresThePlatformMovementThreshold()
    {
        var origin = new global::Avalonia.Point(20, 30);
        var threshold = new global::Avalonia.Size(4, 6);

        Assert.IsFalse(TextureDragGesture.HasExceededThreshold(
            origin,
            new global::Avalonia.Point(23.9, 35.9),
            threshold));
        Assert.IsTrue(TextureDragGesture.HasExceededThreshold(
            origin,
            new global::Avalonia.Point(24, 30),
            threshold));
        Assert.IsTrue(TextureDragGesture.HasExceededThreshold(
            origin,
            new global::Avalonia.Point(20, 24),
            threshold));
    }

    private static ADTVertex TerrainVertex(float x, float y, float z) => new()
    {
        Position = new Vector3(x, y, z),
        Normal = Vector3.UnitZ
    };

}
