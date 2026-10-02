using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWLib.Formats.Common;
using WoWLib.Formats.M2.Root;
using WoWLib.Formats.M2.Root.Record;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;
using SharedLoader = WoWRenderLib.Loaders.M2Loader;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335M2NativePreparationTests
{
    [TestMethod]
    [DataRow(0, true)]
    [DataRow(7, true)]
    [DataRow(0x100, true)]
    [DataRow(8, false)]
    [DataRow(0x80, false)]
    [DataRow(0x200, false)]
    public void NativeSimpleAnimationGateUsesItsExactBoneFlagMasks(int flags, bool expected)
    {
        using var root = Root(flags);
        Assert.AreEqual(expected, Animation(root).Wrath335UsesSimpleAnimation);
    }

    [TestMethod]
    [DataRow("bone")]
    [DataRow("sequence")]
    [DataRow("color")]
    [DataRow("light")]
    [DataRow("camera")]
    [DataRow("ribbon")]
    [DataRow("particle")]
    public void NativeSimpleAnimationRequiresOneBoneAndSequenceAndNoExcludedSources(string source)
    {
        using var root = Root();
        switch (source)
        {
            case "bone": using (var record = new M2CompBoneWotlkPlus()) root.Bones.Add(record); break;
            case "sequence": using (var record = new M2SequenceWotlkToMop()) root.Sequences.Add(record); break;
            case "color": using (var record = new M2ColorWotlkPlus()) root.Colors.Add(record); break;
            case "light": using (var record = new M2LightWotlkPlus()) root.Lights.Add(record); break;
            case "camera": using (var record = new M2CameraWotlk()) root.Cameras.Add(record); break;
            case "ribbon": using (var record = new M2RibbonWotlkPlus()) root.RibbonEmitters.Add(record); break;
            case "particle": using (var record = new M2ParticleWotlk()) root.ParticleEmitters.Add(record); break;
        }
        Assert.IsFalse(Animation(root).Wrath335UsesSimpleAnimation);
    }

    [TestMethod]
    public void AuthoredBoundsSurviveNativeDisposalAndRemainSeparateFromExpandedRenderBounds()
    {
        Wrath335M2Bounds? bounds;
        using (var root = Root())
        {
            using var min = new C3Vector(1, 2, 3);
            using var max = new C3Vector(5, 6, 7);
            root.BoundingBox.Min = min; root.BoundingBox.Max = max;
            root.BoundingSphereRadius = 2.5f;
            bounds = Bounds(root, true);
            Assert.IsNull(Bounds(root, false));
            root.BoundingSphereRadius = 100;
            root.BoundingBox.Min.X = -100;
        }
        using var other = new M2RootTbc();
        Assert.IsNull(Bounds(other, true));
        Assert.AreEqual(new Wrath335M2Bounds(new(1, 2, 3), new(5, 6, 7), 2.5f), bounds);
        var parsed = new ParsedM2 { wrath335Bounds = bounds, boundingRadius = 100,
            boundingBox = new(new(-100), new(100)), vertexBytes = [], indiceBytes = [], mats = [] };
        var loaded = WoWRenderLib.DX11.Loaders.M2Loader.LoadM2(default, parsed);
        Assert.AreEqual(bounds, loaded.wrath335Bounds);
        Assert.AreEqual(100f, loaded.boundingRadius);
    }

    [TestMethod]
    [DataRow(0, 1L)]
    [DataRow(1, 11L)]
    [DataRow(2, 21L)]
    [DataRow(4, 21L)]
    public void VertexSelectorUsesClampedBoneInfluencesRatherThanTextureOrBoneCount(int influences, long vertex)
    {
        Assert.AreEqual(new Wrath335M2ShaderSelection((uint)vertex, 0, 0),
            Wrath335M2ShaderSelectors.Select(2, 0, (ushort)influences, 1, false, new(0, 0, 0, true, 0)));
    }

    [TestMethod]
    [DataRow(0L, 21L, 4L)]
    [DataRow(1L, 51L, 13L)]
    [DataRow(2L, 81L, 14L)]
    [DataRow(3L, 81L, 15L)]
    public void ShadowSelectorClampsOnlyTheVertexFamilyAndRetainsPixelShadowMode(long shadow, long vertex, long pixel)
    {
        var environment = new Wrath335M2ShaderEnvironment(0x10, 0, (uint)shadow, true, 1);
        Assert.AreEqual(new Wrath335M2ShaderSelection((uint)vertex, (uint)pixel, (uint)shadow),
            Wrath335M2ShaderSelectors.Select(2, 0, 2, 1, false, environment));
    }

    [TestMethod]
    public void UnlitMaterialShadowDisableLightingOverridesAndProjectedGatesRemainIndependent()
    {
        var environment = new Wrath335M2ShaderEnvironment(0x10, 2, 3, true, 1);
        Assert.AreEqual(new Wrath335M2ShaderSelection(0, 4, 0),
            Wrath335M2ShaderSelectors.Select(2, 1, 0, 1, false, environment));
        Assert.AreEqual(new Wrath335M2ShaderSelection(64, 15, 3),
            Wrath335M2ShaderSelectors.Select(5, 0, 0, 1, false, environment));
        Assert.AreEqual(new Wrath335M2ShaderSelection(5, 4, 0),
            Wrath335M2ShaderSelectors.Select(2, 0x100, 0, 1, false, environment));
        Assert.AreEqual(new Wrath335M2ShaderSelection(5, 4, 0),
            Wrath335M2ShaderSelectors.Select(2, 0, 0, 1, true, environment));
        Assert.AreEqual(new Wrath335M2ShaderSelection(35, 13, 1),
            Wrath335M2ShaderSelectors.Select(2, 0, 0, 1, false, environment with { LightingFlags = 0x18 }));
    }

    [TestMethod]
    public void AlphaSelectorUsesTruncatedComposedReferenceAndCapabilityShadowGates()
    {
        var environment = new Wrath335M2ShaderEnvironment(0, 0, 0, false, 0);
        Assert.AreEqual(0u, Wrath335M2ShaderSelectors.Select(1, 0, 0, 0.004f, false, environment).PixelIndex);
        Assert.AreEqual(8u, Wrath335M2ShaderSelectors.Select(1, 0, 0, 0.005f, false, environment).PixelIndex);
        Assert.AreEqual(0u, Wrath335M2ShaderSelectors.Select(0, 0, 0, 1, false, environment).PixelIndex);
        Assert.AreEqual(8u, Wrath335M2ShaderSelectors.Select(2, 0, 0, 0, false, environment).PixelIndex);
        Assert.AreEqual(0u, Wrath335M2ShaderSelectors.Select(1, 0, 0, 1, false,
            environment with { AlphaTestCapability = true }).PixelIndex);
        Assert.AreEqual(0u, Wrath335M2ShaderSelectors.Select(1, 0, 0, float.NaN, false, environment).PixelIndex);
        Assert.AreEqual(8u, Wrath335M2ShaderSelectors.Select(1, 0, 0, -1, false, environment).PixelIndex);
    }

    [TestMethod]
    public void ShaderSortingReadsResolvedEntriesAndAcceptsZeroEntriesInAPresentEffect()
    {
        var tables = Tables();
        Assert.IsTrue(tables.TryResolve(new(1, 0, 0), out var first));
        Assert.IsTrue(tables.TryResolve(new(11, 0, 0), out var second));
        Assert.AreEqual(first, second); // Different selectors can share one resolved resource.
        Assert.AreEqual(new Wrath335M2ShaderSortKey(1000, 2000), first);
        Assert.IsTrue(new Wrath335M2EffectSortTables(new uint[90], new uint[16]).TryResolve(new(89, 15, 3), out var zero));
        Assert.AreEqual(default(Wrath335M2ShaderSortKey), zero);
        Assert.IsFalse(tables.TryResolve(new(90, 0, 0), out _));
        Assert.IsFalse(tables.TryResolve(new(1, 16, 4), out _));
    }

    [TestMethod]
    public void StaticRootOnlyMeshPreparationPartitionsComposedAlphaAndCrossingClipState()
    {
        var (model, packet) = Model();
        var queues = new Wrath335M2ElementQueues();
        Assert.IsTrue(Add(queues, model, packet, 1));
        Assert.AreEqual(1, queues.Opaque.Count); // Layer's alpha material uses opaque base.
        Assert.AreEqual(new Wrath335M2DistanceKeys(0, 0), queues.Elements[0].Distance);
        queues.BeginFrame();
        Assert.IsTrue(Add(queues, model, packet, 0.5f));
        Assert.AreEqual(1, queues.AboveWater.Count);
        Assert.AreEqual(1, queues.BelowWater.Count);
        Assert.AreEqual(new Wrath335M2DistanceKeys(25, 25), queues.Elements[0].Distance);
        Assert.AreEqual(new Wrath335M2ShaderSortKey(1000, 2000), queues.Elements[0].Shader);
        Assert.AreEqual(2u, queues.Elements[0].Flags & 2);
        Assert.AreEqual((ushort)2, queues.Elements[0].BlendMode);
        queues.BeginFrame();
        Assert.IsTrue(Add(queues, model, packet, 0.5f, cacheFlags: 0, elementFlags: 7));
        Assert.AreEqual(1, queues.AboveWater.Count);
        Assert.AreEqual(0, queues.BelowWater.Count);
        Assert.AreEqual(0u, queues.Elements[0].Flags & 2);
        Assert.AreEqual(5u, queues.Elements[0].Flags); // Retain clone/opt flags, recompute stale water clip.
    }

    [TestMethod]
    public void MeshPreparationRejectsSentinelMissingResourcesAndMissingComplexPosesWithoutAppending()
    {
        var (model, packet) = Model();
        var queues = new Wrath335M2ElementQueues();
        model.submeshes = [model.submeshes[0] with { wrath335Sort = model.submeshes[0].wrath335Sort!.Value with { ShaderId = 0x8000 } }];
        Assert.IsFalse(Add(queues, model, packet, 0.5f));
        model.submeshes = [model.submeshes[0] with { wrath335Sort = model.submeshes[0].wrath335Sort!.Value with { ShaderId = 0x8001 } }];
        Assert.IsFalse(Add(queues, model, packet, 0.5f, effect: null));
        Assert.IsFalse(Add(queues, model, packet, 0.5f, effect: default(Wrath335M2EffectSortTables)));
        Assert.IsFalse(Add(queues, model, packet, 0.00001f));
        var simple = model.animation;
        model.animation = new() { Bones = simple!.Bones, Sequences = simple.Sequences, GlobalLoops = [] };
        Assert.IsFalse(Add(queues, model, packet, 0.5f));
        Assert.AreEqual(0, queues.Elements.Count);
        model.animation = simple;
        Assert.IsTrue(Add(queues, model, packet, 0.5f)); // 0x8001 passes the exact sentinel gate.
    }

    [TestMethod]
    public void StaticFallbackNeverInventsHigherBoneMatricesAndRecomputesPlacementWaterKeys()
    {
        var (model, packet) = Model();
        var queues = new Wrath335M2ElementQueues();
        var batch = model.submeshes[0];
        model.submeshes = [batch with { wrath335Sort = batch.wrath335Sort!.Value with { CenterBoneIndex = 1 } }];
        Assert.IsFalse(Add(queues, model, packet, 0.5f));
        model.submeshes = [batch];
        packet.WorldMatrices[0] = Matrix4x4.CreateTranslation(0, 0, 3);
        Assert.IsTrue(Add(queues, model, packet, 0.5f));
        Assert.AreEqual(1, queues.AboveWater.Count);
        Assert.AreEqual(0, queues.BelowWater.Count); // Expanded render radius 100 is not used for water.
        Assert.AreEqual(new Wrath335M2DistanceKeys(64, 64), queues.Elements[0].Distance);
        queues.BeginFrame();
        packet.WorldMatrices[0] = Matrix4x4.CreateTranslation(0, 0, -10);
        Assert.IsTrue(Add(queues, model, packet, 0.5f));
        Assert.AreEqual(0, queues.AboveWater.Count);
        Assert.AreEqual(1, queues.BelowWater.Count);
        Assert.AreEqual(new Wrath335M2DistanceKeys(-25, -25), queues.Elements[0].Distance);
    }

    [TestMethod]
    [DataRow(0, 0x20)]
    [DataRow(0x40, 0x20)]
    [DataRow(0x20, 0x40)]
    [DataRow(0x60, 0x60)]
    public void PostQueryEntityFlagsProduceLightingSelectionAndOnlyCrossingWritesThePlane(int entity, int expected)
    {
        var retained = new Vector4(1, 2, 3, 4);
        var lighting = Wrath335M2WaterQueues.EntityLighting((uint)entity, 7, 0x113, retained);
        Assert.AreEqual((uint)(0x113 | expected), lighting.Flags);
        Assert.AreEqual(entity == 0x60 ? new Vector4(0, 0, 1, -7) : retained, lighting.Plane);
        if (entity == 0x60)
        {
            var viewPlane = Wrath335M2WaterQueues.PlaneToView(lighting.Plane, Matrix4x4.CreateTranslation(0, 0, -10));
            Assert.AreEqual(new Vector4(0, 0, 1, 3), viewPlane);
        }
    }

    [TestMethod]
    public void AuthoredParticleForceBelowBitDiffersFromTailAndCannotOverrideOpaqueRouting()
    {
        Assert.AreEqual(0u, Wrath335M2WaterQueues.InitialParticleWaterFlags(0x40000));
        var flags = Wrath335M2WaterQueues.InitialParticleWaterFlags(0x2000);
        Assert.AreEqual(0x40000u, flags);
        Assert.AreEqual(Wrath335M2QueueMask.BelowWater, Wrath335M2WaterQueues.RouteParticle(3, 1, true, flags));
        Assert.AreEqual(Wrath335M2QueueMask.Opaque, Wrath335M2WaterQueues.RouteParticle(1, 1, true, flags));
    }

    private static M2RootWotlk Root(int flags = 0)
    {
        var root = new M2RootWotlk();
        using var bone = new M2CompBoneWotlkPlus { ParentBone = -1, Flags = (BoneFlags)flags };
        using var sequence = new M2SequenceWotlkToMop();
        root.Bones.Add(bone); root.Sequences.Add(sequence); return root;
    }
    private static M2Animation Animation(M2RootWotlk root) => (M2Animation)typeof(SharedLoader)
        .GetMethod("ReadAnimation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [root])!;
    private static Wrath335M2Bounds? Bounds(M2Root root, bool mpq) => (Wrath335M2Bounds?)typeof(SharedLoader)
        .GetMethod("ReadWrath335Bounds", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [root, mpq]);
    private static Wrath335M2EffectSortTables Tables()
    {
        var vertex = new uint[90]; vertex[1] = 1000; vertex[11] = 1000;
        var pixel = new uint[16]; pixel[0] = 2000; return new(vertex, pixel);
    }
    private static (ParsedDoodadBatch Model, M2InstancePacket Packet) Model()
    {
        using var root = Root();
        var model = new ParsedDoodadBatch { fileDataID = 7, animation = Animation(root),
            boundingBox = new(new(-100), new(100)), boundingRadius = 100,
            wrath335Bounds = new(new(-1), new(1), 1),
            submeshes = [new() { blendType = 2, baseBlendType = 0,
                wrath335Sort = new(0, -3, 1, 0, 1, new(0, 0, 5), 2, false) }] };
        var placement = (M2Container)RuntimeHelpers.GetUninitializedObject(typeof(M2Container));
        placement.FileDataId = 7; placement.Scale = 1;
        var packet = new M2InstancePacket([placement]);
        Assert.IsTrue(packet.EnsureSpatialData(model)); return (model, packet);
    }
    private static bool Add(Wrath335M2ElementQueues queues, ParsedDoodadBatch model,
        M2InstancePacket packet, float alpha, uint cacheFlags = 2, uint elementFlags = 0) =>
        Add(queues, model, packet, alpha, Tables(), cacheFlags, elementFlags);
    private static bool Add(Wrath335M2ElementQueues queues, ParsedDoodadBatch model,
        M2InstancePacket packet, float alpha, Wrath335M2EffectSortTables? effect, uint cacheFlags = 2,
        uint elementFlags = 0) =>
        Wrath335M2MeshQueuePreparation.TryAdd(queues, model, packet, 0, 0, null,
            Matrix4x4.Identity, 1, alpha, effect, default,
            new(0x60, 0, 0, true, 0), new(0, 0, 1, 0), cacheFlags, false, elementFlags: elementFlags);
}
