using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class M2RibbonSmokeTests
{
    [TestMethod]
    public void RibbonEdgesRetainHistoricalBonePositionAndAppearance()
    {
        var animation = CreateAnimation();
        var ribbon = CreateRibbon();
        var mesh = M2RibbonMeshBuilder.Build(animation, ribbon, 0, 750);

        Assert.AreEqual(6, mesh.Vertices.Length);
        Assert.AreEqual(12, mesh.Indices.Length);
        Assert.AreEqual(7.5f, mesh.Vertices[0].Position.X, 0.0001f);
        Assert.AreEqual(5f, mesh.Vertices[2].Position.X, 0.0001f);
        Assert.AreEqual(2.5f, mesh.Vertices[4].Position.X, 0.0001f);
        Assert.AreEqual(-3f, mesh.Vertices[0].Position.Y, 0.0001f);
        Assert.AreEqual(2f, mesh.Vertices[1].Position.Y, 0.0001f);
        Assert.AreEqual(0.25f, mesh.Vertices[4].Position.Z, 0.0001f);
        Assert.AreEqual(0.5f, mesh.Vertices[0].TexCoord.X, 0.0001f);
        Assert.AreEqual(5f / 6f, mesh.Vertices[4].TexCoord.X, 0.0001f);
        Assert.AreEqual(0.75f, mesh.Vertices[0].Color.W, 0.0001f);
        Assert.AreEqual(0.25f, mesh.Vertices[4].Color.W, 0.0001f);
        CollectionAssert.AreEqual(new ushort[] { 0, 1, 2, 1, 3, 2 },
            mesh.Indices[..6]);

        var repeated = M2RibbonMeshBuilder.Build(animation, ribbon, 0, 750);
        CollectionAssert.AreEqual(mesh.Vertices, repeated.Vertices);
        CollectionAssert.AreEqual(mesh.Indices, repeated.Indices);
    }

    [TestMethod]
    public void InvisibleRibbonDoesNotBuildGeometry()
    {
        var ribbon = CreateRibbon() with
        {
            Visibility = Track(new M2Timeline<float>([0], [0f]))
        };
        var mesh = M2RibbonMeshBuilder.Build(CreateAnimation(), ribbon, 0, 750);
        Assert.AreEqual(0, mesh.Vertices.Length);
        Assert.AreEqual(0, mesh.Indices.Length);
    }

    private static M2Animation CreateAnimation() => new()
    {
        Sequences = [new M2Sequence(1000, 1)],
        GlobalLoops = [],
        HasAnimatedBones = true,
        Bones =
        [
            new M2Bone(-1, 0x200, Vector3.Zero,
                Track(new M2Timeline<Vector3>([0, 1000],
                    [Vector3.Zero, new Vector3(10, 0, 0)])),
                Track<Quaternion>(), Track<Vector3>())
        ]
    };

    private static M2RibbonAnimation CreateRibbon() => new(
        BoneIndex: 0,
        Position: Vector3.Zero,
        TextureFileDataId: 1,
        TextureFlags: 0,
        MaterialFlags: 0,
        BlendMode: 2,
        EdgesPerSecond: 4,
        EdgeLifetime: 0.75f,
        Gravity: 1f,
        TextureRows: 1,
        TextureColumns: 2,
        Color: Track<Vector3>(),
        Alpha: Track(new M2Timeline<float>([0, 1000], [0f, 1f])),
        HeightAbove: Track(new M2Timeline<float>([0], [2f])),
        HeightBelow: Track(new M2Timeline<float>([0], [3f])),
        TextureSlot: Track(new M2Timeline<float>([0], [1f])),
        Visibility: Track<float>());

    private static M2Track<T> Track<T>(params M2Timeline<T>[] timelines) => new()
    {
        Interpolation = 1,
        GlobalSequence = -1,
        Timelines = timelines
    };
}
