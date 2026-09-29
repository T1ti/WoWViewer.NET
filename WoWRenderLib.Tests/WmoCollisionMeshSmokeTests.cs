using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class WmoCollisionMeshSmokeTests
{
    [TestMethod]
    public void LegacyMopyOnlyExpandsInvisibleCollisionTriangles()
    {
        var vertices = new WMOVertex[4];
        for (var index = 0; index < vertices.Length; index++)
            vertices[index].Position = new Vector3(index, 0, 0);
        ushort[] indices = [0, 1, 2, 0, 2, 3, 1, 2, 3, 0, 1, 3];
        // Hidden sentinel, rendered collidable, hidden collision flag, detail.
        byte[] faces = [0x08, 0xFF, 0x28, 1, 0x08, 1, 0x04, 1];

        var packed = WMOLoader.ReadCollisionVertexBuffer(
            GroupWithChunk("MOPY", faces), vertices, indices);
        var collision = MemoryMarshal.Cast<byte, WMOCollisionVertex>(packed);

        Assert.AreEqual(6, collision.Length);
        Assert.AreEqual(vertices[0].Position, collision[0].Position);
        Assert.AreEqual(vertices[3].Position, collision[5].Position);
        Assert.AreEqual(new Vector2(1, 0), collision[0].Barycentric);
        Assert.AreEqual(new Vector2(0, 1), collision[1].Barycentric);
        Assert.AreEqual(Vector2.Zero, collision[2].Barycentric);
    }

    [TestMethod]
    public void ModernMpy2AcceptsWideCollisionSentinel()
    {
        var vertices = new WMOVertex[3];
        ushort[] indices = [0, 1, 2, 0, 1, 2];
        byte[] faces = [0x20, 0, 0xFF, 0xFF, 0x20, 0, 1, 0];

        var packed = WMOLoader.ReadCollisionVertexBuffer(
            GroupWithChunk("MPY2", faces), vertices, indices);

        Assert.AreEqual(3 * Marshal.SizeOf<WMOCollisionVertex>(), packed.Length);
    }

    [TestMethod]
    public void LegacyViewerRayUsesOnlyBspReferencedFaces()
    {
        ushort[] triangles = [0, 1, 2, 3, 4, 5, 6, 7, 8];
        byte[] faceReferences = [2, 0, 2, 0, 0xFF, 0xFF, 0, 0];

        var selected = WMOLoader.ReadViewerRayIndices(
            GroupWithChunk("MOBR", faceReferences), triangles);

        CollectionAssert.AreEqual(new ushort[] { 6, 7, 8, 0, 1, 2 }, selected);
        CollectionAssert.AreEqual(triangles,
            WMOLoader.ReadViewerRayIndices(GroupWithChunk("MOPY", [0, 0]), triangles));
        Assert.AreEqual(0, WMOLoader.ReadViewerRayIndices(
            GroupWithChunk("MOBR", []), triangles).Length);
    }

    [TestMethod]
    public void DecodedViewerBspPreservesFaceIdentityDuplicatesAndUnsignedChildren()
    {
        WoWLib.Formats.WMO.Group.Chunks.CAaBspNode.Data[] nodes =
        [
            new() { Flags = 0, NegChild = -32768, PosChild = -1,
                NFaces = 2, FaceStart = 1, PlaneDist = 3.5f }
        ];
        WoWLib.Formats.WMO.Group.Chunks.SmoPoly.Data[] polys =
        [new() { Flags = 0x80 }, new() { Flags = 0x28 }];
        ushort[] indices = [0, 1, 2, 3, 4, 5];
        ushort[] references = [1, 1, 0];

        var tree = WMOLoader.ReadViewerBsp(nodes, references, indices, polys);
        Assert.AreEqual((ushort)32768, tree.Nodes[0].NegativeChild);
        Assert.AreEqual(ushort.MaxValue, tree.Nodes[0].PositiveChild);
        Assert.AreEqual((uint)1, tree.Nodes[0].FaceStart);
        Assert.AreEqual(3.5f, tree.Nodes[0].PlaneDistance);
        CollectionAssert.AreEqual(new ushort[] { 1, 1, 0 }, tree.FaceReferences);
        CollectionAssert.AreEqual(new byte[] { 0x80, 0x28 }, tree.FaceFlags);
        CollectionAssert.AreEqual(indices, tree.Indices);
        // The prepared data owns copies of the temporary wowlib spans.
        nodes[0].NegChild = 0;
        references[0] = 0;
        polys[0].Flags = 0;
        Assert.AreEqual((ushort)32768, tree.Nodes[0].NegativeChild);
        Assert.AreEqual((ushort)1, tree.FaceReferences[0]);
        Assert.AreEqual((byte)0x80, tree.FaceFlags[0]);
    }

    [DataTestMethod]
    [DataRow(450, true)]
    [DataRow(451, false)]
    public void ViewerBspDigestFallsBackAboveUniqueVertexLimit(int uniqueVertices, bool expected)
    {
        var faceCount = (uniqueVertices + 2) / 3;
        var indices = new ushort[faceCount * 3];
        for (var index = 0; index < indices.Length; index++)
            indices[index] = (ushort)(index < uniqueVertices ? index : 0);
        var tree = new WmoBspTree(
            [new(4, ushort.MaxValue, ushort.MaxValue, (ushort)faceCount, 0, 0f)],
            Enumerable.Range(0, faceCount).Select(i => (ushort)i).ToArray(),
            indices, new byte[faceCount]);

        Assert.AreEqual(expected, tree.DigestEligibleLeaves[0]);
    }

    private static byte[] GroupWithChunk(string fourCc, byte[] payload)
    {
        var bytes = new byte[8 + 68 + 8 + payload.Length];
        "PGOM"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        for (var index = 0; index < 4; index++)
            bytes[76 + index] = (byte)fourCc[3 - index];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(80), payload.Length);
        payload.CopyTo(bytes.AsSpan(84));
        return bytes;
    }
}
