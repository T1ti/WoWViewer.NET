using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWLib.Formats.Common;
using WoWLib.Formats.M2.Root;
using WoWLib.Formats.M2.Skin;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;
using SharedLoader = WoWRenderLib.Loaders.M2Loader;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335M2MeshSortInputsTests
{
    [TestMethod]
    public void NativeShaderInputsRemainOwnedAndDistinctFromSectionBoneComboIndex()
    {
        Submesh mesh;
        using (var root = new M2RootWotlk())
        using (var profile = Profile())
        {
            profile.Submeshes[0].BoneInfluences = 4;
            profile.Submeshes[0].BoneCount = 321;
            profile.Submeshes[0].BoneComboIndex = 42;
            profile.Batches[0].ShaderId = 0x8001;
            mesh = Decode(root, profile)[0];
            profile.Submeshes[0].BoneInfluences = 1;
            profile.Batches[0].ShaderId = 0x8002;
        }
        Assert.AreEqual((ushort)4, mesh.wrath335Sort!.Value.BoneInfluences);
        Assert.AreEqual((ushort)42, mesh.wrath335Sort.Value.SectionBoneComboIndex);
        Assert.AreEqual((ushort)0x8001, mesh.wrath335Sort.Value.ShaderId);
    }

    [TestMethod]
    public void OpaqueSectionTieUsesNativeComboStartAtOffset0ENotTheCountAt0C()
    {
        Assert.AreEqual(0x0C, Marshal.OffsetOf<M2SkinSectionTbcPlus.Data>("BoneCount").ToInt32());
        Assert.AreEqual(0x0E, Marshal.OffsetOf<M2SkinSectionTbcPlus.Data>("BoneComboIndex").ToInt32());
        Assert.AreEqual(0x10, Marshal.OffsetOf<M2SkinSectionTbcPlus.Data>("BoneInfluences").ToInt32());
        using var root = new M2RootWotlk();
        using var profile = Profile();
        AddSection(profile, 1, Vector3.Zero, 1, 0, 4);
        AddBatch(profile, 1, 0, 0);
        profile.Submeshes[0].BoneCount = 100;
        profile.Submeshes[0].BoneComboIndex = 2;
        profile.Submeshes[1].BoneCount = 1;
        var batches = Decode(root, profile);
        var a = new Wrath335M2ElementSortData { SectionBoneComboIndex = batches[0].wrath335Sort!.Value.SectionBoneComboIndex };
        var b = a with { SectionBoneComboIndex = batches[1].wrath335Sort!.Value.SectionBoneComboIndex };
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareOpaque(a, b) < 0);
    }

    [TestMethod]
    [DataRow(-128)]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(127)]
    public void LoadedProfileKeepsSignedPriorityAndSelectedSectionAuthoredInputs(int priority)
    {
        using var root = new M2RootWotlk { GlobalFlags = (GlobalFlags)0x10 };
        using var profile = new M2SkinProfileTbcToWotlk();
        AddSection(profile, 0, Vector3.Zero, 1, 1, 1);
        AddSection(profile, 1, new(3, -4, 12), 7, 500, 321);
        AddBatch(profile, 1, (sbyte)priority, 60000, 0xA3);

        var mesh = Decode(root, profile)[0];
        var metadata = mesh.wrath335Sort!.Value;
        Assert.AreEqual((sbyte)priority, metadata.PriorityPlane);
        Assert.AreEqual((byte)0xA3, metadata.BatchFlags);
        Assert.AreEqual((ushort)60000, metadata.MaterialLayer);
        Assert.AreEqual((ushort)500, metadata.CenterBoneIndex);
        Assert.AreEqual((ushort)321, metadata.SectionBoneComboIndex);
        Assert.AreEqual(new Vector3(3, -4, 12), metadata.SortCenter);
        Assert.AreEqual(7f, metadata.SortRadius);
        Assert.IsTrue(metadata.RawDistance);
        Assert.AreEqual((ushort)1, mesh.geosetId);
        Assert.AreEqual(9u, mesh.numFaces);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0x10, true)]
    [DataRow(0x90, true)]
    [DataRow(0x80, false)]
    public void RawDistanceUsesOnlyItsRootFlag(int flags, bool expected)
    {
        using var root = new M2RootWotlk { GlobalFlags = (GlobalFlags)flags };
        using var profile = Profile();
        Assert.AreEqual(expected, Decode(root, profile)[0].wrath335Sort!.Value.RawDistance);
    }

    [TestMethod]
    public void MetadataIsDetachedFromNativeSourceMutationAndDisposal()
    {
        Submesh mesh;
        using (var root = new M2RootWotlk())
        using (var profile = Profile())
        {
            mesh = Decode(root, profile)[0];
            root.GlobalFlags = (GlobalFlags)0x10;
            profile.Batches[0].PriorityPlane = -128;
            profile.Submeshes[0].SortRadius = 99;
            using var center = new C3Vector(100, 200, 300);
            profile.Submeshes[0].SortCenterPosition = center;
        }
        var metadata = mesh.wrath335Sort!.Value;
        Assert.AreEqual(new Vector3(0, 0, 5), metadata.SortCenter);
        Assert.AreEqual(2f, metadata.SortRadius);
        Assert.AreEqual((sbyte)0, metadata.PriorityPlane);
        Assert.IsFalse(metadata.RawDistance);
        Assert.IsTrue(Build(mesh, Matrix4x4.Identity, [Matrix4x4.Identity], out var inputs));
        Assert.AreEqual(new Wrath335M2DistanceKeys(25, 25), inputs.Distance);
    }

    [TestMethod]
    public void NonMpqWotlkAndOtherClientRootsDoNotPublish335SortMetadata()
    {
        using var profile = Profile();
        using var wrath = new M2RootWotlk();
        using var tbc = new M2RootTbc();
        using var cata = new M2RootCataToMop();
        var cases = new[] { Decode(wrath, profile, false)[0], Decode(tbc, profile)[0], Decode(cata, profile)[0] };
        foreach (var mesh in cases)
        {
            Assert.IsNull(mesh.wrath335Sort);
            Assert.AreEqual(9u, mesh.numFaces);
            Assert.IsFalse(Build(mesh, Matrix4x4.Identity, [], out _));
        }
    }

    [TestMethod]
    public void MissingSectionIsSkippedAndLegacySectionHasNoInventedSortCenter()
    {
        using var root = new M2RootWotlk();
        using var profile = Profile();
        profile.Batches[0].SkinSectionIndex = 5;
        Assert.AreEqual(0, Decode(root, profile).Length);

        using var legacy = new M2SkinProfileVanilla();
        using var section = new M2SkinSectionVanilla { IndexCount = 9 };
        using var batch = new M2Batch { TextureCount = 1 };
        legacy.Submeshes.Add(section);
        legacy.Batches.Add(batch);
        var mesh = Decode(root, legacy)[0];
        Assert.AreEqual(9u, mesh.numFaces);
        Assert.IsNull(mesh.wrath335Sort);
    }

    [TestMethod]
    public void SelectedBonePrecedesNoncommutingModelAndViewTransforms()
    {
        var mesh = Mesh(new(1, 2, 3), bone: 1);
        Matrix4x4[] bones = [Matrix4x4.CreateTranslation(100, 100, 100),
            Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateTranslation(5, 6, 7)];
        // Exact quarter turn around Y, then view translation. Center becomes
        // (7,12,19) in model space and (19,12,3) in view space: 361+144+9.
        var worldToView = new Matrix4x4(0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 10, 1);
        Assert.IsTrue(Build(mesh, worldToView, bones, out var inputs));
        Assert.AreEqual(new Wrath335M2DistanceKeys(514, 514), inputs.Distance);
    }

    [TestMethod]
    [DataRow(1, 49f)]
    [DataRow(2, 225f)]
    [DataRow(3, 49f)]
    public void RadiusUsesTransformedFirstAxisRatherThanMaximumScale(int flags, float expected)
    {
        var mesh = Mesh(new(0, 0, 1), flags: (byte)flags, radius: 2);
        var modelToView = Matrix4x4.CreateTranslation(0, 0, 10);
        Assert.IsTrue(Build(mesh, modelToView, [Matrix4x4.CreateScale(2, 5, 1)], out var inputs));
        // Center z=11, first-axis radius=4. Maximum-axis radius would be 10.
        Assert.AreEqual(new Wrath335M2DistanceKeys(expected, expected), inputs.Distance);
    }

    [TestMethod]
    public void ViewRotationAndModelScaleParticipateInFirstAxisRadius()
    {
        var mesh = Mesh(new(0, 0, 1), flags: 2, radius: 2);
        var view = new Matrix4x4(0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 10, 1);
        var modelToView = Matrix4x4.CreateScale(3, 1, 1) * view;
        Assert.IsTrue(Build(mesh, modelToView, [Matrix4x4.CreateScale(2, 5, 1)], out var inputs));
        // Center (1,0,10), radius 2*2*3=12; squared length=(sqrt(101)+12)^2.
        Assert.AreEqual((float)Math.Pow(Math.Sqrt(101) + 12, 2), inputs.Distance.Secondary, 0.001f);
    }

    [TestMethod]
    public void ModelOriginAndEligibleParentSupplyWholeModelKey()
    {
        var modelToView = Matrix4x4.CreateTranslation(3, 4, -12);
        Assert.AreEqual(169f, Wrath335M2MeshSortInputAdapter.ModelDistance(modelToView));
        Assert.AreEqual(7f, Wrath335M2MeshSortInputAdapter.ModelDistance(modelToView, 7));
        Assert.AreEqual(169f, Wrath335M2MeshSortInputAdapter.ModelDistance(modelToView, 7, true));
    }

    [TestMethod]
    public void RawDistanceStillTransformsCenterButBypassesRadiusAndEyeSide()
    {
        var mesh = Mesh(new(0, 0, -1), flags: 3, radius: float.NaN, raw: true);
        Assert.IsTrue(Build(mesh, Matrix4x4.CreateTranslation(0, 0, -4), [Matrix4x4.Identity], out var inputs));
        Assert.AreEqual(new Wrath335M2DistanceKeys(81, 25), inputs.Distance);
    }

    [TestMethod]
    [DataRow(0, false, 81f)]
    [DataRow(8, false, 81f)]
    [DataRow(16, false, 25f)]
    [DataRow(0, true, 25f)]
    public void ProjectedAndAuthoredNoWriteGateZFillPrimary(int renderFlags, bool projected, float expected)
    {
        var mesh = Mesh(new(0, 0, 5), renderFlags: (ushort)renderFlags);
        Assert.IsTrue(Wrath335M2MeshSortInputAdapter.TryBuild(mesh, Matrix4x4.Identity,
            [Matrix4x4.Identity], 81, true, true, projected, out var inputs));
        Assert.AreEqual(new Wrath335M2DistanceKeys(expected, 25), inputs.Distance);
    }

    [TestMethod]
    public void OpaqueNeedsNoBoneAndTranslucentNeverSubstitutesIdentityForMissingBone()
    {
        var mesh = Mesh(new(0, 0, 5), bone: 500);
        Assert.IsTrue(Wrath335M2MeshSortInputAdapter.TryBuild(mesh, Matrix4x4.Identity, [],
            81, false, true, false, out var opaque));
        Assert.AreEqual(new Wrath335M2DistanceKeys(81, 81), opaque.Distance);
        Assert.IsFalse(Build(mesh, Matrix4x4.Identity, new Matrix4x4[256], out var missing));
        Assert.AreEqual(default(Wrath335M2MeshSortInputs), missing);
        var fullBones = new Matrix4x4[501];
        fullBones[500] = Matrix4x4.CreateTranslation(0, 0, 2);
        Assert.IsTrue(Build(mesh, Matrix4x4.Identity, fullBones, out var valid));
        Assert.AreEqual(new Wrath335M2DistanceKeys(49, 49), valid.Distance);
    }

    [TestMethod]
    public void ChangedPoseAndViewAreReadOnEveryPreparation()
    {
        var mesh = Mesh(new(0, 0, 5));
        Matrix4x4[] bones = [Matrix4x4.Identity];
        Assert.IsTrue(Build(mesh, Matrix4x4.Identity, bones, out var first));
        bones[0] = Matrix4x4.CreateTranslation(0, 0, 2);
        Assert.IsTrue(Build(mesh, Matrix4x4.CreateTranslation(0, 0, -10), bones, out var second));
        Assert.AreEqual(new Wrath335M2DistanceKeys(25, 25), first.Distance);
        Assert.AreEqual(new Wrath335M2DistanceKeys(-9, -9), second.Distance);
    }

    [TestMethod]
    public void DecodedPriorityAndLayerDriveBaseHeapOrderWithoutClamping()
    {
        using var root = new M2RootWotlk();
        using var profile = new M2SkinProfileTbcToWotlk();
        AddSection(profile, 0, new(0, 0, 5), 2, 0, 321);
        AddBatch(profile, 0, 127, 0);
        AddBatch(profile, 0, -128, 60000);
        AddBatch(profile, 0, -128, 40000);
        var meshes = Decode(root, profile);
        var elements = new Wrath335M2ElementSortData[meshes.Length];
        for (var i = 0; i < meshes.Length; i++)
        {
            Assert.IsTrue(Build(meshes[i], Matrix4x4.Identity, [Matrix4x4.Identity], out var inputs));
            elements[i] = new() { Distance = inputs.Distance, PriorityPlane = inputs.PriorityPlane,
                MaterialLayer = inputs.MaterialLayer, SectionBoneComboIndex = inputs.SectionBoneComboIndex };
        }
        int[] indices = [0, 1, 2];
        Wrath335M2ElementOrdering.Sort(indices, elements, true);
        CollectionAssert.AreEqual(new[] { 2, 1, 0 }, indices);
    }

    private static bool Build(Submesh mesh, Matrix4x4 modelToView, ReadOnlySpan<Matrix4x4> bones,
        out Wrath335M2MeshSortInputs inputs) => Wrath335M2MeshSortInputAdapter.TryBuild(
        mesh, modelToView, bones, 81, true, false, false, out inputs);

    private static Submesh Mesh(Vector3 center, byte flags = 0, float radius = 2,
        ushort bone = 0, bool raw = false, ushort renderFlags = 0) => new()
    {
        renderFlags = renderFlags,
        wrath335Sort = new(flags, 0, 0, bone, 1, center, radius, raw)
    };

    private static M2SkinProfileTbcToWotlk Profile()
    {
        var profile = new M2SkinProfileTbcToWotlk();
        AddSection(profile, 0, new(0, 0, 5), 2, 0, 1);
        AddBatch(profile, 0, 0, 0);
        return profile;
    }

    private static void AddSection(M2SkinProfileTbcToWotlk profile, ushort id, Vector3 center,
        float radius, ushort bone, ushort count)
    {
        using var section = new M2SkinSectionTbcPlus { SkinSectionId = id, IndexCount = 9,
            CenterBoneIndex = bone, BoneCount = 1, BoneComboIndex = count, SortRadius = radius };
        using var sortCenter = new C3Vector(center.X, center.Y, center.Z);
        using var averageCenter = new C3Vector(101, 202, 303);
        section.SortCenterPosition = sortCenter;
        section.CenterPosition = averageCenter;
        profile.Submeshes.Add(section);
    }

    private static void AddBatch(M2SkinProfileTbcToWotlk profile, ushort section, sbyte priority,
        ushort layer, byte flags = 0)
    {
        using var batch = new M2Batch { SkinSectionIndex = section, PriorityPlane = priority,
            MaterialLayer = layer, Flags = flags, TextureCount = 1 };
        profile.Batches.Add(batch);
    }

    private static Submesh[] Decode(M2Root root, M2SkinProfile profile, bool isMpq = true) =>
        SharedLoader.ReadSubmeshes(root, profile, [new M2Material()],
            [new SharedLoader.M2RenderMaterial(0, 2)], isMpq);
}
