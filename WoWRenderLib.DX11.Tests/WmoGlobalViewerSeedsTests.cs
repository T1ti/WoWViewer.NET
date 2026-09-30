using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class WmoGlobalViewerSeedsTests
{
    [TestMethod]
    public void SharedWinnerSeedsItsSelectedGroupWithoutRepeatingBspQuery()
    {
        var model = Model(Group(), Group());
        var scratch = new WmoPortalVisibilityScratch();
        var visible = Compute(model, new(1, -1), scratch, out _);
        CollectionAssert.AreEqual(new[] { false, true }, visible);
        Assert.AreEqual(0u, scratch.ViewerBsp.Epoch);
    }

    [TestMethod]
    public void UnselectedPlacementDoesNotPromoteItsOwnFloorToAnInteriorSeed()
    {
        var model = Model(Group());
        CollectionAssert.AreEqual(new[] { false }, Compute(model, new(-1, -1), new(), out _));
        var visible = new bool[1];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(model, Matrix4x4.Identity, Matrix4x4.Identity,
            new(0f, 0f, 1f), [true], visible, [], [], new(), out _));
        Assert.IsTrue(visible[0]);
    }

    [TestMethod]
    public void RepeatedRegistryGroupTraversesItsPortalOnlyOnce()
    {
        var owner = Group() with
        {
            portalLinks = [new() { PortalIndex = 0, TargetGroupIndex = 1, Side = 1 }]
        };
        var model = Model(owner, Group());
        model.portals = [new()
        {
            Normal = Vector3.UnitZ,
            Vertices = [new(-0.2f, -0.2f, 0f), new(0.2f, -0.2f, 0f),
                new(0.2f, 0.2f, 0f), new(-0.2f, 0.2f, 0f)]
        }];
        CollectionAssert.AreEqual(new[] { true, true }, Compute(model, new(0, 0), new(), out var traversed));
        Assert.AreEqual(1, traversed);
    }

    [TestMethod]
    public void BothPortalGroupsSeedTraversalAndInvalidGroupsAreIgnored()
    {
        var model = Model(Group(), Group(), Group());
        CollectionAssert.AreEqual(new[] { true, false, true }, Compute(model, new(2, 0), new(), out _));
        CollectionAssert.AreEqual(new[] { false, false, false }, Compute(model, new(99, -1), new(), out _));
    }

    [TestMethod]
    public void SelectedExteriorLitGroupKeepsExteriorVisibilityAvailable()
    {
        var model = Model(Group(0x40), Group(8));
        CollectionAssert.AreEqual(new[] { true, true }, Compute(model, new(0, -1), new(), out _));
        model.groupBatches[0] = Group();
        CollectionAssert.AreEqual(new[] { true, false }, Compute(model, new(0, -1), new(), out _));
    }

    [TestMethod]
    public void ExplicitRegistryUsesLoadedMogpFlagsWithoutRejectingPortalSelectedGroup()
    {
        // Portal selection reads MOGI; its selected index is then resolved to a
        // loaded MOGP group. The top-level seed pass does not reject MOGP bit 8.
        var selected = Group(8) with { mogiFlags = 0 };
        var model = Model(selected);
        Assert.IsTrue(Compute(model, new(0, -1), new(), out _)[0]);
    }

    [TestMethod]
    public void OtherClientsIgnoreWrathRegistryOverride()
    {
        var model = Model(Group());
        model.wrath335 = false;
        Assert.IsTrue(Compute(model, new(-1, -1), new(), out _)[0]);
    }

    private static bool[] Compute(WorldModel model, WmoViewerGroups groups,
        WmoPortalVisibilityScratch scratch, out int traversed)
    {
        var visible = new bool[model.groupBatches.Length];
        Assert.IsTrue(WmoPortalVisibility.TryCompute(model, Matrix4x4.Identity, Matrix4x4.Identity,
            new(0f, 0f, 1f), Enumerable.Repeat(true, visible.Length).ToArray(), visible,
            [], [], scratch, out traversed, 1760f, groups));
        return visible;
    }

    private static WorldModel Model(params WorldModelGroupBatches[] groups) => new()
    {
        wrath335 = true, legacyLighting = true, portalGraphValid = true,
        boundingBox = new(new(-2f, -2f, -2f), new(2f, 2f, 2f)),
        groupBatches = groups, portals = [], doodads = [], wmoRenderBatches = []
    };

    private static WorldModelGroupBatches Group(uint flags = 0) => new()
    {
        flags = flags, mogiFlags = flags,
        boundingBox = new(new(-2f, -2f, -2f), new(2f, 2f, 2f)),
        mogiBoundingBox = new(new(-0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, 0.5f)),
        portalLinks = [], doodadReferences = [],
        raycastVertices = [new(-1f, -1f, 0f), new(1f, -1f, 0f), new(0f, 1f, 0f)],
        raycastIndices = [0, 1, 2],
        viewerBsp = new([new(4, ushort.MaxValue, ushort.MaxValue, 1, 0, 0f)], [0], [0, 1, 2], [0])
    };
}
