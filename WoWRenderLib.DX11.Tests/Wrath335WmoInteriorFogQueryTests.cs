using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335WmoInteriorFogQueryTests
{
    private static readonly Wrath335FogState Outdoor = new(50, 500, 2, 0xff203040);

    [DataTestMethod]
    [DataRow(8u)]
    [DataRow(64u)]
    [DataRow(72u)]
    public void ExteriorLitViewerDoesNotActivateWhiteInteriorMfog(uint flags)
    {
        var model = Model([Group(flags)]);
        Assert.IsFalse(Evaluate(model, new(0, -1), out var current));
        Assert.AreEqual(Outdoor, current);
    }

    [TestMethod]
    public void InteriorEligibilityReadsMogpRatherThanRootMogiFlags()
    {
        var model = Model([Group(0) with { mogiFlags = 0x48 }]);
        Assert.IsTrue(Evaluate(model, new(0, -1), out var current));
        Assert.AreEqual(new Wrath335FogState(25, 100, 1, 0xffffffff), current);
    }

    [TestMethod]
    public void SecondaryInteriorActivatesFogButFirstGroupStillSuppliesVolumeIds()
    {
        var model = Model([Group(0x40, [1]), Group(0, [2])]);
        Assert.IsTrue(Evaluate(model, new(0, 1), out var current));
        Assert.AreEqual(new Wrath335FogState(50, 200, 1, 0xff112233), current);
    }

    [TestMethod]
    public void PortalWeightUsesTheNearestExteriorPortalAcrossBothInteriorGroups()
    {
        var model = Model([
            Group(0) with { portalLinks = [new() { PortalIndex = 0, TargetGroupIndex = 2 }] },
            Group(0) with { portalLinks = [new() { PortalIndex = 1, TargetGroupIndex = 2 }] },
            Group(8) with { mogiFlags = 8 }
        ]);
        model.portals = [Portal(0), Portal(15)];
        Assert.IsTrue(Wrath335WmoInteriorFogQuery.TryEvaluate(model, new(0, 1),
            new(0, 0, 20), Outdoor, 500, false, out var current));
        Assert.AreEqual(420f, current.EndDistance, 0.0001f); // distance 5, weight 0.2
        Assert.AreEqual(45f, current.StartDistance, 0.0001f);
        Assert.AreEqual(1.8f, current.Rate, 0.0001f);
    }

    [TestMethod]
    public void ExteriorSecondGroupDoesNotCancelTheFirstInterior()
    {
        var model = Model([Group(0), Group(0x48)]);
        Assert.IsTrue(Evaluate(model, new(0, 1), out var current));
        Assert.AreEqual(100f, current.EndDistance);
    }

    [TestMethod]
    public void SingleFogRecordAndOtherClientsDoNotUseTheWrathQuery()
    {
        var model = Model([Group(0)]);
        model.fogs = [model.fogs[0]];
        Assert.IsFalse(Evaluate(model, new(0, -1), out _));
        model = Model([Group(0)]);
        model.wrath335 = false;
        Assert.IsFalse(Evaluate(model, new(0, -1), out _));
    }

    private static bool Evaluate(WorldModel model, WmoViewerGroups groups, out Wrath335FogState current) =>
        Wrath335WmoInteriorFogQuery.TryEvaluate(model, groups, Vector3.Zero, Outdoor, 500, false, out current);

    private static WorldModel Model(WorldModelGroupBatches[] groups) => new()
    {
        wrath335 = true, legacyLighting = true, groupBatches = groups, portals = [],
        fogs = [Fog(100, 0xffffffff), Fog(200, 0xff112233), Fog(300, 0xffaa4433)]
    };

    private static WmoFogVolume Fog(float end, uint color) =>
        new(0, Vector3.Zero, 100, 200, new(end, 0.25f, color), default);

    private static WorldModelGroupBatches Group(uint flags, byte[]? ids = null) => new()
    { flags = flags, mogiFlags = 0, fogIds = ids ?? [0, 0, 0, 0], portalLinks = [] };

    private static WmoPortal Portal(float z) => new()
    {
        Normal = Vector3.UnitZ, Distance = -z,
        Vertices = [new(-1, -1, z), new(1, -1, z), new(1, 1, z), new(-1, 1, z)]
    };
}
