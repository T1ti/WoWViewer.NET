using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WmoSceneViewerQueryTests
{
    private static readonly Vector3 Eye = new(0f, 0f, 1f);
    private static readonly BoundingBox Bounds = new(new(-10f, -10f, -2000f), new(10f, 10f, 10f));

    [TestMethod]
    public void LoadedDoodadOpacityReachesInstanceStreamsAndSeparatesFadedDrawGroups()
    {
        using var scene = new CpuScene();
        var model = QueueModel(QueueGroup(10, 8) with { doodadReferences = [0, 1, 2, 3] });
        model.doodads = [default, default, default, default];
        model.doodadsReferencedByGroups = [true, true, true, true];
        var placement = scene.Add(model);
        var box = new BoundingBox(Vector3.Zero, new(2, 2, 2));
        scene.AddDoodad(placement, 0, new(new(80, 0, 1), 0.01f), box);
        scene.AddDoodad(placement, 1, new(new(95, 0, 1), 0.01f), box);
        scene.AddDoodad(placement, 2, new(new(97.5f, 0, 1), 0.01f), box);
        scene.AddDoodad(placement, 3, new(new(85, 0, 1), 0.01f), box);
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(new(default, default, true), true, Eye, Vector3.UnitX, QueueCamera,
            scene.Objects, doodadFade: new(1));
        placement.SetPortalVisibilityFrame(32);
        Assert.IsTrue(placement.TryGetDoodadSubmissionOpacity(1, 32, out var opacity));
        Assert.AreEqual(0.5f, opacity);
        Assert.IsFalse(placement.TryGetDoodadSubmissionOpacity(1, 33, out _));
        var data = M2InstanceData.ForScene(placement.ActiveDoodads[1], Matrix4x4.Identity, 32, default, null);
        Assert.AreEqual(new Vector4(0.5f, 1, 0, 0), data.RenderParameters);
        Assert.AreEqual(Vector4.Zero, M2InstanceData.ForScene(placement.ActiveDoodads[1], Matrix4x4.Identity,
            33, default, null).RenderParameters);

        var source = new M2AnimationDrawGroup();
        var pose = new M2AnimationPose { Materials = [new() { Color = Vector4.One }] };
        source.Reset(pose);
        source.AddInstance(0); source.AddInstance(1); source.AddInstance(2); source.AddInstance(3);
        var builder = new M2DoodadFadeDrawGroups();
        var groups = builder.Build([source], placement.ActiveDoodads, 32);
        Assert.AreEqual(3, groups.Count);
        Assert.IsTrue(groups.All(group => ReferenceEquals(pose, group.Pose)));
        Assert.AreEqual(1f, groups[0].DoodadOpacity);
        Assert.AreEqual(0.5f, groups[1].DoodadOpacity);
        Assert.AreEqual(0.25f, groups[2].DoodadOpacity);
        Assert.AreEqual(2, groups[0].Indices.Count);
        Assert.AreEqual(1, groups[1].Indices.Count);
        Assert.AreEqual(1, groups[2].Indices.Count);
        Assert.AreEqual((true, true), M2DoodadFadeDrawGroups.Passes(groups, [new() { blendType = 0 }], true));
        pose.Materials = [new() { Color = new(1, 1, 1, 0.5f) }];
        groups = builder.Build([source], placement.ActiveDoodads, 32);
        Assert.AreEqual(4, groups.Count); // Material alpha also excludes the native instanced path.
        Assert.IsTrue(groups.All(group => group.Indices.Count == 1));
        Assert.AreEqual((false, true), M2DoodadFadeDrawGroups.Passes(groups, [new() { blendType = 0 }], true));
        // Full-alpha additive layers of an opaque base stay in its opaque group.
        pose.Materials = [new() { Color = Vector4.One }];
        Submesh[] layered = [new() { blendType = 3, baseBlendType = 0 }];
        groups = builder.Build([source], placement.ActiveDoodads, 32, layered);
        Assert.AreEqual(3, groups.Count);
        Assert.AreEqual(2, groups[0].Indices.Count);
        Assert.AreEqual((true, true), M2DoodadFadeDrawGroups.Passes(groups, layered, true));
        // A translucent base excludes every layer from the native instanced path,
        // even when this layer's blend is opaque and instance alpha is one.
        layered = [new() { blendType = 0, baseBlendType = 2 }];
        groups = builder.Build([source], placement.ActiveDoodads, 32, layered);
        Assert.AreEqual(4, groups.Count);
        Assert.IsTrue(groups.All(group => group.Indices.Count == 1));
        Assert.AreEqual((false, true), M2DoodadFadeDrawGroups.Passes(groups, layered, true));
        var retained = groups[0];
        groups = builder.Build([source], placement.ActiveDoodads, 33);
        Assert.AreEqual(1, groups.Count);
        Assert.AreSame(retained, groups[0]);
        Assert.IsFalse(groups[0].NativeDoodadFade);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, groups[0].Indices.ToArray());
        Assert.AreEqual((true, false), M2DoodadFadeDrawGroups.Passes(groups, [new() { blendType = 0 }], true));
    }

    [DataTestMethod]
    [DataRow(1.5f, true, true)]
    [DataRow(1f, true, false)]
    [DataRow(1f, false, true)]
    [DataRow(0.5f, false, false)]
    public void ScenePreparationAppliesDetailAndObjectFadeToLoadedDoodads(float detail, bool objectFade, bool visible)
    {
        using var scene = new CpuScene();
        var model = QueueModel(QueueGroup(10, 8) with { doodadReferences = [0] });
        model.doodads = [default];
        model.doodadsReferencedByGroups = [true];
        var placement = scene.Add(model);
        var sphere = new WoWRenderLib.Raycasting.BoundingSphere(new(99.96f, 0, 0.5f), 0.01f);
        scene.AddDoodad(placement, 0, sphere, new(new(99, -1, 0), new(101, 1, 1)));
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(new(default, default, true), true, Eye, Vector3.UnitX, QueueCamera,
            scene.Objects, doodadFade: new(detail, objectFade));
        placement.SetPortalVisibilityFrame(31);
        Assert.AreEqual(visible, placement.IsDoodadPortalVisible(0, 31, sphere));
        Assert.IsTrue(placement.IsDoodadPortalVisible(0, 32, sphere));
    }

    [TestMethod]
    public void ScenePreparationEnlistsLoadedExteriorDoodadsAcrossBandsWithProfileAndFrameGuards()
    {
        using var scene = new CpuScene();
        var model = QueueModel(QueueGroup(10, 8) with { doodadReferences = [0, 1, 2] });
        model.doodads = [default, default, default];
        model.doodadsReferencedByGroups = [true, true, true];
        var placement = scene.Add(model);
        var inside = new WoWRenderLib.Raycasting.BoundingSphere(new(101, 0, 0.5f), 0.01f);
        var cutoff = new WoWRenderLib.Raycasting.BoundingSphere(new(2500, 0, 0.5f), 0.01f);
        var outside = new WoWRenderLib.Raycasting.BoundingSphere(new(101, 2, 0.5f), 0.01f);
        scene.AddDoodad(placement, 0, inside);
        scene.AddDoodad(placement, 1, cutoff);
        scene.AddDoodad(placement, 2, outside);
        var preparation = PrepareScene(scene);
        placement.SetPortalVisibilityFrame(30);
        Assert.IsTrue(Groups(placement)[0]);
        Assert.IsTrue(placement.IsDoodadPortalVisible(0, 30, inside));
        Assert.IsTrue(placement.TryGetDoodadCurrentFog(0, 30, out var current));
        Assert.IsFalse(current); // fresh definitions start with staged fog
        Assert.IsFalse(placement.IsDoodadPortalVisible(1, 30, cutoff));
        Assert.IsFalse(placement.IsDoodadPortalVisible(2, 30, outside));
        Assert.IsTrue(placement.IsDoodadPortalVisible(1, 31, cutoff));
        placement.EnabledGroups[0] = false;
        PrepareScene(scene, preparation: preparation);
        Assert.IsFalse(placement.IsDoodadPortalVisible(0, 30, inside));
        preparation.Prepare(new(default, default, true), false, Eye, Vector3.UnitX, QueueCamera, scene.Objects);
        Assert.IsFalse(placement.TryGetDoodadCurrentFog(0, 30, out _));
    }

    [TestMethod]
    public void ScenePreparationCarriesPortalSphereAdmissionAndFogIntoThePlacementFrame()
    {
        using var scene = new CpuScene();
        var root = Floor(0) with { portalLinks =
            [new() { PortalIndex = 0, TargetGroupIndex = 1, Side = 1 },
             new() { PortalIndex = 1, TargetGroupIndex = 2, Side = 1 }] };
        var model = Model(root, Floor(-1) with { doodadReferences = [0] },
            Floor(-1, 0x40) with { doodadReferences = [1] });
        model.portalGraphValid = true;
        model.portals = [ClipPortal(0.5f, 0.2f), ClipPortal(0.6f, 0.2f)];
        model.doodads = [default, default];
        model.doodadsReferencedByGroups = [true, true];
        var placement = scene.Add(model);
        var preparation = new WmoScenePortalPreparation();
        var viewer = scene.Locate();
        Assert.AreSame(placement, viewer.Primary.Instance);
        preparation.Prepare(viewer, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        placement.SetPortalVisibilityFrame(20);
        var inside = new WoWRenderLib.Raycasting.BoundingSphere(new(0, 0, 0.75f), 0.01f);
        var outside = new WoWRenderLib.Raycasting.BoundingSphere(new(0.5f, 0, 0.75f), 0.01f);
        Assert.IsTrue(placement.IsDoodadPortalVisible(0, 20, inside));
        Assert.IsTrue(placement.TryGetDoodadCurrentFog(0, 20, out var current));
        Assert.IsTrue(current);
        Assert.IsTrue(placement.IsDoodadPortalVisible(1, 20, inside));
        Assert.IsTrue(placement.TryGetDoodadCurrentFog(1, 20, out current));
        Assert.IsFalse(current);
        Assert.IsFalse(placement.IsDoodadPortalVisible(0, 20, outside));
        Assert.IsTrue(placement.IsDoodadPortalVisible(0, 21, outside));
        Assert.IsFalse(placement.TryGetDoodadCurrentFog(1, 21, out _));
        preparation.Prepare(viewer, false, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.IsFalse(placement.TryGetDoodadCurrentFog(1, 20, out _));
        preparation.Prepare(viewer, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.IsFalse(placement.TryGetDoodadCurrentFog(1, 20, out _));
        Assert.IsTrue(placement.IsDoodadPortalVisible(1, 20, inside));
        Assert.IsTrue(placement.TryGetDoodadCurrentFog(1, 20, out current));
        Assert.IsFalse(current);
    }

    [TestMethod]
    public void EqualHitsFollowSceneInsertionOrderAcrossRepeatedAssets()
    {
        using var scene = new CpuScene();
        var first = scene.Add(Model(Floor(0f)));
        var middle = scene.Add(Model(Floor(0f)));
        var last = scene.Add(first.FileDataId);
        last.PlacementFlags = 0x420; // MODF flags must not become runtime flags.
        var result = scene.Locate();
        Assert.AreSame(last, result.Primary.Instance);
        scene.Buckets.Clear();
        scene.Buckets[(middle.FileDataId, "")] = [middle];
        scene.Buckets[(first.FileDataId, "")] = [first, last];
        Assert.AreSame(last, scene.Locate().Primary.Instance);
        scene.Objects.Reverse();
        Assert.AreSame(first, scene.Locate().Primary.Instance);
        Assert.AreEqual(0u, last.ViewerRuntimeFlags);
    }

    [TestMethod]
    public void ExteriorWinnerClearsInteriorAndBlocksLaterFartherGeometry()
    {
        using var scene = new CpuScene();
        scene.Add(Model(Floor(0f)));
        scene.Add(Model(Floor(0f, 8)));
        scene.Add(Model(Floor(-5f)));
        Assert.IsNull(scene.Locate().Primary.Instance);
        var last = scene.Add(Model(Floor(0f)));
        Assert.AreSame(last, scene.Locate().Primary.Instance);
    }

    [TestMethod]
    public void QueryWithoutAHitKeepsTheEarlierWinner()
    {
        using var scene = new CpuScene();
        var first = scene.Add(Model(Floor(0f)));
        scene.Add(Model(Floor(0f, 0x410080)));
        Assert.AreSame(first, scene.Locate().Primary.Instance);
    }

    [TestMethod]
    public void LiveTransformMovesPlacementIntoIndependentSecondaryPool()
    {
        using var scene = new CpuScene();
        var normal = scene.Add(Model(Floor(0f)));
        var updated = scene.Add(Model(Floor(0f)));
        updated.ModelMatrix = Matrix4x4.CreateTranslation(0f, 0f, 0.5f);
        var result = scene.Locate();
        Assert.AreEqual(0x400u, updated.ViewerRuntimeFlags);
        Assert.AreSame(normal, result.Primary.Instance);
        Assert.AreSame(updated, result.Secondary.Instance);
        normal.EnabledGroups[0] = false;
        result = scene.Locate();
        Assert.AreSame(updated, result.Primary.Instance);
        Assert.IsNull(result.Secondary.Instance);
    }

    [TestMethod]
    public void StoredFileBoundsControlBroadPhaseUntilATransformRebuildsThem()
    {
        using var scene = new CpuScene();
        var placement = scene.Add(Model(Floor(0f)));
        placement.InitializeFileViewerPlacement(new(new(100f, 100f, -10f), new(110f, 110f, 10f)));
        Assert.IsNull(scene.Locate().Primary.Instance);
        Assert.AreEqual(0, scene.TerrainQueries);
        placement.InvalidateTransform();
        placement.ModelMatrix = Matrix4x4.Identity;
        Assert.AreSame(placement, scene.Locate().Primary.Instance);
        Assert.AreEqual(1, scene.TerrainQueries);
    }

    [TestMethod]
    public void PortalOverrideChangesTheCapForFollowingPlacements()
    {
        using var scene = new CpuScene();
        scene.Add(Model(Floor(0f)));
        var portalModel = WithPortal(-0.15f);
        scene.Add(portalModel);
        var last = scene.Add(Model(Floor(-0.1f)));
        Assert.AreSame(last, scene.Locate().Primary.Instance);
        Assert.AreEqual(1, scene.TerrainQueries);
        scene.TerrainLimit = 0.5f;
        Assert.IsNull(scene.Locate().Primary.Instance);
        Assert.AreEqual(1, scene.TerrainQueries);
    }

    [TestMethod]
    public void PortalCanExtendPoolCapPastOriginalSegmentLength()
    {
        using var scene = new CpuScene();
        scene.Add(WithPortal(-1759.1f));
        // The face straddles the original segment's digest AABB, while its
        // intersection lies just past the endpoint. Native t uses the pool cap.
        var face = Floor(-1759.2f);
        face.raycastVertices[2].Z = -1758.9f;
        var next = scene.Add(Model(face));
        Assert.AreSame(next, scene.Locate().Primary.Instance);
    }

    [TestMethod]
    public void SharedSceneResultsAssignSeedsOnlyToTheTwoSelectedPlacements()
    {
        using var scene = new CpuScene();
        var normal = scene.Add(WithPortal(-0.15f));
        var unselected = scene.Add(Model(Floor(-5f)));
        var updated = scene.Add(Model(Floor(0f)));
        updated.ModelMatrix = Matrix4x4.CreateTranslation(0f, 0f, 0.5f);
        var result = scene.Locate();
        Assert.IsTrue(result.UsesWrath335Rules);
        Assert.AreEqual(new WmoViewerGroups(1, 0), result.GetViewerGroups(normal));
        Assert.AreEqual(new WmoViewerGroups(0, -1), result.GetViewerGroups(updated));
        Assert.AreEqual(new WmoViewerGroups(-1, -1), result.GetViewerGroups(unselected));
        Assert.IsNull(default(WmoSceneViewerResult).GetViewerGroups(normal));
    }

    [TestMethod]
    public void OtherLegacyClientsRetainTheirExistingFirstHitTieRule()
    {
        using var scene = new CpuScene();
        var model = Model(Floor(0f));
        model.wrath335 = false;
        var first = scene.Add(model);
        scene.Add(model);
        scene.Objects.Reverse();
        Assert.AreSame(first, scene.Locate().Primary.Instance);
    }

    [TestMethod]
    public void ScenePortalPreparationReusesPrimaryMasksWithoutAnotherBspQuery()
    {
        using var scene = new CpuScene();
        var model = Model(Floor(0f));
        model.portalGraphValid = true;
        var primary = scene.Add(model);
        var viewer = scene.Locate();
        primary.GetPortalVisibilityBuffers(model, out _, out _, out _, out var scratch);
        var epoch = scratch.ViewerBsp.Epoch;
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(viewer, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.IsFalse(preparation.Views.HasSkyView);
        Assert.IsTrue(preparation.TryGetPreparedVisibility(primary, out var applied, out var traversed));
        Assert.IsTrue(applied);
        Assert.AreEqual(0, traversed);
        Assert.AreEqual(epoch, scratch.ViewerBsp.Epoch);
        preparation.Prepare(viewer, false, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.IsTrue(preparation.Views.HasSkyView);
        Assert.IsFalse(preparation.UsesWrath335Rules);
        Assert.IsFalse(preparation.TryGetPreparedVisibility(primary, out _, out _));
    }

    [TestMethod]
    public void InvalidPrimaryPortalGraphKeepsSceneAndSkyVisible()
    {
        using var scene = new CpuScene();
        var primary = scene.Add(Model(Floor(0f)));
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(scene.Locate(), true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.IsTrue(preparation.Views.HasSkyView);
        Assert.IsTrue(preparation.Views.HasExteriorView);
        Assert.IsTrue(preparation.TryGetPreparedVisibility(primary, out var applied, out _));
        Assert.IsFalse(applied);
    }

    [TestMethod]
    public void ScenePortalPreparationDropsRootSkySeedWhenSecondaryExists()
    {
        using var scene = new CpuScene();
        var model = Model(Floor(0f, 0x40));
        model.portalGraphValid = true;
        var primary = scene.Add(model);
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(scene.Locate(), true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.AreEqual(WmoPortalRect.Full, preparation.Views.SkyRect);
        var updated = scene.Add(model);
        updated.InvalidateTransform();
        preparation.Prepare(scene.Locate(), true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.IsFalse(preparation.Views.HasSkyView);
        Assert.IsTrue(preparation.TryGetPreparedVisibility(primary, out var applied, out _));
        Assert.IsTrue(applied);
        Assert.IsTrue(preparation.TryGetPreparedVisibility(updated, out var secondaryApplied, out _));
        Assert.IsTrue(secondaryApplied);
    }

    [TestMethod]
    public void ScenePreparationBuildsPrimaryComplementAndDiscardsOtherPlacementAndOldFrameWindows()
    {
        using var scene = new CpuScene();
        var owner = Floor(0f) with { portalLinks = [new() { PortalIndex = 0, TargetGroupIndex = 1, Side = 1 }] };
        var model = Model(owner, Floor(-1f, 8));
        model.portalGraphValid = true;
        model.portals = [new() { Normal = Vector3.UnitZ, Distance = -0.5f,
            Vertices = [new(-0.2f, -0.2f, 0.5f), new(0.2f, -0.2f, 0.5f),
                new(0.2f, 0.2f, 0.5f), new(-0.2f, 0.2f, 0.5f)] }];
        var primary = scene.Add(model);
        var secondaryModel = model;
        secondaryModel.portals = [model.portals[0] with
            { Vertices = model.portals[0].Vertices.Select(v => new Vector3(v.X * 3f, v.Y * 3f, v.Z)).ToArray() }];
        var secondary = scene.Add(secondaryModel);
        secondary.InvalidateTransform(); // native secondary pool, distinct from the primary
        var viewer = scene.Locate();
        Assert.AreSame(primary, viewer.Primary.Instance);
        Assert.AreSame(secondary, viewer.Secondary.Instance);
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(viewer, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.AreEqual(1, preparation.Views.Windows.Length);
        Assert.AreEqual(new Wrath335PortalWindow(new(0.4f, 0.4f, 0.6f, 0.6f), 0.5f),
            preparation.Views.Windows[0]);
        CollectionAssert.AreEqual(new WmoPortalRect[] { new(0f, 0f, 1f, 0.4f),
            new(0f, 0.6f, 1f, 1f), new(0f, 0.4f, 0.4f, 0.6f), new(0.6f, 0.4f, 1f, 0.6f) },
            preparation.Views.Complement.Views.ToArray().Select(v => v.Rect).ToArray());
        preparation.Prepare(viewer, false, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.AreEqual(0, preparation.Views.Windows.Length);
        Assert.AreEqual(0, preparation.Views.Complement.Views.Length);
        preparation.Prepare(viewer, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        var invalidViewer = viewer with { Primary = viewer.Primary with
            { Model = viewer.Primary.Model with { portalGraphValid = false } } };
        preparation.Prepare(invalidViewer, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.AreEqual(0, preparation.Views.Windows.Length);
        Assert.AreEqual(0, preparation.Views.Complement.Views.Length);
        Assert.IsTrue(preparation.Views.HasSkyView);
        preparation.Prepare(viewer, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        preparation.Prepare(viewer with { UsesWrath335Rules = false }, true, Eye, -Vector3.UnitZ, Matrix4x4.Identity, scene.Objects);
        Assert.AreEqual(0, preparation.Views.Windows.Length);
        Assert.AreEqual(0, preparation.Views.Complement.Views.Length);
        Assert.IsFalse(preparation.UsesWrath335Rules);
    }

    [TestMethod]
    public void SceneExteriorSeedsInterleavePlacementsAndReemitOnReturningToAPlacement()
    {
        using var scene = new CpuScene();
        var a = scene.Add(QueueModel(QueueGroup(10f, 8, QueueLink(0, 2)),
            QueueGroup(80f, 8, QueueLink(0, 2)), QueueGroup(0f, 0)));
        var bModel = QueueModel(QueueGroup(40f, 8, QueueLink(0, 1)), QueueGroup(0f, 0));
        bModel.portals = [QueuePortal(0.1f)];
        var b = scene.Add(bModel);
        var preparation = PrepareScene(scene);
        AssertPolygonOrder(preparation, 0.4f, 0.45f, 0.4f);
        CollectionAssert.AreEqual(new[] { true, true, true }, Groups(a));
        Assert.IsTrue(preparation.TryGetPreparedVisibility(a, out var applied, out var references));
        Assert.IsTrue(applied);
        Assert.AreEqual(2, references);
        Assert.IsTrue(preparation.TryGetPreparedVisibility(b, out applied, out references));
        Assert.IsTrue(applied);
        Assert.AreEqual(1, references);
        Assert.AreEqual(0, scene.TerrainQueries);
        b.EnabledGroups[0] = false;
        PrepareScene(scene, preparation: preparation);
        AssertPolygonOrder(preparation, 0.4f); // Consecutive A seeds share emission bit 8.
        Assert.IsFalse(Groups(b)[0]);
    }

    [TestMethod]
    public void AlwaysDrawCallbackBetweenExteriorSeedsDoesNotSwitchPortalCachePlacement()
    {
        using var scene = new CpuScene();
        var a = scene.Add(QueueModel(QueueGroup(10f, 8, QueueLink(0, 2)),
            QueueGroup(80f, 8, QueueLink(0, 2)), QueueGroup(0f, 0)));
        var always = scene.Add(QueueModel(QueueGroup(40f, 0x10000, QueueLink(0, 1)), QueueGroup(0f, 0)));
        var preparation = PrepareScene(scene);
        AssertPolygonOrder(preparation, 0.4f);
        CollectionAssert.AreEqual(new[] { true, false }, Groups(always));
        Assert.IsTrue(preparation.TryGetPreparedVisibility(always, out var applied, out var references));
        Assert.IsTrue(applied);
        Assert.AreEqual(0, references);
        CollectionAssert.AreEqual(new[] { true, true, true }, Groups(a));
    }

    [TestMethod]
    public void RepeatedAssetsStillHaveIndependentPlacementCacheGenerations()
    {
        using var scene = new CpuScene();
        var first = scene.Add(QueueModel(QueueGroup(10f, 8, QueueLink(0, 2)),
            QueueGroup(80f, 8, QueueLink(0, 2)), QueueGroup(0f, 0)));
        scene.Add(first.FileDataId);
        var preparation = PrepareScene(scene);
        AssertPolygonOrder(preparation, 0.4f, 0.4f, 0.4f, 0.4f);
    }

    [TestMethod]
    public void PrimaryInteriorEmissionSuppressesItsExteriorSeedUntilAnotherPlacementIsVisited()
    {
        using var scene = new CpuScene();
        var primary = scene.Add(QueueModel(QueueGroup(0f, 0, QueueLink(0, 1)),
            QueueGroup(10f, 8, QueueLink(0, 0)), QueueGroup(80f, 8, QueueLink(0, 0))));
        var otherModel = QueueModel(QueueGroup(40f, 8, QueueLink(0, 1)), QueueGroup(0f, 0));
        otherModel.portals = [QueuePortal(0.1f)];
        var other = scene.Add(otherModel);
        var viewer = new WmoSceneViewerResult(new(primary, primary.GetWMO(), 0, -1), default, true);
        var preparation = PrepareScene(scene, viewer);
        Assert.AreEqual(1, preparation.Views.Windows.Length);
        AssertPolygonOrder(preparation, 0.45f, 0.4f);
        other.EnabledGroups[0] = false;
        PrepareScene(scene, viewer, preparation);
        Assert.AreEqual(1, preparation.Views.Windows.Length);
        Assert.AreEqual(0, preparation.Views.RenderViews.Views.Length);
    }

    [TestMethod]
    public void EnclosedViewerAcceptsUpdatedGroupsThroughTheLiveVisibleBoundsChain()
    {
        using var scene = new CpuScene();
        var primary = scene.Add(QueueModel(QueueGroup(0f, 0, width: 10f)));
        var first = scene.Add(QueueModel(QueueGroup(10f, 8, width: 10f)));
        var second = scene.Add(QueueModel(QueueGroup(20f, 8, width: 10f)));
        var disjoint = scene.Add(QueueModel(QueueGroup(35f, 8)));
        var ordinary = scene.Add(QueueModel(QueueGroup(0f, 8)));
        UpdatePlacement(first);
        UpdatePlacement(second);
        UpdatePlacement(disjoint);
        var viewer = new WmoSceneViewerResult(new(primary, primary.GetWMO(), 0, -1), default, true);
        var preparation = PrepareScene(scene, viewer);
        Assert.IsFalse(preparation.Views.HasExteriorView);
        Assert.IsTrue(Groups(first)[0]);
        Assert.IsTrue(Groups(second)[0]);
        Assert.IsFalse(Groups(disjoint)[0]);
        Assert.IsFalse(Groups(ordinary)[0]);
        scene.Objects.Remove(second);
        scene.Objects.Insert(1, second);
        PrepareScene(scene, viewer, preparation);
        Assert.IsFalse(Groups(second)[0]); // Rejected arrivals are not reconsidered.
        Assert.IsTrue(Groups(first)[0]);
    }

    [TestMethod]
    public void UpdatedSeedsUseFullCameraRectWhileOrdinarySeedsUseTheInteriorExteriorWindow()
    {
        using var scene = new CpuScene();
        var primary = scene.Add(QueueModel(QueueGroup(0f, 0, QueueLink(0, 1)), QueueGroup(10f, 8)));
        var sideGroup = QueueGroup(20f, 8) with
        { mogiBoundingBox = new(new(20f, 0.5f, 0.1f), new(21f, 0.8f, 0.8f)) };
        var ordinary = scene.Add(QueueModel(sideGroup));
        var updated = scene.Add(ordinary.FileDataId);
        UpdatePlacement(updated);
        var viewer = new WmoSceneViewerResult(new(primary, primary.GetWMO(), 0, -1), default, true);
        var preparation = PrepareScene(scene, viewer);
        Assert.IsTrue(preparation.Views.HasExteriorView);
        Assert.IsFalse(Groups(ordinary)[0]);
        Assert.IsTrue(Groups(updated)[0]);
    }

    [TestMethod]
    public void NoViewerRebucketCutoffStopsLaterUpdatedGroupsWithoutStoppingOrdinarySources()
    {
        using var scene = new CpuScene();
        var updated = scene.Add(QueueModel(QueueGroup(10f, 8), QueueGroup(3000f, 8), QueueGroup(20f, 8)));
        UpdatePlacement(updated);
        var ordinary = scene.Add(QueueModel(QueueGroup(3000f, 8), QueueGroup(40f, 8)));
        var later = scene.Add(QueueModel(QueueGroup(10f, 8)));
        UpdatePlacement(later);
        _ = PrepareScene(scene);
        CollectionAssert.AreEqual(new[] { true, false, false }, Groups(updated));
        CollectionAssert.AreEqual(new[] { false, true }, Groups(ordinary));
        Assert.IsFalse(Groups(later)[0]);
    }

    [TestMethod]
    public void SecondaryVisibleCallbacksSurviveWindowResetAndGateUpdatedExteriorGroups()
    {
        using var scene = new CpuScene();
        var primary = scene.Add(QueueModel(QueueGroup(0f, 0, width: 10f)));
        var secondary = scene.Add(QueueModel(QueueGroup(50f, 0, QueueLink(0, 1), 10f), QueueGroup(51f, 8)));
        UpdatePlacement(secondary);
        var updated = scene.Add(QueueModel(QueueGroup(60f, 8)));
        UpdatePlacement(updated);
        var viewer = new WmoSceneViewerResult(new(primary, primary.GetWMO(), 0, -1),
            new(secondary, secondary.GetWMO(), 0, -1), true);
        var preparation = PrepareScene(scene, viewer);
        Assert.AreEqual(0, preparation.Views.Windows.Length);
        Assert.IsFalse(preparation.Views.HasExteriorView);
        Assert.IsTrue(Groups(secondary)[0]);
        Assert.IsTrue(Groups(updated)[0]);
    }

    [TestMethod]
    public void ScenePreparationResetsVisibilityAndDoodadsWithoutLosingOtherClientFallback()
    {
        using var scene = new CpuScene();
        var model = QueueModel(QueueGroup(10f, 8) with { doodadReferences = [0] });
        model.doodads = [default, default];
        model.doodadsReferencedByGroups = [true, false];
        var placement = scene.Add(model);
        placement.PlacementFlags = 0x420;
        var otherClientModel = model with { wrath335 = false };
        var otherClient = scene.Add(otherClientModel);
        var invalid = scene.Add(model with { portalGraphValid = false });
        var preparation = PrepareScene(scene);
        placement.GetPortalVisibilityBuffers(placement.GetWMO(), out _, out var doodads, out _, out _);
        CollectionAssert.AreEqual(new[] { true, true }, doodads);
        Assert.IsTrue(Groups(placement)[0]); // MODF flags did not become runtime skip/update flags.
        Assert.IsFalse(preparation.TryGetPreparedVisibility(otherClient, out _, out _));
        Assert.IsTrue(preparation.TryGetPreparedVisibility(invalid, out var applied, out _));
        Assert.IsFalse(applied);
        placement.EnabledGroups[0] = false;
        PrepareScene(scene, preparation: preparation);
        Assert.IsFalse(Groups(placement)[0]);
        CollectionAssert.AreEqual(new[] { false, true }, doodads);
        scene.Objects.Remove(placement);
        PrepareScene(scene, preparation: preparation);
        Assert.IsFalse(preparation.TryGetPreparedVisibility(placement, out _, out _));
        preparation.Prepare(new(default, default, true), false, Eye, Vector3.UnitX,
            QueueCamera, scene.Objects);
        Assert.IsFalse(preparation.TryGetPreparedVisibility(invalid, out _, out _));
        Assert.AreEqual(0, preparation.Views.RenderViews.Views.Length);
        Assert.IsTrue(preparation.Views.HasSkyView);
    }

    [TestMethod]
    public void NativeMapOccluderRejectsExteriorGroupAndDropsItsStateOnMapAndProfileChanges()
    {
        using var scene = new CpuScene();
        Wrath335ClipVolumesTests.StormwindCamera(out var center, out var forward, out var eye, out var camera);
        var placement = scene.Add(Model(ClipGroup(8, -21f, -19f)) with { portalGraphValid = true });
        placement.ModelMatrix = ClipPlacement(center, forward);
        placement.InitializeFileViewerPlacement();
        var preparation = new WmoScenePortalPreparation();
        var viewer = new WmoSceneViewerResult(default, default, true);
        preparation.Prepare(viewer, true, eye, forward, camera, scene.Objects, 0);
        Assert.AreEqual(1, preparation.ClipVolumes.Volumes.Length);
        Assert.IsFalse(Groups(placement)[0]);
        preparation.Prepare(viewer, true, eye, forward, camera, scene.Objects, 1);
        Assert.IsTrue(Groups(placement)[0]);
        Assert.AreEqual(0, preparation.ClipVolumes.Volumes.Length);
        preparation.Prepare(viewer, true, eye, forward, camera, scene.Objects, 0);
        preparation.Prepare(viewer with { UsesWrath335Rules = false }, true,
            eye, forward, camera, scene.Objects, 0);
        Assert.AreEqual(0, preparation.ClipVolumes.Volumes.Length);
        Assert.IsFalse(preparation.TryGetPreparedVisibility(placement, out _, out _));
        preparation.Prepare(viewer, true, eye, forward, camera, scene.Objects, 0);
        preparation.Prepare(viewer, false, eye, forward, camera, scene.Objects, 0);
        Assert.AreEqual(0, preparation.ClipVolumes.Volumes.Length);
    }

    [DataTestMethod]
    [DataRow(8u, false)]
    [DataRow(0u, true)]
    [DataRow(0x40u, true)]
    public void ExteriorPortalOcclusionBypassUsesLoadedOwnerBitEightRatherThanRootOrExteriorLightFlags(
        uint loadedFlags, bool targetVisible)
    {
        using var scene = new CpuScene();
        Wrath335ClipVolumesTests.StormwindCamera(out var center, out var forward, out var eye, out var camera);
        var source = ClipGroup(8, -40f, 40f, 30f) with
        { flags = loadedFlags, portalLinks = QueueLink(0, 1) };
        var model = Model(source, ClipGroup(0, -21f, -19f)) with
        { portalGraphValid = true, portals = [ClipPortal(-20f, 5f)] };
        var placement = scene.Add(model);
        placement.ModelMatrix = ClipPlacement(center, forward);
        placement.InitializeFileViewerPlacement();
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(new(default, default, true), true, eye, forward, camera, scene.Objects, 0);
        Assert.AreEqual(1, preparation.ClipVolumes.Volumes.Length);
        Assert.IsTrue(Groups(placement)[0]); // Its sphere straddles the occluder cap.
        Assert.AreEqual(targetVisible, Groups(placement)[1]);
        Assert.AreEqual(targetVisible ? 1 : 0, preparation.Views.RenderViews.Views.Length);
    }

    [TestMethod]
    public void UpdatedExteriorPlacementBypassesSphereOcclusionWithAnInteriorExteriorWindow()
    {
        using var scene = new CpuScene();
        Wrath335ClipVolumesTests.StormwindCamera(out var center, out var forward, out var eye, out var camera);
        var primaryModel = Model(ClipGroup(0, -1f, 1f) with { portalLinks = QueueLink(0, 1) },
            ClipGroup(8, -1f, 1f)) with { portalGraphValid = true, portals = [ClipPortal(0f, 100f)] };
        var primary = scene.Add(primaryModel);
        primary.ModelMatrix = ClipPlacement(center - forward * 50f, forward);
        primary.InitializeFileViewerPlacement();
        var ordinary = scene.Add(Model(ClipGroup(8, -21f, -19f)) with { portalGraphValid = true });
        ordinary.ModelMatrix = ClipPlacement(center, forward);
        ordinary.InitializeFileViewerPlacement();
        var updated = scene.Add(ordinary.FileDataId);
        updated.ModelMatrix = ordinary.ModelMatrix;
        Assert.AreEqual(0x400u, updated.ViewerRuntimeFlags);
        var viewer = new WmoSceneViewerResult(new(primary, primary.GetWMO(), 0, -1), default, true);
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(viewer, true, eye, forward, camera, scene.Objects, 0);
        Assert.IsTrue(preparation.Views.HasExteriorView);
        Assert.AreEqual(1, preparation.ClipVolumes.Volumes.Length);
        Assert.IsFalse(Groups(ordinary)[0]);
        Assert.IsTrue(Groups(updated)[0]);
    }

    [TestMethod]
    public void ClosedPrimaryInteriorIsVisitedBeforeStaticVolumesAndSkipsTheirConstruction()
    {
        using var scene = new CpuScene();
        Wrath335ClipVolumesTests.StormwindCamera(out var center, out var forward, out var eye, out var camera);
        var primary = scene.Add(Model(ClipGroup(0, -21f, -19f)) with { portalGraphValid = true });
        primary.ModelMatrix = ClipPlacement(center, forward);
        primary.InitializeFileViewerPlacement();
        var viewer = new WmoSceneViewerResult(new(primary, primary.GetWMO(), 0, -1), default, true);
        var preparation = new WmoScenePortalPreparation();
        preparation.Prepare(viewer, true, eye, forward, camera, scene.Objects, 0);
        Assert.IsTrue(Groups(primary)[0]);
        Assert.IsFalse(preparation.Views.HasExteriorView);
        Assert.AreEqual(0, preparation.ClipVolumes.Volumes.Length);
    }

    [DataTestMethod]
    [DataRow(8u)]
    [DataRow(0x10000u)]
    public void TerrainEdgesUpdateAfterTheirBandAndGateBothExteriorAndAlwaysDrawSeeds(uint flags)
    {
        using var scene = new CpuScene();
        AddTerrain(scene, 50f, 60f, 20f);
        var sameBand = scene.Add(HorizonModel(55f, flags));
        var behind = scene.Add(HorizonModel(110f, flags));
        PrepareTerrainScene(scene);
        Assert.IsTrue(Groups(sameBand)[0]);
        Assert.IsFalse(Groups(behind)[0]);
    }

    [TestMethod]
    public void IntactChunkDefersItsEdgesUntilTheFarCornerBand()
    {
        using var scene = new CpuScene();
        AddTerrain(scene, 50f, 83f, 20f);
        var beforeFarBand = scene.Add(HorizonModel(75f, 8));
        var behind = scene.Add(HorizonModel(110f, 8));
        PrepareTerrainScene(scene);
        Assert.IsTrue(Groups(beforeFarBand)[0]);
        Assert.IsFalse(Groups(behind)[0]);
    }

    [DataTestMethod]
    [DataRow((ushort)1)]
    [DataRow(ushort.MaxValue)]
    public void HoleChunksEraseAfterIntactChunksEvenWhenTheyArrivedFirst(ushort holes)
    {
        using var scene = new CpuScene();
        AddTerrain(scene, 50f, 60f, 20f, holes);
        AddTerrain(scene, 50f, 60f, 20f);
        var behind = scene.Add(HorizonModel(110f, 8));
        var preparation = PrepareTerrainScene(scene);
        Assert.IsTrue(Groups(behind)[0]);
        Assert.IsFalse(preparation.TerrainOcclusion.Buffer.ContainsBox(
            new(new(109f, -1f, 0f), new(111f, 1f, 10f))));
    }

    [TestMethod]
    public void AChunkRejectedByAnEarlierHorizonCannotEraseItOrBecomeAnOccluder()
    {
        using var scene = new CpuScene();
        AddTerrain(scene, 50f, 60f, 20f);
        AddTerrain(scene, 100f, 110f, 20f, 1);
        var behind = scene.Add(HorizonModel(150f, 8));
        PrepareTerrainScene(scene);
        Assert.IsFalse(Groups(behind)[0]);
    }

    [DataTestMethod]
    [DataRow(70f, true)]
    [DataRow(1000f, false)]
    public void IntactTerrainProducerHonorsTheStrictFarDistanceWindow(float farClip, bool visible)
    {
        using var scene = new CpuScene();
        AddTerrain(scene, 50f, 60f, 20f);
        var behind = scene.Add(HorizonModel(110f, 8));
        PrepareTerrainScene(scene, farClip: farClip);
        Assert.AreEqual(visible, Groups(behind)[0]);
    }

    [DataTestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public void UpdatedGroupBypassesTerrainOnlyWithAPrimaryViewer(bool hasPrimary, bool updatedVisible)
    {
        using var scene = new CpuScene();
        var viewer = new WmoSceneViewerResult(default, default, true);
        if (hasPrimary)
        {
            var owner = Floor(0f) with { portalLinks = QueueLink(0, 1) };
            var primaryModel = Model(owner, HorizonModel(40f, 8).groupBatches[0]) with
            {
                portalGraphValid = true,
                portals = [new() { Normal = -Vector3.UnitX, Distance = 40f,
                    Vertices = [new(40f, -100f, -100f), new(40f, 100f, -100f),
                        new(40f, 100f, 100f), new(40f, -100f, 100f)] }]
            };
            var primary = scene.Add(primaryModel);
            viewer = viewer with { Primary = new(primary, primary.GetWMO(), 0, -1) };
        }
        AddTerrain(scene, 80f, 90f, 20f);
        var ordinary = scene.Add(HorizonModel(120f, 8));
        var updated = scene.Add(ordinary.FileDataId);
        UpdatePlacement(updated);
        var preparation = PrepareTerrainScene(scene, viewer);
        Assert.IsTrue(preparation.Views.HasExteriorView);
        Assert.IsTrue(preparation.TerrainOcclusion.Buffer.ContainsBox(
            new(new(119f, -1f, 0f), new(121f, 1f, 10f))));
        Assert.IsFalse(Groups(ordinary)[0]);
        Assert.AreEqual(updatedVisible, Groups(updated)[0]);
    }

    [TestMethod]
    public void TerrainFeedDropsUnloadedAndMissingMetadataAndOldProfileState()
    {
        using var scene = new CpuScene();
        var adt = AddTerrain(scene, 50f, 60f, 20f);
        var behind = scene.Add(HorizonModel(110f, 8));
        var preparation = PrepareTerrainScene(scene);
        Assert.IsFalse(Groups(behind)[0]);
        var terrain = adt.Terrain;
        adt.UpdateTerrain(terrain with { chunkHoleMasks = null! });
        PrepareTerrainScene(scene, preparation: preparation);
        Assert.IsTrue(Groups(behind)[0]);
        adt.UpdateTerrain(terrain);
        PrepareTerrainScene(scene, preparation: preparation);
        Assert.IsFalse(Groups(behind)[0]);
        adt.Unload();
        PrepareTerrainScene(scene, preparation: preparation);
        Assert.IsTrue(Groups(behind)[0]);
        adt.OnLoaded(terrain);
        PrepareTerrainScene(scene, preparation: preparation);
        PrepareTerrainScene(scene, new(default, default, false), preparation);
        Assert.IsFalse(preparation.TerrainOcclusion.Buffer.Active);
        PrepareTerrainScene(scene, preparation: preparation);
        preparation.Prepare(new(default, default, true), false, Vector3.Zero, Vector3.UnitX,
            HorizonCamera, scene.Objects, cameraProjection: HorizonProjection);
        Assert.IsFalse(preparation.TerrainOcclusion.Buffer.Active);
    }

    [TestMethod]
    public void TerrainOutsideTheSceneViewDoesNotOccludeVisibleWmos()
    {
        using var scene = new CpuScene();
        AddTerrain(scene, 50f, 60f, 100f);
        var behind = scene.Add(HorizonModel(110f, 8));
        PrepareTerrainScene(scene);
        Assert.IsTrue(Groups(behind)[0]);
    }

    private static readonly Matrix4x4 HorizonProjection =
        Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(MathF.PI / 2f, 1f, 1f, 1000f);
    private static readonly Matrix4x4 HorizonCamera = new Camera(Vector3.Zero, 0f, 0f, 1f).GetViewMatrix() *
        HorizonProjection;

    private static WmoScenePortalPreparation PrepareTerrainScene(CpuScene scene,
        WmoSceneViewerResult? viewer = null, WmoScenePortalPreparation? preparation = null, float farClip = 1000f)
    {
        preparation ??= new();
        preparation.Prepare(viewer ?? new(default, default, true), true, Vector3.Zero, Vector3.UnitX,
            HorizonCamera, scene.Objects, cameraProjection: HorizonProjection, farClip: farClip);
        return preparation;
    }

    private static WorldModel HorizonModel(float x, uint flags) => Model(Floor(0f, flags) with
    { mogiBoundingBox = new(new(x - 1f, -1f, 0f), new(x + 1f, 1f, 10f)) }) with { portalGraphValid = true };

    private static ADTContainer AddTerrain(CpuScene scene, float minX, float maxX, float height, ushort holes = 0)
    {
        var adt = new ADTContainer(default, default) { ModelMatrix = Matrix4x4.Identity, Scale = 1f };
        adt.OnLoaded(new() { usesLegacyLighting = true,
            vertices = Wrath335TerrainClipBufferTests.TerrainVertices(minX, maxX, height),
            chunkBounds = [new(new(minX, -50f, height), new(maxX, 50f, height))], chunkHoleMasks = [holes] });
        scene.Objects.Add(adt);
        return adt;
    }

    private static Matrix4x4 ClipPlacement(Vector3 center, Vector3 forward)
    {
        var z = -forward;
        var x = Vector3.Cross(Vector3.UnitZ, z);
        return new(x.X, x.Y, x.Z, 0f, 0f, 0f, 1f, 0f, z.X, z.Y, z.Z, 0f,
            center.X, center.Y, center.Z, 1f);
    }

    private static WorldModelGroupBatches ClipGroup(uint flags, float minZ, float maxZ, float radius = 1f) =>
        Floor(0f, flags) with
        { mogiBoundingBox = new(new(-radius, -radius, minZ), new(radius, radius, maxZ)) };

    private static WmoPortal ClipPortal(float z, float radius) => new()
    {
        Normal = Vector3.UnitZ, Distance = -z,
        Vertices = [new(-radius, -radius, z), new(radius, -radius, z),
            new(radius, radius, z), new(-radius, radius, z)]
    };

    private static readonly Matrix4x4 QueueCamera = Matrix4x4.CreateScale(1f / 4096f, 1f, 1f);

    private static void UpdatePlacement(WMOContainer placement)
    {
        placement.InvalidateTransform();
        placement.ModelMatrix = Matrix4x4.Identity;
        Assert.AreEqual(0x400u, placement.ViewerRuntimeFlags);
    }

    private static WmoScenePortalPreparation PrepareScene(CpuScene scene, WmoSceneViewerResult? viewer = null,
        WmoScenePortalPreparation? preparation = null)
    {
        preparation ??= new();
        preparation.Prepare(viewer ?? new(default, default, true), true, Eye, Vector3.UnitX,
            QueueCamera, scene.Objects);
        return preparation;
    }

    private static bool[] Groups(WMOContainer placement)
    {
        placement.GetPortalVisibilityBuffers(placement.GetWMO(), out var groups, out _, out _, out _);
        return groups;
    }

    private static void AssertPolygonOrder(WmoScenePortalPreparation preparation, params float[] expected) =>
        CollectionAssert.AreEqual(expected, preparation.Views.RenderViews.Views.ToArray()
            .Select(v => v.Rect.MinY).ToArray());

    private static WorldModel QueueModel(params WorldModelGroupBatches[] groups) =>
        Model(groups) with { portalGraphValid = true, portals = [QueuePortal(0.2f)] };

    private static WorldModelGroupBatches QueueGroup(float depth, uint flags,
        WmoPortalLink[]? links = null, float width = 1f) => Floor(0f, flags) with
        {
            mogiBoundingBox = new(new(depth, -0.2f, 0.1f), new(depth + width, 0.2f, 0.8f)),
            portalLinks = links ?? []
        };

    private static WmoPortalLink[] QueueLink(ushort portal, ushort group) =>
        [new() { PortalIndex = portal, TargetGroupIndex = group, Side = 1 }];

    private static WmoPortal QueuePortal(float radius) => new()
    {
        Normal = Vector3.UnitZ, Distance = -0.5f,
        Vertices = [new(-2048f, -radius, 0.5f), new(2048f, -radius, 0.5f),
            new(2048f, radius, 0.5f), new(-2048f, radius, 0.5f)]
    };

    private static WorldModel Model(params WorldModelGroupBatches[] groups) => new()
    {
        legacyLighting = true, wrath335 = true, boundingBox = Bounds,
        groupBatches = groups, portals = [], doodads = [], doodadSets = [], wmoRenderBatches = []
    };

    private static WorldModelGroupBatches Floor(float height, uint flags = 0) => new()
    {
        flags = flags, mogiFlags = flags, boundingBox = Bounds, mogiBoundingBox = Bounds,
        raycastVertices = [new(-5f, -5f, height), new(5f, -5f, height), new(0f, 5f, height)],
        raycastIndices = [0, 1, 2], portalLinks = [], doodadReferences = [],
        viewerBsp = new([new(4, ushort.MaxValue, ushort.MaxValue, 1, 0, 0f)],
            [0], [0, 1, 2], [0])
    };

    private static WorldModel WithPortal(float height)
    {
        var owner = Floor(-1800f) with
        {
            portalLinks = [new() { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }]
        };
        var model = Model(owner, Floor(-1800f));
        model.portals = [new()
        {
            Normal = Vector3.UnitZ, Distance = -height,
            Vertices = [new(-1f, -1f, height), new(1f, -1f, height),
                new(1f, 1f, height), new(-1f, 1f, height)]
        }];
        return model;
    }

    /// <summary>Seed decoded CPU models without constructing a device or loading assets.</summary>
    private sealed class CpuScene : IDisposable
    {
        private static readonly FieldInfo DeviceField = typeof(WMOCache).GetField(
            "cachedDevice", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object? _previousDevice = DeviceField.GetValue(null);
        private readonly Dictionary<uint, WorldModel> _cache = (Dictionary<uint, WorldModel>)
            typeof(WMOCache).GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        private readonly List<uint> _keys = [];
        private static readonly FieldInfo M2DeviceField = typeof(M2Cache).GetField(
            "cachedDevice", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object? _previousM2Device = M2DeviceField.GetValue(null);
        private readonly Dictionary<uint, ParsedDoodadBatch> _m2Cache = (Dictionary<uint, ParsedDoodadBatch>)
            typeof(M2Cache).GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        private readonly List<uint> _m2Keys = [];
        private readonly List<WMOContainer> _placements = [];
        private readonly WmoSceneViewerQuery _query;
        public readonly List<Container3D> Objects = [];
        public readonly Dictionary<(uint, string), List<WMOContainer>> Buckets = [];
        public float? TerrainLimit;
        public int TerrainQueries;

        public CpuScene() => _query = new(_ => { TerrainQueries++; return 1760f; });

        public WMOContainer Add(WorldModel model)
        {
            var key = uint.MaxValue - (uint)_keys.Count;
            while (_cache.ContainsKey(key))
                key--;
            _keys.Add(key);
            model.rootWMOFileDataID = key;
            _cache.Add(key, model);
            return Add(key);
        }

        public WMOContainer Add(uint key)
        {
            var placement = new WMOContainer(default, key, 0)
            {
                Scale = 1f, ModelMatrix = Matrix4x4.Identity
            };
            placement.InitializeFileViewerPlacement();
            _placements.Add(placement);
            Objects.Add(placement);
            if (!Buckets.TryGetValue((key, ""), out var bucket))
                Buckets[(key, "")] = bucket = [];
            bucket.Add(placement);
            return placement;
        }

        public WmoSceneViewerResult Locate() => _query.Locate(Objects, Buckets, Eye, ref TerrainLimit);

        public void AddDoodad(WMOContainer placement, int index, WoWRenderLib.Raycasting.BoundingSphere sphere,
            BoundingBox? bounds = null)
        {
            var key = uint.MaxValue - (uint)_m2Keys.Count;
            while (_m2Cache.ContainsKey(key)) key--;
            _m2Keys.Add(key);
            _m2Cache.Add(key, new() { fileDataID = key, mats = [] });
            var doodad = new M2Container(default, key, 0)
            {
                ParentWMO = placement, WmoDoodadIndex = index, CachedBoundingSphere = sphere, CachedBoundingBox = bounds
            };
            placement.ActiveDoodads.Add(doodad);
            Objects.Add(doodad);
        }

        public void Dispose()
        {
            foreach (var placement in _placements)
                WMOCache.Release(placement.FileDataId, 0);
            foreach (var key in _keys)
                _cache.Remove(key);
            foreach (var key in _m2Keys)
            {
                M2Cache.Release(key, 0);
                _m2Cache.Remove(key);
            }
            DeviceField.SetValue(null, _previousDevice);
            M2DeviceField.SetValue(null, _previousM2Device);
        }
    }
}
