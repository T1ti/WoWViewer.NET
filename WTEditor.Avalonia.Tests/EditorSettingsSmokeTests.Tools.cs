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
    public void EditorModeDefinitions_KeepCapabilitiesWithModeMetadata()
    {
        Assert.AreEqual(EditorModeDefinitions.SelectionId, EditorModeDefinitions.Selection.Id);
        Assert.IsTrue(EditorModeDefinitions.Selection.Capabilities.HasFlag(EditorModeCapabilities.Selection));
        Assert.IsTrue(EditorModeDefinitions.Terrain.Capabilities.HasFlag(EditorModeCapabilities.TerrainEditing));
        Assert.IsTrue(EditorModeDefinitions.Texture.Capabilities.HasFlag(EditorModeCapabilities.TextureEditing));
        Assert.IsNotNull(EditorModeDefinitions.Selection.Icon);
        Assert.IsNotNull(EditorModeDefinitions.Terrain.Icon);
        Assert.IsNotNull(EditorModeDefinitions.Texture.Icon);
        Assert.AreNotSame(EditorModeDefinitions.Selection.Icon, EditorModeDefinitions.Terrain.Icon);
        Assert.AreEqual(10d, new TerrainEditingViewModel().Brush.Size);
    }

    [TestMethod]
    public void BrushSettings_AreSharedWhileToolSpecificSettingsStaySeparate()
    {
        var terrain = new TerrainEditingViewModel();
        var texture = new TextureEditingViewModel();

        CollectionAssert.AreEquivalent(
            new[] { BrushShape.Circle, BrushShape.Square },
            terrain.Brush.AvailableBrushes.Select(option => option.Shape).Distinct().ToArray());
        CollectionAssert.AreEqual(
            terrain.Brush.AvailableBrushes.Select(option => option.Id).ToArray(),
            texture.Brush.AvailableBrushes.Select(option => option.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "Paint", "Smooth", "Colour" },
            texture.SubModes.Select(mode => mode.DisplayName).ToArray());
        Assert.IsTrue(texture.SubModes.All(mode => mode.Icon != null));
        Assert.IsTrue(texture.SubModes.All(mode => mode.UsesFalloff));

        terrain.Speed = 100;
        texture.Opacity = 300;
        texture.Strength = -1;
        Assert.AreEqual(50d, terrain.Speed);
        Assert.AreEqual(255d, texture.Opacity);
        Assert.AreEqual(0d, texture.Strength);
        texture.SelectedSubMode = texture.SubModes.Single(mode => mode.DisplayName == "Smooth");
        Assert.IsFalse(texture.IsOpacityVisible);
        Assert.IsNotNull(TextureBrushModes.GetPreviewColor(TextureBrushMode.Smooth));

        var grass = new TexturePaletteItemViewModel("grass", "Grass", FileDataId: 123);
        texture.AddOrSelectTexture(grass);
        texture.AddOrSelectTexture(new TexturePaletteItemViewModel("grass", "Duplicate"));
        Assert.AreEqual(1, texture.UserTextures.Count);
        Assert.AreSame(grass, texture.SelectedTexture);
        texture.SelectedFavorite = null;
        Assert.AreSame(grass, texture.SelectedTexture);

        texture.ShowChunkTextures([
            new TerrainChunkTextureLayer(2, 300),
            new TerrainChunkTextureLayer(0, 100),
            new TerrainChunkTextureLayer(1, 200)
        ]);
        CollectionAssert.AreEqual(
            new uint?[] { 100, 200, 300 },
            texture.ChunkTextures.Select(item => item.FileDataId).ToArray());
        Assert.IsTrue(texture.IsChunkPickerOpen);
        var rock = texture.ChunkTextures[1];
        texture.AddFavoriteCommand.Execute(rock);
        texture.AddFavoriteCommand.Execute(rock);
        Assert.AreEqual(2, texture.Favorites.Count);
        texture.SelectTextureCommand.Execute(rock);
        Assert.AreSame(rock, texture.SelectedTexture);
        Assert.IsFalse(texture.IsChunkPickerOpen);
        texture.RemoveFavoriteCommand.Execute(rock);
        Assert.AreEqual(1, texture.Favorites.Count);
        texture.SelectTerrainTexture(new TerrainChunkTextureLayer(3, 400));
        Assert.AreEqual((uint)400, texture.SelectedTexture?.FileDataId);
        Assert.AreEqual(1, texture.Favorites.Count);
        texture.AddFavorites([
            new TerrainChunkTextureLayer(0, 123),
            new TerrainChunkTextureLayer(1, 400),
            new TerrainChunkTextureLayer(2, 500),
            new TerrainChunkTextureLayer(3, 500)
        ]);
        CollectionAssert.AreEqual(
            new uint?[] { 123, 400, 500 },
            texture.Favorites.Select(item => item.FileDataId).ToArray());
        var tileRequestRaised = false;
        texture.CurrentTerrainTileTexturesRequested += (_, _) => tileRequestRaised = true;
        texture.AddFromCurrentPositionTileCommand.Execute(null);
        Assert.IsTrue(tileRequestRaised);
        Assert.IsTrue(terrain.Brush.HasFalloff);
        Assert.AreEqual(0.35d, terrain.Brush.Falloff);

        terrain.Brush.SelectedBrush = terrain.Brush.AvailableBrushes.Single(
            brush => brush.Id == BuiltInBrushPreset.HardRound);
        Assert.IsFalse(terrain.Brush.HasFalloff);
        Assert.IsFalse(terrain.Brush.IsFalloffVisible);
    }

}
