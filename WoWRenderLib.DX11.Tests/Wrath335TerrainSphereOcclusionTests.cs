using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Raycasting;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335TerrainSphereOcclusionTests
{
    [DataTestMethod]
    [DataRow(0f, false)]
    [DataRow(0.00000011920929f, false)]
    [DataRow(0.00000023841858f, true)]
    public void RadiusEpsilonIsInclusive(float radius, bool hidden)
    {
        Assert.AreEqual(hidden, Buffer().ContainsSphere(new(new(0, 0, 100), radius)));
    }

    [DataTestMethod]
    [DataRow(49.999f, (byte)16, false)]
    [DataRow(50f, (byte)16, true)]
    [DataRow(1f, (byte)8, true)]
    public void DoodadFlagSixteenKeepsCenterDepthGateAndOnlyBitEightBypassesIt(float depth, byte flags, bool hidden)
    {
        Assert.AreEqual(hidden, Buffer().ContainsSphere(new(new(0, -0.1f, depth), 0.1f), flags));
    }

    [TestMethod]
    public void SphereUsesCenterDepthAndProjectedTopRatherThanBoxCorners()
    {
        var buffer = Buffer();
        Assert.IsTrue(buffer.ContainsSphere(new(new(0, 19, 100), 1)));
        Assert.IsFalse(buffer.ContainsSphere(new(new(0, 19.001f, 100), 1)));
        Assert.IsTrue(buffer.ContainsSphere(new(new(0, 0, 50), 1)));
        Assert.IsFalse(buffer.ContainsBox(new(new(-1, -1, 49), new(1, 1, 51))));
    }

    [TestMethod]
    public void RadiusUsesSeparateProjectionAndNativeSwappedExtentsWithPointTranslation()
    {
        var radiusProjection = Matrix4x4.CreateScale(2, 4, 1);
        radiusProjection.M41 = 1;
        radiusProjection.M42 = 2;
        var buffer = Buffer(radiusProjection: radiusProjection);
        // radius=1 -> projected X=3 (top), Y=6 (half-width).
        Assert.IsTrue(buffer.ContainsSphere(new(new(0, 17, 100), 1)));
        Assert.IsFalse(buffer.ContainsSphere(new(new(0, 17.001f, 100), 1)));
        buffer.BeginProjected(Matrix4x4.Identity, 0, sphereProjection: radiusProjection);
        buffer.UpdateProtectedLine(new(-5, 20, 100), new(5, 20, 100));
        Assert.IsFalse(buffer.ContainsSphere(new(new(0, 0, 100), 1)));
        buffer.UpdateProtectedLine(new(-10, 20, 100), new(10, 20, 100));
        Assert.IsTrue(buffer.ContainsSphere(new(new(0, 0, 100), 1)));
    }

    [TestMethod]
    public void SphereConsumerRequiresExtraRightColumnAndClampsPartlyOffscreenIntervals()
    {
        var buffer = Buffer();
        buffer.BeginProjected(Matrix4x4.Identity, 0);
        buffer.UpdateProtectedLine(new(0, 20, 100), new(0, 20, 100));
        Assert.IsFalse(buffer.ContainsSphere(new(new(0, 0, 100), 0.01f)));
        buffer.UpdateProtectedLine(new(-3.125f, 20, 100), new(3.125f, 20, 100));
        Assert.IsTrue(buffer.ContainsSphere(new(new(0, 0, 100), 0.01f)));
        buffer.UpdateProtectedLine(new(-400, 20, 100), new(400, 20, 100));
        Assert.IsFalse(buffer.ContainsSphere(new(new(350, 0, 100), 1)));
        Assert.IsTrue(buffer.ContainsSphere(new(new(300, 0, 100), 10)));
    }

    [TestMethod]
    public void FlattenedCameraTranslationAndSeparateProjectionRetainWorldRadius()
    {
        var buffer = new Wrath335TerrainClipBuffer();
        buffer.Begin(new(10, 20, 30), new(4, 0, 3), Matrix4x4.Identity);
        buffer.UpdateProtectedLine(new(110, -80, 50), new(110, 120, 50));
        Assert.IsTrue(buffer.ContainsSphere(new(new(110, 20, 49), 1)));
        Assert.IsFalse(buffer.ContainsSphere(new(new(110, 20, 49.001f), 1)));
    }

    [DataTestMethod]
    [DataRow(-0.9f, true)]
    [DataRow(0.9f, true)]
    [DataRow(-0.90001f, false)]
    [DataRow(0.90001f, false)]
    public void PitchAndEnabledGatesApplyToSphereReader(float pitch, bool hidden)
    {
        var buffer = Buffer(pitch);
        Assert.AreEqual(hidden, buffer.ContainsSphere(new(new(0, 0, 100), 1)));
        buffer.BeginProjected(Matrix4x4.Identity, 0, enabled: false);
        Assert.IsFalse(buffer.ContainsSphere(new(new(0, 0, 100), 1)));
    }

    [TestMethod]
    public void InvalidProjectionAndFrameClearFailOpen()
    {
        var buffer = Buffer();
        Assert.IsFalse(buffer.ContainsSphere(new(new(float.NaN, 0, 100), 1)));
        Assert.IsFalse(buffer.ContainsSphere(new(new(0, 0, 0), 1), 8));
        Assert.IsFalse(buffer.ContainsSphere(new(new(0, 0, 100), float.PositiveInfinity)));
        buffer.Clear();
        Assert.IsFalse(buffer.ContainsSphere(new(new(0, 0, 100), 1)));
    }

    private static Wrath335TerrainClipBuffer Buffer(float pitch = 0, Matrix4x4? radiusProjection = null)
    {
        var result = new Wrath335TerrainClipBuffer();
        result.BeginProjected(Matrix4x4.Identity, pitch, sphereProjection: radiusProjection);
        result.UpdateProtectedLine(new(-100, 20, 100), new(100, 20, 100));
        return result;
    }
}
