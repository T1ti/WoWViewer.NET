using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335WmoDoodadLightingTests
{
    [TestMethod]
    public void InteriorClassificationUsesMogiDespiteExteriorMogpAndMissingMocv()
    {
        var result = Wrath335WmoDoodadLighting.Build([Doodad()], [Group(0, 0x48, 0)]);
        Assert.IsTrue(result[0].Referenced);
        Assert.IsTrue(result[0].Interior);
    }

    [DataTestMethod]
    [DataRow(8u)]
    [DataRow(0x40u)]
    public void ExteriorMogiUsesSunlightDespiteInteriorMogp(uint flags)
    {
        Assert.IsFalse(Wrath335WmoDoodadLighting.Build([Doodad()], [Group(flags, 0, 0)])[0].Interior);
    }

    [DataTestMethod]
    [DataRow((short)2)]
    [DataRow((short)4)]
    [DataRow((short)7)]
    public void RawModdFlagsDoNotReplaceTheRuntimeGroupClassifier(short flags)
    {
        var doodad = Doodad();
        doodad.flags = flags;
        Assert.IsTrue(Wrath335WmoDoodadLighting.Build([doodad], [Group(0, 0, 0)])[0].Interior);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SharedDoodadRetainsExteriorClassificationInEitherGroupArrivalOrder(bool exteriorFirst)
    {
        var interior = Group(0, 0, 0);
        var exterior = Group(8, 0, 0);
        var result = Wrath335WmoDoodadLighting.Build([Doodad()],
            exteriorFirst ? [exterior, interior] : [interior, exterior]);
        Assert.IsTrue(result[0].Referenced);
        Assert.IsFalse(result[0].Interior);
    }

    [TestMethod]
    public void UnreferencedAndInvalidModrEntriesCannotInventAnOwner()
    {
        var result = Wrath335WmoDoodadLighting.Build([Doodad(), Doodad()], [Group(0, 0, 1, 200)]);
        Assert.IsFalse(result[0].Referenced);
        Assert.IsTrue(result[1].Referenced);
    }

    [TestMethod]
    public void DarkModdRaisesDiffuseValueAndRetainsAmbientHue()
    {
        var (ambient, diffuse) = Wrath335WmoDoodadLighting.AdjustLightmap(Color(28, 14, 7));
        AssertColor(new(28, 14, 7), ambient);
        AssertColor(new(112, 56, 28), diffuse);
    }

    [TestMethod]
    public void BlackModdRemainsBlackInsteadOfReceivingAWhiteFloor()
    {
        var (ambient, diffuse) = Wrath335WmoDoodadLighting.AdjustLightmap(Color(0, 0, 0));
        Assert.AreEqual(Vector3.Zero, ambient);
        Assert.AreEqual(Vector3.Zero, diffuse);
    }

    [TestMethod]
    public void BrightModdUsesNativeIntegerAmbientScaleAndKeepsDiffuse()
    {
        var (ambient, diffuse) = Wrath335WmoDoodadLighting.AdjustLightmap(Color(255, 128, 64));
        AssertColor(new(96, 48, 24), ambient);
        AssertColor(new(255, 128, 64), diffuse);
    }

    [TestMethod]
    public void AmbientMultiplierKeepsTheBiasedNearestEvenConversion()
    {
        // 96*255/192 = 127.5; subtract 0.5 then FISTP -> multiplier 127.
        var (ambient, diffuse) = Wrath335WmoDoodadLighting.AdjustLightmap(Color(192, 96, 48));
        AssertColor(new(96, 48, 24), ambient);
        AssertColor(new(192, 96, 48), diffuse);
    }

    [TestMethod]
    public void ModdAlphaIsNeitherOpacityNorAnInteriorLightIndex()
    {
        Assert.AreEqual(Wrath335WmoDoodadLighting.AdjustLightmap(Color(28, 14, 7, 0)),
            Wrath335WmoDoodadLighting.AdjustLightmap(Color(28, 14, 7, 255)));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void NativeLitBlendFamiliesHonorTheUnlitMaterialFlag(int blend)
    {
        Assert.IsTrue(Wrath335WmoDoodadLighting.IsMaterialLit(0, blend));
        Assert.IsFalse(Wrath335WmoDoodadLighting.IsMaterialLit(1, blend));
    }

    [DataTestMethod]
    [DataRow(5)]
    [DataRow(6)]
    public void ModulateMaterialsDoNotMultiplyTheBakedLighting(int blend) =>
        Assert.IsFalse(Wrath335WmoDoodadLighting.IsMaterialLit(0, blend));

    [TestMethod]
    public void SpawnGateRequiresModrAndEnabledSetOnlyFor12340()
    {
        var model = new WorldModel
        {
            wrath335 = true, legacyLighting = true, doodads = [Doodad(), Doodad()],
            doodadsReferencedByGroups = [true, false]
        };
        Assert.IsTrue(SceneManager.IsWmoDoodadSpawnable(model, 0, [true]));
        Assert.IsFalse(SceneManager.IsWmoDoodadSpawnable(model, 1, [true]));
        Assert.IsFalse(SceneManager.IsWmoDoodadSpawnable(model, 0, [false]));
        model.doodads[0].doodadSet = uint.MaxValue;
        Assert.IsFalse(SceneManager.IsWmoDoodadSpawnable(model, 0, [true]));
        model.wrath335 = false;
        Assert.IsTrue(SceneManager.IsWmoDoodadSpawnable(model, 1, [true]));
    }

    [TestMethod]
    public void InstanceStreamSeparatesIndoorSunlitAndStandaloneCopiesOfOneModel()
    {
        const uint fileId = 0xFFF01234;
        var model = new WorldModel
        {
            rootWMOFileDataID = fileId, wrath335 = true, legacyLighting = true,
            doodadLighting = Wrath335WmoDoodadLighting.Build([Doodad(), Doodad()],
                [Group(0, 0, 0), Group(8, 0, 1)])
        };
        {
            var world = Matrix4x4.CreateRotationZ(1f) * Matrix4x4.CreateTranslation(5, 3, 2);
            var indoor = M2InstanceData.ForWmo(model, 0, world);
            Assert.AreEqual(world, indoor.World);
            Assert.AreEqual(1f, indoor.Ambient.W);
            AssertColor(new(96, 48, 24), new(indoor.Ambient.X, indoor.Ambient.Y, indoor.Ambient.Z));
            Assert.AreEqual(Vector3.Normalize(Wrath335WmoDoodadLighting.DirectionToLight),
                new Vector3(indoor.Direction.X, indoor.Direction.Y, indoor.Direction.Z));
            Assert.AreEqual(2f, M2InstanceData.ForWmo(model, 1, world).Ambient.W);
            Assert.AreEqual(0f, M2InstanceData.ForWmo(model, -1, world).Ambient.W);
            model.wrath335 = false;
            Assert.AreEqual(0f, M2InstanceData.ForWmo(model, 0, world).Ambient.W);
        }
    }

    private static WMODoodad Doodad() => new() { filedataid = 1, color = Color(255, 128, 64) };
    private static WorldModelGroupBatches Group(uint mogi, uint mogp, params ushort[] references) =>
        new() { mogiFlags = mogi, flags = mogp, doodadReferences = references };
    private static Vector4 Color(byte r, byte g, byte b, byte a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);
    private static void AssertColor(Vector3 bytes, Vector3 actual) =>
        Assert.IsTrue(Vector3.Distance(bytes / 255f, actual) < 0.00001f, $"Expected {bytes / 255f}, got {actual}");
}
