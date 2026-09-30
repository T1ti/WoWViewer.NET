using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335TerrainClipBufferTests
{
    [TestMethod]
    public void FreshFrameAndClearDropBothHorizonHeightsAndProtectedFlags()
    {
        var buffer = Buffer();
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 0f, 100f)));
        buffer.UpdateProtectedLine(new(-100f, 20f, 100f), new(100f, 20f, 100f));
        Assert.IsTrue(buffer.ContainsBox(Point(0f, 10f, 100f)));
        buffer.BeginProjected(Matrix4x4.Identity, 0f);
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 10f, 100f)));
        buffer.Clear();
        Assert.IsFalse(buffer.Active);
    }

    [TestMethod]
    public void SegmentUsesLowerEndpointRatherThanInterpolatingItsSlope()
    {
        var buffer = Buffer();
        buffer.UpdateProtectedLine(new(-100f, 20f, 100f), new(100f, 100f, 100f));
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 40f, 100f)));
        Assert.IsTrue(buffer.ContainsBox(Point(0f, 20f, 100f)));
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 20.001f, 100f)));
    }

    [TestMethod]
    public void ConsumerRequiresItsExtraRightColumnToBeCovered()
    {
        var buffer = Buffer();
        buffer.UpdateProtectedLine(new(0f, 20f, 100f), new(0f, 20f, 100f));
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 10f, 100f)));
        buffer.UpdateProtectedLine(new(0f, 20f, 100f), new(3.125f, 20f, 100f));
        Assert.IsTrue(buffer.ContainsBox(Point(0f, 10f, 100f)));
    }

    [DataTestMethod]
    [DataRow(0f, 192)]
    [DataRow(0.015625f, 192)]
    [DataRow(0.03125f, 194)]
    [DataRow(0.046875f, 194)]
    [DataRow(-0.015625f, 190)]
    [DataRow(-0.0078125f, 191)]
    public void ColumnConversionKeepsNearestEvenHalfUnitBoundaries(float x, int expected)
    {
        Assert.IsTrue(Wrath335TerrainClipBuffer.TryColumn(x, out var actual));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void PartlyOffscreenIntervalsClampWhileWhollyOffscreenBoxesRemainVisible()
    {
        var buffer = Buffer();
        buffer.UpdateProtectedLine(new(-400f, 20f, 100f), new(400f, 20f, 100f));
        Assert.IsFalse(buffer.ContainsBox(Point(-350f, 10f, 100f)));
        Assert.IsFalse(buffer.ContainsBox(Point(350f, 10f, 100f)));
        Assert.IsTrue(buffer.ContainsBox(new(new(-350f, 10f, 100f), new(-250f, 10f, 100f))));
        Assert.IsTrue(buffer.ContainsBox(Point(299f, 10f, 100f)));
    }

    [DataTestMethod]
    [DataRow(49.999f, (byte)1, false)]
    [DataRow(50f, (byte)1, true)]
    [DataRow(1f, (byte)8, true)]
    public void WmoArgumentOneKeepsTheFiftyUnitGateAndOnlyBitEightBypassesIt(
        float depth, byte flags, bool hidden)
    {
        var buffer = Buffer();
        buffer.UpdateProtectedLine(new(-100f, 20f, 100f), new(100f, 20f, 100f));
        Assert.AreEqual(hidden, buffer.ContainsBox(Point(0f, 0f, depth), flags));
    }

    [TestMethod]
    public void EveryBoundsCornerMustPassDepthAndTheHighestProjectionMustFitTheHorizon()
    {
        var buffer = Buffer();
        buffer.UpdateProtectedLine(new(-100f, 20f, 100f), new(100f, 20f, 100f));
        Assert.IsFalse(buffer.ContainsBox(new(new(0f, 0f, 49f), new(1f, 0f, 100f))));
        Assert.IsFalse(buffer.ContainsBox(new(new(0f, 0f, 100f), new(1f, 21f, 200f))));
        Assert.IsTrue(buffer.ContainsBox(new(new(0f, 0f, 100f), new(1f, 20f, 200f))));
    }

    [DataTestMethod]
    [DataRow(-0.9f, true)]
    [DataRow(0.9f, true)]
    [DataRow(-0.90001f, false)]
    [DataRow(0.90001f, false)]
    public void PitchGateIsInclusiveAndDisablesBothWriterAndReader(float pitch, bool hidden)
    {
        var buffer = Buffer(pitch);
        buffer.UpdateProtectedLine(new(-100f, 20f, 100f), new(100f, 20f, 100f));
        Assert.AreEqual(hidden, buffer.ContainsBox(Point(0f, 0f, 100f)));
        buffer.BeginProjected(Matrix4x4.Identity, 0f, enabled: false);
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 0f, 100f)));
    }

    [TestMethod]
    public void BothSegmentEndpointsMustMeetTheProducerDepthThreshold()
    {
        var buffer = Buffer();
        buffer.UpdateProtectedLine(new(-0.01f, 1f, 0.02f), new(0.01f, 1f, 0.027777778f));
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 0f, 100f)));
        buffer.UpdateProtectedLine(new(-0.01f, 1f, 0.027777778f), new(0.01f, 1f, 0.027777778f));
        Assert.IsTrue(buffer.ContainsBox(Point(0f, 0f, 100f)));
    }

    [TestMethod]
    public void ProjectionDividesByStoredClipZRatherThanHomogeneousW()
    {
        var matrix = Matrix4x4.Identity;
        matrix.M34 = 9f;
        var buffer = Buffer();
        buffer.BeginProjected(matrix, 0f);
        buffer.UpdateProtectedLine(new(-100f, 20f, 100f), new(100f, 20f, 100f));
        Assert.IsTrue(buffer.ContainsBox(Point(0f, 10f, 100f)));
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 21f, 100f)));
    }

    [TestMethod]
    public void CameraPitchIsFlattenedBeforeApplyingTheCapturedProjection()
    {
        var buffer = new Wrath335TerrainClipBuffer();
        buffer.Begin(Vector3.Zero, new(4f, 0f, 3f), Matrix4x4.Identity);
        buffer.UpdateProtectedLine(new(100f, -100f, 20f), new(100f, 100f, 20f));
        Assert.IsTrue(buffer.ContainsBox(Point(200f, 0f, 30f)));
        Assert.IsFalse(buffer.ContainsBox(Point(200f, 0f, 50f)));
    }

    [TestMethod]
    public void TerrainHolesEraseUnprotectedColumnsButPreserveProtectedHorizonLines()
    {
        var buffer = new Wrath335TerrainClipBuffer();
        buffer.Begin(Vector3.Zero, Vector3.UnitX, Matrix4x4.Identity);
        var edge = TerrainVertices(100f, 110f, 20f);
        buffer.UpdateTerrainEdges(edge, Matrix4x4.Identity, Vector3.UnitX, false);
        Assert.IsTrue(buffer.ContainsBox(Point(200f, 0f, 10f)));
        buffer.UpdateTerrainEdges(edge, Matrix4x4.Identity, Vector3.UnitX, true);
        Assert.IsFalse(buffer.ContainsBox(Point(200f, 0f, 10f)));
        buffer.UpdateProtectedLine(new(100f, -50f, 20f), new(100f, 50f, 20f));
        buffer.UpdateTerrainEdges(edge, Matrix4x4.Identity, Vector3.UnitX, true);
        Assert.IsTrue(buffer.ContainsBox(Point(200f, 0f, 10f)));
        buffer.Begin(Vector3.Zero, Vector3.UnitX, Matrix4x4.Identity);
        buffer.UpdateTerrainEdges(edge, Matrix4x4.Identity, Vector3.UnitX, true);
        Assert.IsFalse(buffer.ContainsBox(Point(200f, 0f, 10f)));
    }

    [TestMethod]
    public void TerrainEdgeSelectionTracksBothSignsAndTreatsZeroAsTheFirstEdge()
    {
        var buffer = new Wrath335TerrainClipBuffer();
        buffer.Begin(Vector3.Zero, Vector3.UnitX, Matrix4x4.Identity);
        var vertices = TerrainVertices(100f, 110f, 0f);
        for (var index = 0; index < 9; index++)
            vertices[index].Position.Z = 20f; // Only row zero covers the center.
        buffer.UpdateTerrainEdges(vertices, Matrix4x4.Identity, Vector3.UnitX, false);
        Assert.IsTrue(buffer.ContainsBox(Point(200f, 0f, 10f)));
        buffer.Begin(Vector3.Zero, Vector3.UnitX, Matrix4x4.Identity);
        buffer.UpdateTerrainEdges(vertices, Matrix4x4.Identity, -Vector3.UnitX, false);
        Assert.IsFalse(buffer.ContainsBox(Point(200f, 0f, 10f)));
        buffer.Begin(Vector3.Zero, Vector3.UnitY, Matrix4x4.Identity);
        vertices = TerrainVertices(-50f, 50f, 0f);
        for (var index = 0; index < vertices.Length; index++)
            vertices[index].Position.Y += 160f;
        for (var index = 0; index < 9; index++)
            vertices[index * 17].Position.Z = 20f;
        buffer.UpdateTerrainEdges(vertices, Matrix4x4.Identity, Vector3.UnitY, false);
        Assert.IsTrue(buffer.ContainsBox(Point(0f, 250f, 10f)));
        buffer.Begin(Vector3.Zero, Vector3.UnitY, Matrix4x4.Identity);
        buffer.UpdateTerrainEdges(vertices, Matrix4x4.Identity, -Vector3.UnitY, false);
        Assert.IsFalse(buffer.ContainsBox(Point(0f, 250f, 10f)));
    }

    internal static ADTVertex[] TerrainVertices(float minX, float maxX, float height)
    {
        var vertices = new ADTVertex[145];
        for (var row = 0; row < 9; row++)
            for (var column = 0; column < 9; column++)
                vertices[row * 17 + column].Position = new(maxX - row * (maxX - minX) / 8f,
                    50f - column * 12.5f, height);
        return vertices;
    }

    private static Wrath335TerrainClipBuffer Buffer(float pitch = 0f)
    {
        var buffer = new Wrath335TerrainClipBuffer();
        buffer.BeginProjected(Matrix4x4.Identity, pitch);
        return buffer;
    }

    private static BoundingBox Point(float x, float y, float z) => new(new(x, y, z), new(x, y, z));
}
