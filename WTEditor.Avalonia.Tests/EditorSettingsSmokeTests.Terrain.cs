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
    public void TerrainBatching_MergesContiguousChunksWithSharedBoundResources()
    {
        var common = CreateTerrainBatch(material: 10, scale: 1f);
        var batches = new[]
        {
            common,
            CreateTerrainBatch(material: 10, scale: 1f),
            CreateTerrainBatch(material: 20, scale: 1f),
            CreateTerrainBatch(material: 10, scale: 1f)
        };

        Assert.AreEqual(
            2,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 1, 2, 3 },
                new[] { true, true, true, true },
                batches));
        Assert.AreEqual(
            1,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 1 },
                new[] { true, false },
                batches));
        Assert.AreEqual(
            1,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 2 },
                new[] { true, true },
                batches));
        Assert.IsTrue(TerrainBatching.AreCompatible(
            CreateTerrainBatch(material: 10, scale: 1f),
            CreateTerrainBatch(material: 10, scale: 2f)));

        var retainedRunLengths = TerrainBatching.BuildCompatibleRunLengths(batches);
        CollectionAssert.AreEqual(new[] { 2, 1, 1, 1 }, retainedRunLengths);
        Assert.AreEqual(
            2,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 1, 2, 3 },
                new[] { true, true, true, true },
                retainedRunLengths));
        Assert.AreEqual(
            1,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 1 },
                new[] { true, false },
                retainedRunLengths));
    }

    [TestMethod]
    public void TerrainShaderVariants_SelectSmallestFixedLayerBucket()
    {
        Assert.AreEqual(1, TerrainBatching.GetShaderLayerCount(1));
        Assert.AreEqual(2, TerrainBatching.GetShaderLayerCount(2));
        Assert.AreEqual(4, TerrainBatching.GetShaderLayerCount(3));
        Assert.AreEqual(4, TerrainBatching.GetShaderLayerCount(4));
        Assert.AreEqual(8, TerrainBatching.GetShaderLayerCount(5));
        Assert.IsFalse(TerrainBatching.UsesHeightTextures(4, new[] { 0, 0, 0, 0 }));
        Assert.IsFalse(TerrainBatching.UsesHeightTextures(2, new[] { 0, 0, 123, 0 }));
        Assert.IsTrue(TerrainBatching.UsesHeightTextures(2, new[] { 0, 123, 0, 0 }));
    }

    [TestMethod]
    public void TerrainBrushTools_KeepSharedPipelineAndModeSpecificOperationsSeparate()
    {
        var vertices = new ADTVertex[145];
        var sample = new TerrainBrushSample(
            vertices,
            0,
            10f,
            2f,
            8f,
            20f,
            EditAction.Positive);

        Assert.AreEqual(12f, TerrainBrushTools.Get(TerrainBrushMode.Sculpt).Apply(sample));
        Assert.AreEqual(20f, TerrainBrushTools.Get(TerrainBrushMode.Flatten).Apply(sample));
        Assert.AreEqual(1, TerrainBrushTools.Get(TerrainBrushMode.Sculpt).GetPassCount(8));
        Assert.AreEqual(8, TerrainBrushTools.Get(TerrainBrushMode.Smooth).GetPassCount(8));
    }

    [TestMethod]
    public void TerrainSurfaceEditor_WeldsSeamsAndRebuildsNormalsAfterSmoothing()
    {
        var left = new[]
        {
            TerrainVertex(0, 0, 0), TerrainVertex(1, 0, 0), TerrainVertex(0, 1, 10)
        };
        var right = new[]
        {
            TerrainVertex(1, 0, 0), TerrainVertex(0, 1, 10), TerrainVertex(1, 1, 0)
        };
        var input = new TerrainBrushInput
        {
            ToolMode = TerrainBrushMode.Smooth,
            Action = EditAction.Positive,
            Speed = 1,
            SmoothIterations = 1
        };

        Assert.IsTrue(TerrainSurfaceEditor.Apply(
            [
                new(left, [0, 1, 2], Matrix4x4.Identity),
                new(right, [0, 1, 2], Matrix4x4.Identity)
            ],
            new Vector3(.5f, .5f, 0),
            new BrushInput(2, 0, false, BrushShape.Circle, BrushFalloffProfile.Hard),
            input,
            1));

        Assert.AreEqual(left[1].Position.Z, right[0].Position.Z, .0001f);
        Assert.AreEqual(left[2].Position.Z, right[1].Position.Z, .0001f);
        Assert.IsTrue(left.Concat(right).All(vertex => vertex.Normal.Z > .99f));
    }

    [TestMethod]
    public void TerrainFlattenTargets_ExposeFixedAndStrokeCentreModes()
    {
        var editor = new TerrainEditingViewModel();
        CollectionAssert.AreEqual(
            new[] { "Fixed height", "Brush centre (stroke start)" },
            editor.FlattenTargets.ToArray());
        editor.FlattenTarget = 1;
        Assert.IsFalse(editor.IsFixedFlattenHeight);
    }

    [TestMethod]
    public void TextureSmooth_BlendsTheWholeNormalizedLayerVector()
    {
        var weights = TextureLayerMath.Smooth(
            new Vector4(1, 0, 0, 0),
            new Vector4(0, .5f, .5f, 0),
            .5f);

        Assert.AreEqual(.5f, weights.X, .0001f);
        Assert.AreEqual(.25f, weights.Y, .0001f);
        Assert.AreEqual(.25f, weights.Z, .0001f);
        Assert.AreEqual(1f, weights.X + weights.Y + weights.Z + weights.W, .0001f);
    }

    [TestMethod]
    public void TerrainAlphaSampler_MatchesPackedChannelsAndPrefersHigherLayerOnTies()
    {
        var firstGroup = new byte[TerrainAlphaMapSampler.Size * TerrainAlphaMapSampler.Size * 4];
        for (var pixel = 0; pixel < TerrainAlphaMapSampler.Size * TerrainAlphaMapSampler.Size; pixel++)
        {
            firstGroup[pixel * 4 + 1] = 102;
            firstGroup[pixel * 4 + 2] = 102;
        }

        var weights = TerrainAlphaMapSampler.SampleWeights(
            [firstGroup],
            layerCount: 3,
            new Vector2(0.321f, 0.654f));

        Assert.AreEqual(0.36f, weights[0], 0.0001f);
        Assert.AreEqual(0.24f, weights[1], 0.0001f);
        Assert.AreEqual(0.4f, weights[2], 0.0001f);
        Assert.AreEqual(2, TerrainAlphaMapSampler.FindDominantLayer(weights, [10, 20, 30]));
    }

    [TestMethod]
    public void TerrainAlphaSampler_ClampsChunkEdgesInsteadOfWrapping()
    {
        var alpha = new byte[TerrainAlphaMapSampler.Size * TerrainAlphaMapSampler.Size * 4];
        for (var row = 0; row < TerrainAlphaMapSampler.Size; row++)
            alpha[((row * TerrainAlphaMapSampler.Size + 63) * 4) + 1] = 255;

        var left = TerrainAlphaMapSampler.SampleWeights([alpha], 2, new Vector2(0f, 0.5f));
        var right = TerrainAlphaMapSampler.SampleWeights([alpha], 2, new Vector2(1f, 0.5f));

        Assert.AreEqual(0f, left[1]);
        Assert.AreEqual(1f, right[1]);
    }

}
