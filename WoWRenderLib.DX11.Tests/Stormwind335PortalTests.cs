using System.Numerics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Stormwind335PortalTests
{
    [DataTestMethod]
    [DataRow(181)]
    [DataRow(182)]
    [DataRow(212)]
    [DataRow(229)]
    [DataRow(231)]
    [DataRow(235)]
    [DataRow(239)]
    public void FacadePortalCameraSelectsTheNativePairAndProjectionKeepsFullView(int index)
    {
        using var fixture = ReadFixture();
        var root = fixture.RootElement;
        var entry = root.GetProperty("facadePortals").EnumerateArray().Single(p => p.GetProperty("portal").GetInt32() == index);
        var portal = new WmoPortal { Normal = Vector(entry.GetProperty("normal")),
            Distance = entry.GetProperty("distance").GetSingle(),
            Vertices = entry.GetProperty("vertices").EnumerateArray().Select(Vector).ToArray() };
        var center = portal.Vertices.Aggregate(Vector3.Zero, (a, b) => a + b) / portal.Vertices.Length;
        Assert.IsTrue(Wrath335PortalProjection.ContainsEye(portal, center), $"Portal {index}");
        var projection = new Wrath335PortalProjection();
        var cameraProjection = Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(0.01f);
        projection.Prepare(Matrix4x4.Identity, cameraProjection);
        Assert.AreEqual(WmoPortalRect.Full, projection.Project(portal, center));

        var references = entry.GetProperty("references").EnumerateArray().ToArray();
        Assert.AreEqual(2, references.Length); // These particular facade links are paired in this asset.
        var sourceMap = Enumerable.Repeat(-1, root.GetProperty("sourceGroupCount").GetInt32()).ToArray();
        for (var i = 0; i < 2; i++)
            sourceMap[references[i].GetProperty("owner").GetInt32()] = i;
        var bounds = new BoundingBox(center - new Vector3(100f), center + new Vector3(100f));
        var groups = new WorldModelGroupBatches[2];
        for (var i = 0; i < 2; i++)
        {
            var reference = references[i];
            var owner = reference.GetProperty("owner").GetInt32();
            var header = root.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("index").GetInt32() == owner);
            groups[i] = new() { flags = header.GetProperty("mogpFlags").GetUInt32(),
                mogiFlags = header.GetProperty("mogiFlags").GetUInt32(),
                boundingBox = bounds, mogiBoundingBox = bounds, doodadReferences = [],
                portalLinks = WMOLoader.BuildPortalLinks(new() { portalCount = 1 },
                    [new() { PortalIndex = 0, GroupIndex = reference.GetProperty("target").GetUInt16(),
                        Side = reference.GetProperty("side").GetInt16() }], sourceMap) };
        }
        var model = new WorldModel { wrath335 = true, legacyLighting = true, portalGraphValid = true,
            boundingBox = bounds, groupBatches = groups, portals = [portal], doodads = [], wmoRenderBatches = [] };
        var scratch = new WmoPortalVisibilityScratch();
        WmoPortalVisibility.TryLocateViewerGroup(model, Matrix4x4.Identity, center, [true, true], scratch, out _);
        Assert.IsTrue(scratch.PortalViewerOverride);
        Assert.AreEqual(0f, scratch.PrimaryViewerHitDistance);
        if ((groups[0].mogiFlags & 8) == 0 && (groups[1].mogiFlags & 8) == 0)
        {
            Assert.AreNotEqual(scratch.PrimaryViewerGroupIndex, scratch.SecondaryViewerGroupIndex);
            var visible = new bool[2];
            Assert.IsTrue(WmoPortalVisibility.TryCompute(model, Matrix4x4.Identity, cameraProjection,
                center, [true, true], visible, [], [], scratch, out _, 1760f,
                new(scratch.PrimaryViewerGroupIndex, scratch.SecondaryViewerGroupIndex)));
            CollectionAssert.AreEqual(new[] { true, true }, visible);
            Assert.AreEqual(WmoPortalRect.Full, scratch.PortalRects[0]);
        }
    }

    [TestMethod]
    public void ElevenSingleOwnerPortalsRetainTheirDirectionAndEmptyDestinationRanges()
    {
        using var fixture = ReadFixture();
        var root = fixture.RootElement;
        Assert.AreEqual(286, root.GetProperty("sourceGroupCount").GetInt32());
        Assert.AreEqual(627, root.GetProperty("ownedReferenceCount").GetInt32());
        Assert.AreEqual(0, root.GetProperty("nullTargetCount").GetInt32());
        var references = root.GetProperty("oneWayReferences").EnumerateArray().ToArray();
        Assert.AreEqual(11, references.Length);
        foreach (var reference in references)
        {
            var owner = reference.GetProperty("owner").GetInt32();
            var target = reference.GetProperty("target").GetUInt16();
            var destination = root.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("index").GetInt32() == target);
            Assert.AreEqual(0, destination.GetProperty("portalCount").GetInt32());
            var map = Enumerable.Repeat(-1, 286).ToArray();
            map[owner] = 0;
            map[target] = 1;
            PreppedWMOPortalReference[] raw = [new() { PortalIndex = reference.GetProperty("portal").GetUInt16(),
                GroupIndex = target, Side = reference.GetProperty("side").GetInt16() }];
            var links = WMOLoader.BuildPortalLinks(new() { portalCount = 1 }, raw, map);
            Assert.AreEqual(1, links.Length);
            Assert.AreEqual((ushort)1, links[0].TargetGroupIndex);
            Assert.AreEqual(raw[0].Side, links[0].Side);
            Assert.AreEqual(0, WMOLoader.BuildPortalLinks(new() { portalCount = 0 }, raw, map).Length);
        }
    }

    private static Vector3 Vector(JsonElement element) => new(element[0].GetSingle(), element[1].GetSingle(), element[2].GetSingle());
    private static JsonDocument ReadFixture()
    {
        using var stream = typeof(Stormwind335PortalTests).Assembly.GetManifestResourceStream(
            "WoWRenderLib.DX11.Tests.Fixtures.Stormwind335Portals.json")!;
        return JsonDocument.Parse(stream);
    }
}
