using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335M2QueuePreparationTests
{
    [TestMethod]
    public void ParticleKeyTransformsEmitterOriginAndKeepsUnsignedSquaredSecondary()
    {
        var boneToView = Matrix4x4.CreateTranslation(2, 0, -8);
        var parent = Wrath335M2ElementOrdering.ParticleDistance(new(1, 4, -4), boneToView, 7);
        Assert.AreEqual(new Wrath335M2DistanceKeys(7, 169), parent);
        Assert.AreEqual(parent, Wrath335M2ElementOrdering.ParticleDistance(new(3, 4, 12), 7));
        Assert.AreEqual(new Wrath335M2DistanceKeys(7, 7), Wrath335M2ElementOrdering.RibbonDistance(7));
    }

    [TestMethod]
    public void AdditiveBlocksRegroupParticlesButCannotCrossNonAdditiveSeparators()
    {
        Wrath335M2ElementSortData[] elements = [Mesh(9, 3), Particle(8, 10), Particle(7, 3),
            Mesh(6, 2), Mesh(5, 4), Particle(4, 3)];
        int[] order = [5, 4, 3, 2, 1, 0];
        Wrath335M2ElementOrdering.SortTransparentQueue(order, elements, false, true, 3, false);
        CollectionAssert.AreEqual(new[] { 2, 1, 0, 3, 5, 4 }, order);
        Assert.AreEqual(elements[0].AdditiveGroup, elements[2].AdditiveGroup);
        Assert.IsTrue(elements[3].AdditiveGroup > elements[0].AdditiveGroup);
        Assert.IsTrue(elements[4].AdditiveGroup > elements[3].AdditiveGroup);
    }

    [TestMethod]
    [DataRow(false, 2)]
    [DataRow(true, 0)]
    [DataRow(true, 1)]
    public void RegroupRequiresCacheFlagAndMoreThanOneAdmittedAdditiveParticle(bool enabled, int count)
    {
        Wrath335M2ElementSortData[] elements = [Mesh(9, 3), Particle(8, 3), Particle(7, 10)];
        int[] order = [2, 1, 0];
        Wrath335M2ElementOrdering.SortTransparentQueue(order, elements, false, enabled, (uint)count, true);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, order);
    }

    [TestMethod]
    public void ForcedParticleClassificationChangesBlocksWithoutChangingTheCountGate()
    {
        var queues = new Wrath335M2ElementQueues();
        queues.Add(Mesh(9, 3), Wrath335M2QueueMask.AboveWater);
        queues.Add(Particle(8, 2), Wrath335M2QueueMask.AboveWater);
        queues.Add(Particle(7, 3), Wrath335M2QueueMask.AboveWater);
        queues.Add(Particle(6, 10), Wrath335M2QueueMask.BelowWater);
        queues.Sort(0x80);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, queues.AboveWater.ToArray());
        queues.Sort(0x180);
        CollectionAssert.AreEqual(new[] { 1, 2, 0 }, queues.AboveWater.ToArray());
        Assert.AreEqual(2u, queues.AdditiveParticleCount);
        queues.BeginFrame();
        queues.Add(Mesh(9, 3), Wrath335M2QueueMask.AboveWater);
        queues.Add(Particle(8, 2), Wrath335M2QueueMask.AboveWater);
        queues.Sort(0x180);
        CollectionAssert.AreEqual(new[] { 0, 1 }, queues.AboveWater.ToArray());
        Assert.AreEqual(0u, queues.AdditiveParticleCount);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, true)]
    [DataRow(4, true)]
    [DataRow(5, false)]
    [DataRow(6, false)]
    public void AuthoredMeshAndRibbonAdditiveModesUseTheTransparentBlendRow(int blend, bool expected)
    {
        Assert.AreEqual(expected, Wrath335M2ElementOrdering.IsAdditiveForRegrouping(Mesh(0, (ushort)blend), false));
        Assert.AreEqual(expected, Wrath335M2ElementOrdering.IsAdditiveForRegrouping(
            Mesh(0, (ushort)blend) with { Kind = Wrath335M2ElementKind.Ribbon }, false));
        Assert.AreEqual(expected, Wrath335M2ElementOrdering.IsAdditiveForRegrouping(
            Mesh(0, (ushort)blend) with { Kind = Wrath335M2ElementKind.ProjectedMesh }, false));
    }

    [TestMethod]
    public void AdditiveParticleComparatorUsesResolvedMaterialAndWrappedTextureOrder()
    {
        var a = Particle(1, 3) with { PriorityPlane = 100, ModelIdentity = uint.MaxValue };
        var b = Particle(100, 10) with { PriorityPlane = -100 };
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareAdditive(a, b, true) < 0);
        b = b with { ParticleBlendMode = 3, ParticleFlags = 0 };
        a = a with { ParticleFlags = 7 }; // reconstructed flags 4 precede 23.
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareAdditive(a, b, false) < 0);
        a = a with { ParticleFlags = 0, ParticleTextureIdentity = 0xFFFFFFFC };
        b = b with { ParticleTextureIdentity = 4 };
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareAdditive(a, b, false) < 0);
        a = a with { ParticleTextureIdentity = 4 };
        Assert.AreEqual(0, Wrath335M2ElementOrdering.CompareAdditive(a, b, true));
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareAdditive(a with { AdditiveGroup = 2 },
            b with { AdditiveGroup = 1 }, false) > 0);
    }

    [TestMethod]
    public void NonParticleAdditiveEntriesKeepTheBaseTransparentComparator()
    {
        var mesh = Mesh(9, 3);
        var ribbon = Mesh(10, 4) with { Kind = Wrath335M2ElementKind.Ribbon };
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareAdditive(ribbon, mesh, false) < 0);
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareAdditive(Particle(1, 10), ribbon, false) < 0);
        Wrath335M2ElementSortData[] elements = [Particle(7, 3), Particle(7, 3), Particle(7, 3)];
        int[] order = [0, 1, 2];
        Wrath335M2ElementOrdering.SortTransparentQueue(order, elements, false, true, 3, false);
        // Both native heaps move equal elements; no insertion-order tie is added.
        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, order);
    }

    [TestMethod]
    public void GlobalParticleCountRegroupsBothWaterQueuesEvenWithOneParticleInEach()
    {
        var queues = new Wrath335M2ElementQueues();
        var both = Wrath335M2QueueMask.AboveWater | Wrath335M2QueueMask.BelowWater;
        queues.Add(Mesh(9, 3), both);
        queues.Add(Particle(8, 3), Wrath335M2QueueMask.AboveWater);
        queues.Add(Mesh(7, 2), both);
        queues.Add(Particle(6, 10), Wrath335M2QueueMask.BelowWater);
        queues.Add(Mesh(5, 4), both);
        queues.Add(Mesh(1, 0), Wrath335M2QueueMask.Opaque);
        queues.Add(Particle(100, 3), Wrath335M2QueueMask.None);
        queues.Sort(0x80);
        Assert.AreEqual(2u, queues.AdditiveParticleCount);
        CollectionAssert.AreEqual(new[] { 1, 0, 2, 4 }, queues.AboveWater.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 2, 3, 4 }, queues.BelowWater.ToArray());
        CollectionAssert.AreEqual(new[] { 5 }, queues.Opaque.ToArray());
        Assert.AreEqual(6, queues.Elements.Count);
        queues.BeginFrame();
        Assert.AreEqual(0, queues.Elements.Count);
        Assert.AreEqual(0, queues.AboveWater.Count);
        Assert.AreEqual(0, queues.BelowWater.Count);
        Assert.AreEqual(0, queues.Opaque.Count);
        queues.Add(Mesh(3, 2), Wrath335M2QueueMask.BelowWater);
        queues.Sort(0x80);
        CollectionAssert.AreEqual(new[] { 0 }, queues.BelowWater.ToArray());
        Assert.AreEqual(0u, queues.AdditiveParticleCount);
    }

    [TestMethod]
    [DataRow(-2f, false, true)]
    [DataRow(-1f, true, true)]
    [DataRow(0f, true, true)]
    [DataRow(1f, true, true)]
    [DataRow(2f, true, false)]
    public void SphereSelectionIncludesBothExactWaterBoundaries(float z, bool above, bool below)
    {
        Assert.AreEqual(new Wrath335M2WaterSelection(above, below), Select(0x60, z, true, false));
        Assert.AreEqual(above, Wrath335M2WaterQueues.SelectParticleAbove(0x60, Vector3.Zero,
            Vector3.Zero, 1, Matrix4x4.CreateTranslation(0, 0, z), new(0, 0, 1, 0)));
    }

    [TestMethod]
    [DataRow(0, false, false)]
    [DataRow(0x20, true, false)]
    [DataRow(0x40, false, true)]
    public void SingleOrAbsentLightingFlagsDoNotReadThePlane(int flags, bool above, bool below)
    {
        var plane = new Vector4(float.NaN);
        Assert.AreEqual(new Wrath335M2WaterSelection(above, below),
            Wrath335M2WaterQueues.SelectModel((uint)flags, Vector3.Zero, Vector3.Zero,
                1, Matrix4x4.Identity, plane, false, true));
        Assert.AreEqual(above, Wrath335M2WaterQueues.SelectParticleAbove((uint)flags,
            Vector3.Zero, Vector3.Zero, 1, Matrix4x4.Identity, plane));
        Assert.AreEqual(new Wrath335M2WaterSelection(false, false),
            Wrath335M2WaterQueues.SelectModel(0x60, Vector3.Zero, Vector3.Zero,
                1, Matrix4x4.Identity, plane, true, false));
    }

    [TestMethod]
    public void WaterSphereUsesAuthoredMidpointAndFirstAxisScaleWithoutRenormalizingThePlane()
    {
        var matrix = Matrix4x4.CreateScale(2, 100, 30) * Matrix4x4.CreateTranslation(0, 0, -57);
        var selection = Wrath335M2WaterQueues.SelectModel(0x60, new(0, 0, 1), new(0, 0, 3),
            1, matrix, new(0, 0, 1, 0), true, false);
        Assert.AreEqual(new Wrath335M2WaterSelection(true, false), selection); // center z=3, radius=2.
        Assert.AreEqual(new Wrath335M2WaterSelection(false, true),
            Wrath335M2WaterQueues.SelectModel(0x60, Vector3.Zero, Vector3.Zero,
                1, Matrix4x4.CreateTranslation(0, 0, -0.75f), new(0, 0, 2, 0), true, false));
    }

    [TestMethod]
    public void CrossingCollapseUsesSceneSideAndDoesNotChangeParticleAboveSelection()
    {
        Assert.AreEqual(new Wrath335M2WaterSelection(true, false), Select(0x60, 0, false, false));
        Assert.AreEqual(new Wrath335M2WaterSelection(false, true), Select(0x60, 0, false, true));
        Assert.AreEqual(new Wrath335M2WaterSelection(true, true), Select(0x60, 0, true, true));
        Assert.IsTrue(Wrath335M2WaterQueues.SelectParticleAbove(0x60, Vector3.Zero,
            Vector3.Zero, 1, Matrix4x4.Identity, new(0, 0, 1, 0)));
    }

    [TestMethod]
    public void CameraPlaneTransformsItsPointAndNormalWithTheNativeNormalizationGate()
    {
        var view = Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(3, 4, 5);
        var plane = Wrath335M2WaterQueues.PlaneToView(new(0, 0, 1, -2), view);
        var point = Vector3.Transform(new(0, 0, 2), view);
        Assert.AreEqual(1f, new Vector3(plane.X, plane.Y, plane.Z).Length(), 0.00001f);
        Assert.AreEqual(0f, Vector4.Dot(new(point, 1), plane), 0.00001f);
        var tiny = 1f / 2048;
        Assert.AreEqual(new Vector4(tiny, 0, 0, 0),
            Wrath335M2WaterQueues.PlaneToView(new(tiny, 0, 0, 0), Matrix4x4.Identity));
        Assert.AreEqual(new Vector4(1, 0, 0, 0),
            Wrath335M2WaterQueues.PlaneToView(new(tiny * 2, 0, 0, 0), Matrix4x4.Identity));
    }

    [TestMethod]
    [DataRow(false, false, 0)]
    [DataRow(true, false, 2)]
    [DataRow(false, true, 4)]
    [DataRow(true, true, 6)]
    public void MeshAndProjectedRoutingDifferForCrossingOrUnselectedModels(bool above, bool below, int mask)
    {
        var selection = new Wrath335M2WaterSelection(above, below);
        Assert.AreEqual(new Wrath335M2MeshQueueRoute((Wrath335M2QueueMask)mask, above && below),
            Wrath335M2WaterQueues.RouteMesh(true, false, selection));
        Assert.AreEqual(new Wrath335M2MeshQueueRoute(below ? Wrath335M2QueueMask.BelowWater :
            Wrath335M2QueueMask.AboveWater, false), Wrath335M2WaterQueues.RouteMesh(true, true, selection));
        Assert.AreEqual(new Wrath335M2MeshQueueRoute(Wrath335M2QueueMask.Opaque, false),
            Wrath335M2WaterQueues.RouteMesh(false, false, selection));
    }

    [TestMethod]
    public void RibbonsAndCallbacksChooseOneSideAndOpaqueClassificationWins()
    {
        var threshold = Wrath335M2FadeMaterial.OpaqueThreshold;
        Assert.AreEqual(Wrath335M2QueueMask.Opaque, Wrath335M2WaterQueues.RouteRibbon(1, threshold, false));
        Assert.AreEqual(Wrath335M2QueueMask.AboveWater,
            Wrath335M2WaterQueues.RouteRibbon(1, MathF.BitDecrement(threshold), true));
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, Wrath335M2WaterQueues.RouteRibbon(2, 1, false));
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, Wrath335M2WaterQueues.RouteRibbon(0, float.NaN, false));
        Assert.AreEqual(Wrath335M2QueueMask.Opaque, Wrath335M2WaterQueues.RouteCallback(true, false));
        Assert.AreEqual(Wrath335M2QueueMask.AboveWater, Wrath335M2WaterQueues.RouteCallback(false, true));
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, Wrath335M2WaterQueues.RouteCallback(false, false));
    }

    [TestMethod]
    public void ParticleRoutingUsesResolvedBlendAndExplicitRuntimeForceBelowAfterOpaqueCheck()
    {
        var threshold = Wrath335M2FadeMaterial.OpaqueThreshold;
        Assert.AreEqual(Wrath335M2QueueMask.Opaque, Wrath335M2WaterQueues.RouteParticle(-1, threshold, true, 0x40000));
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater,
            Wrath335M2WaterQueues.RouteParticle(1, MathF.BitDecrement(threshold), true, 0x40000));
        Assert.AreEqual(Wrath335M2QueueMask.AboveWater, Wrath335M2WaterQueues.RouteParticle(3, 1, true, 0));
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, Wrath335M2WaterQueues.RouteParticle(3, 1, false, 0));
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, Wrath335M2WaterQueues.RouteParticle(3, 1, true, 0x40000));
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, Wrath335M2WaterQueues.RouteParticle(0, float.NaN, false, 0));
    }

    [TestMethod]
    [DataRow(0L, 4, 2)]
    [DataRow(1L, 2, 4)]
    [DataRow(4294967295L, 2, 4)]
    public void LiquidIdSelectsTransparentQueueOrder(long liquidId, int before, int after)
    {
        Assert.AreEqual(new Wrath335M2WaterPassOrder((Wrath335M2QueueMask)before,
            (Wrath335M2QueueMask)after), Wrath335M2WaterQueues.PassOrder((uint)liquidId));
    }

    private static Wrath335M2WaterSelection Select(uint flags, float z, bool split, bool sceneBelow) =>
        Wrath335M2WaterQueues.SelectModel(flags, Vector3.Zero, Vector3.Zero, 1,
            Matrix4x4.CreateTranslation(0, 0, z), new(0, 0, 1, 0), split, sceneBelow);
    private static Wrath335M2ElementSortData Mesh(float distance, ushort blend) =>
        new() { Kind = Wrath335M2ElementKind.Mesh, Distance = new(distance, distance), BlendMode = blend };
    private static Wrath335M2ElementSortData Particle(float distance, int blend) =>
        new() { Kind = Wrath335M2ElementKind.ParticleRun, Distance = new(distance, distance), ParticleBlendMode = blend };
}
