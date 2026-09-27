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
    public void Camera_SetDirectionRestoresFrontVector()
    {
        var expectedDirection = Vector3.Normalize(new Vector3(-0.25f, 0.5f, 0.75f));
        var camera = new Camera(Vector3.Zero, 0f, 0f, 1f);

        camera.SetDirection(expectedDirection);

        Assert.AreEqual(expectedDirection.X, camera.Front.X, 1e-5f);
        Assert.AreEqual(expectedDirection.Y, camera.Front.Y, 1e-5f);
        Assert.AreEqual(expectedDirection.Z, camera.Front.Z, 1e-5f);
    }

    [TestMethod]
    public void Camera_SetDirectionRejectsNonFiniteVectors()
    {
        var camera = new Camera(Vector3.Zero, 45f, 10f, 1f);
        var expected = camera.Front;

        camera.SetDirection(new Vector3(float.NaN, 0f, 1f));

        Assert.AreEqual(expected, camera.Front);
    }

    [TestMethod]
    public void Camera_ConstructorAppliesOrientationAndAspectRatio()
    {
        var camera = new Camera(Vector3.Zero, yaw: 90f, pitch: 30f, aspectRatio: 16f / 9f);

        Assert.AreEqual(90f, camera.Yaw);
        Assert.AreEqual(30f, camera.Pitch);
        Assert.AreEqual(16f / 9f, camera.AspectRatio, 1e-5f);
        Assert.AreEqual(0f, camera.Front.X, 1e-5f);
        Assert.AreEqual(MathF.Cos(Camera.DegreesToRadians(30f)), camera.Front.Y, 1e-5f);
        Assert.AreEqual(0.5f, camera.Front.Z, 1e-5f);
    }

    [TestMethod]
    public void RenderingConfiguration_NormalizeReplacesNonFiniteValuesWithDefaults()
    {
        var defaults = new RenderingConfiguration();
        var normalized = new RenderingConfiguration
        {
            AmbientColor = new Vector3(float.NaN, float.PositiveInfinity, float.NegativeInfinity),
            DiffuseColor = new Vector3(float.NaN, float.PositiveInfinity, float.NegativeInfinity),
            TerrainRenderDistance = float.NaN,
            ModelRenderDistance = float.PositiveInfinity,
            AnimationRenderDistancePercent = float.NaN,
            ParticleRenderDistancePercent = float.PositiveInfinity,
            MinimumModelScreenSizePixels = float.NegativeInfinity,
            TerrainLodTransitionPixels = float.NaN,
            MovementSpeed = float.PositiveInfinity,
            MouseSensitivity = float.NaN
        }.Normalize();

        Assert.AreEqual(defaults.AmbientColor, normalized.AmbientColor);
        Assert.AreEqual(defaults.DiffuseColor, normalized.DiffuseColor);
        Assert.AreEqual(defaults.TerrainRenderDistance, normalized.TerrainRenderDistance);
        Assert.AreEqual(defaults.ModelRenderDistance, normalized.ModelRenderDistance);
        Assert.AreEqual(defaults.AnimationRenderDistancePercent, normalized.AnimationRenderDistancePercent);
        Assert.AreEqual(defaults.ParticleRenderDistancePercent, normalized.ParticleRenderDistancePercent);
        Assert.AreEqual(defaults.MinimumModelScreenSizePixels, normalized.MinimumModelScreenSizePixels);
        Assert.AreEqual(defaults.TerrainLodTransitionPixels, normalized.TerrainLodTransitionPixels);
        Assert.AreEqual(defaults.MovementSpeed, normalized.MovementSpeed);
        Assert.AreEqual(defaults.MouseSensitivity, normalized.MouseSensitivity);
    }

    [TestMethod]
    public void ScreenSpaceCulling_UsesProjectedSphereDiameter()
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(
            MathF.PI / 4f,
            16f / 9f,
            1f,
            100_000f);
        var diameter = ScreenSpaceCulling.EstimateProjectedDiameterPixels(
            Vector3.Zero,
            Vector3.UnitX,
            new Vector3(1_000f, 0f, 0f),
            1f,
            projection.M22,
            1_080);

        Assert.AreEqual(2.61f, diameter, 0.02f);
        Assert.IsFalse(ScreenSpaceCulling.IsBelowPixelThreshold(
            Vector3.Zero, Vector3.UnitX, new Vector3(1_000f, 0f, 0f), 1f,
            projection.M22, 1_080, 1f));
        Assert.IsTrue(ScreenSpaceCulling.IsBelowPixelThreshold(
            Vector3.Zero, Vector3.UnitX, new Vector3(10_000f, 0f, 0f), 1f,
            projection.M22, 1_080, 1f));
        Assert.IsFalse(ScreenSpaceCulling.IsBelowPixelThreshold(
            Vector3.Zero, Vector3.UnitX, new Vector3(10_000f, 0f, 0f), 1f,
            projection.M22, 1_080, 0f));

        Assert.AreEqual(
            diameter,
            ScreenSpaceCulling.EstimateProjectedDiameterPixelsNormalized(
                Vector3.Zero,
                Vector3.UnitX,
                new Vector3(1_000f, 0f, 0f),
                1f,
                projection.M22,
                1_080),
            0.0001f);
    }

    [TestMethod]
    public void ScreenSpaceCulling_OffsetThresholdMatchesProjectedDiameter()
    {
        var forward = Vector3.UnitX;
        foreach (var center in new[]
                 {
                     new Vector3(-1f, 0f, 0f),
                     new Vector3(1f, 0f, 0f),
                     new Vector3(100f, 20f, 0f),
                     new Vector3(10_000f, 0f, 0f)
                 })
        foreach (var radius in new[] { -1f, 0f, 0.5f, 5f })
        foreach (var threshold in new[] { 0f, 1f, 10f, float.PositiveInfinity, float.NaN })
        {
            var expected = ScreenSpaceCulling.IsBelowPixelThresholdNormalized(
                Vector3.Zero, forward, center, radius, 2.4f, 1_080, threshold);
            var actual = ScreenSpaceCulling.IsBelowPixelThresholdNormalizedFromOffset(
                center, forward, radius, 2.4f, 1_080, threshold);
            Assert.AreEqual(expected, actual,
                $"center={center}, radius={radius}, threshold={threshold}");
        }
    }

    [TestMethod]
    public void Frustum_ClassifiesBoxesForHierarchicalTerrainCulling()
    {
        var frustum = new Frustum();
        frustum.ExtractFromMatrix(Matrix4x4.Identity);

        Assert.AreEqual(
            Frustum.BoxIntersection.Inside,
            frustum.ClassifyAxisAlignedBox(new Vector3(-0.5f), new Vector3(0.5f)));
        Assert.AreEqual(
            Frustum.BoxIntersection.Intersecting,
            frustum.ClassifyAxisAlignedBox(
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(1.5f, 0.5f, 0.5f)));
        Assert.AreEqual(
            Frustum.BoxIntersection.Outside,
            frustum.ClassifyAxisAlignedBox(
                new Vector3(2f, -0.5f, -0.5f),
                new Vector3(3f, 0.5f, 0.5f)));
    }

    [TestMethod]
    public void TileSceneBounds_AggregatesChildrenAndRebuildsAfterExplicitInvalidation()
    {
        var tileBounds = new TileSceneBounds(new MapTile
        {
            WdtPath = "world/maps/Test/Test.wdt",
            TileX = 2,
            TileY = 3
        });
        var child = new TestBoundsContainer(new BoundingBox(
            new Vector3(2f, -2f, 0.5f),
            new Vector3(4f, 0.5f, 3f)));

        tileBounds.SetTerrain(42, new BoundingBox(Vector3.Zero, Vector3.One));
        tileBounds.AddObject(child);

        Assert.IsTrue(tileBounds.TryGetCombinedBounds(out var combined));
        Assert.AreEqual(new Vector3(0f, -2f, 0f), combined.Min);
        Assert.AreEqual(new Vector3(4f, 1f, 3f), combined.Max);
        Assert.IsFalse(tileBounds.IsDirty);

        child.SetBounds(new BoundingBox(
            new Vector3(-5f, -4f, -3f),
            new Vector3(-2f, -1f, -0.5f)));
        tileBounds.MarkDirty();

        Assert.IsTrue(tileBounds.IsDirty);
        Assert.IsTrue(tileBounds.TryGetCombinedBounds(out combined));
        Assert.AreEqual(new Vector3(-5f, -4f, -3f), combined.Min);
        Assert.AreEqual(Vector3.One, combined.Max);
    }

    [TestMethod]
    public void TileSceneBounds_DoesNotCullWithIncompleteChildBounds()
    {
        var tileBounds = new TileSceneBounds(default);
        tileBounds.SetTerrain(7, new BoundingBox(Vector3.Zero, Vector3.One));
        tileBounds.AddObject(new TestBoundsContainer(null));

        Assert.IsFalse(tileBounds.TryGetCombinedBounds(out _));
        Assert.IsTrue(tileBounds.IsDirty);
    }

    [TestMethod]
    public void M2RenderBounds_ContainEveryUploadedVertex()
    {
        var vertices = new[]
        {
            new Vector3(-2, -3, -4),
            new Vector3(10, 5, 6),
            new Vector3(1, 20, 2)
        };

        var (box, radius) = WoWRenderLib.Loaders.M2Loader.CalculateRenderBounds(vertices);

        Assert.AreEqual(new Vector3(-2, -3, -4), box.Min);
        Assert.AreEqual(new Vector3(10, 20, 6), box.Max);
        foreach (var vertex in vertices)
        {
            Assert.IsTrue(
                Vector3.Distance(box.Center, vertex) <= radius + 0.0001f,
                $"Render vertex {vertex} escaped the calculated sphere.");
        }
    }

    [TestMethod]
    public void M2WorldSphere_AppliesAdtPlacementScaleRotationAndTranslation()
    {
        var local = new BoundingSphere(new Vector3(1, 2, 3), 5f);
        var transform = Matrix4x4.CreateScale(3f) *
                        Matrix4x4.CreateRotationZ(MathF.PI / 4f) *
                        Matrix4x4.CreateTranslation(100, -50, 20);

        var world = BoundingSphere.Transform(local, transform);

        Assert.AreEqual(15f, world.Radius, 0.0001f);
        Assert.IsTrue(world.Center.X > 90f);
        Assert.IsTrue(world.Center.Y < -35f);
    }

    [TestMethod]
    public void WmoEnabledGroupSignature_UsesMaskContentsRatherThanArrayIdentity()
    {
        var first = new[] { true, false, true, true, false, false, false, false, true };
        var sameContents = first.ToArray();
        var different = first.ToArray();
        different[1] = true;

        Assert.AreEqual(
            WMOContainer.CreateEnabledGroupSignature(first),
            WMOContainer.CreateEnabledGroupSignature(sameContents));
        Assert.AreNotEqual(
            WMOContainer.CreateEnabledGroupSignature(first),
            WMOContainer.CreateEnabledGroupSignature(different));
    }

}
