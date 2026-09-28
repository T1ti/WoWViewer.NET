using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335PortalFogDistanceTests
{
    [TestMethod]
    public void PortalDistanceUsesThePolygonEdgeOutsideItsProjection()
    {
        var portal = Portal();
        Assert.AreEqual(10f,
            Wrath335PortalFogDistance.DistanceToPolygon(new Vector3(0f, 0f, 10f), portal),
            0.0001f);
        Assert.AreEqual(MathF.Sqrt(101f),
            Wrath335PortalFogDistance.DistanceToPolygon(new Vector3(2f, 0f, 10f), portal),
            0.0001f);
    }

    [TestMethod]
    public void PortalDistanceStopsAtClientRangeAndExteriorGroupFlags()
    {
        var wmo = new WorldModel
        {
            portals = [Portal()],
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    portalLinks = [new WmoPortalLink
                    {
                        PortalIndex = 0, TargetGroupIndex = 1
                    }]
                },
                new WorldModelGroupBatches { mogiFlags = 0x8 }
            ]
        };

        Assert.IsTrue(Wrath335PortalFogDistance.TryFind(
            wmo, 0, new Vector3(0f, 0f, 10f), out var nearby));
        Assert.AreEqual(10f, nearby, 0.0001f);
        Assert.IsFalse(Wrath335PortalFogDistance.TryFind(
            wmo, 0, new Vector3(0f, 0f, 30f), out _));
    }

    private static WmoPortal Portal() => new()
    {
        Vertices =
        [
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
        ],
        Normal = Vector3.UnitZ,
        Distance = 0f
    };
}
