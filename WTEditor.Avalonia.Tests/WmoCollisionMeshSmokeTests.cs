using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

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
