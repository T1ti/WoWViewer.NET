using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Controls;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

public sealed partial class EditorSettingsSmokeTests
{
    [TestMethod]
    public void M2TextureResolution_PrefersAuthoritativeTxidForEveryTextureType()
    {
        Assert.AreEqual(777u, WoWRenderLib.Loaders.M2Loader.SelectTextureFileDataId(777, 11, 0));
        Assert.AreEqual(888u, WoWRenderLib.Loaders.M2Loader.SelectTextureFileDataId(0, 0, 888));
        Assert.AreEqual(186184u, WoWRenderLib.Loaders.M2Loader.SelectTextureFileDataId(0, 11, 888));
    }

    [TestMethod]
    public void M2RenderMaterial_UsesBatchMaterialFlagsRatherThanTextureFlags()
    {
        M2Loader.M2RenderMaterial[] materials =
        [
            new(0, 0),
            new(4, 1)
        ];

        var leaves = M2Loader.ResolveRenderMaterial(1, materials);
        Assert.AreEqual((ushort)4, leaves.Flags);
        Assert.AreEqual((ushort)1, leaves.BlendMode);
        Assert.IsTrue(SceneManager.IsM2TwoSided(leaves.Flags));
        Assert.IsFalse(SceneManager.IsM2TwoSided(0));
        Assert.AreEqual(128f / 255f, SceneManager.GetAlphaReference(1));
        Assert.AreEqual(-1f, SceneManager.GetAlphaReference(0));
        Assert.AreEqual(0, SceneManager.GetM2SamplerIndex(0));
        Assert.AreEqual(2, SceneManager.GetM2SamplerIndex(1));
        Assert.AreEqual(1, SceneManager.GetM2SamplerIndex(2));
        Assert.AreEqual(3, SceneManager.GetM2SamplerIndex(3));
    }

    [TestMethod]
    public void M2DepthPolicy_UsesWotlkMaterialDepthFlagsWithoutChangingModernState()
    {
        Assert.AreEqual(M2DepthMode.ReadOnly, M2DepthPolicy.ForMaterial(true, 0x14));
        Assert.AreEqual(M2DepthMode.Disabled, M2DepthPolicy.ForMaterial(true, 0x08));
        Assert.AreEqual(M2DepthMode.Default, M2DepthPolicy.ForMaterial(true, 0));
        Assert.AreEqual(M2DepthMode.Default, M2DepthPolicy.ForMaterial(false, 0x14));
    }

    [TestMethod]
    public void WmoMaterialPolicy_UsesLegacyCutoutThresholdAndIndependentClampAxes()
    {
        Assert.AreEqual(224f / 255f, WmoMaterialPolicy.AlphaReference(1, true));
        Assert.AreEqual(1f / 255f, WmoMaterialPolicy.AlphaReference(2, true));
        Assert.AreEqual(1f / 255f, WmoMaterialPolicy.AlphaReference(6, true));
        Assert.AreEqual(0f, WmoMaterialPolicy.AlphaReference(0, true));
        Assert.AreEqual(128f / 255f, WmoMaterialPolicy.AlphaReference(1, false));
        Assert.AreEqual(-1f, WmoMaterialPolicy.AlphaReference(2, false));

        Assert.AreEqual(3, WmoMaterialPolicy.SamplerIndex(0));
        Assert.AreEqual(1, WmoMaterialPolicy.SamplerIndex(0x40));
        Assert.AreEqual(2, WmoMaterialPolicy.SamplerIndex(0x80));
        Assert.AreEqual(0, WmoMaterialPolicy.SamplerIndex(0xC0));
    }

    [TestMethod]
    public void M2Animation_LoopsGlobalTrackAndRotatesAroundBonePivot()
    {
        var translation = new M2Track<Vector3>
        {
            Interpolation = 1,
            GlobalSequence = 0,
            Timelines = [new M2Timeline<Vector3>([0, 1000], [Vector3.Zero, new Vector3(4, 0, 0)])]
        };
        var rotation = new M2Track<Quaternion>
        {
            Interpolation = 0,
            GlobalSequence = -1,
            Timelines = [new M2Timeline<Quaternion>([0],
                [Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)])]
        };
        var scale = new M2Track<Vector3>
        {
            Interpolation = 0,
            GlobalSequence = -1,
            Timelines = []
        };
        var animation = new M2Animation
        {
            Bones = [new M2Bone(-1, 0x200, new Vector3(1, 0, 0), translation, rotation, scale)],
            Sequences = [new M2Sequence(500, 0)],
            GlobalLoops = [1000]
        };

        Span<Matrix4x4> palette = stackalloc Matrix4x4[1];
        animation.Evaluate(0, 250, palette);
        var first = Vector3.Transform(new Vector3(2, 0, 0), palette[0]);
        animation.Evaluate(0, 1250, palette);
        var looped = Vector3.Transform(new Vector3(2, 0, 0), palette[0]);

        Assert.IsTrue(Vector3.Distance(first, new Vector3(2, 1, 0)) < 0.0001f);
        Assert.IsTrue(Vector3.Distance(first, looped) < 0.0001f);
    }

    [TestMethod]
    public void BrushMath_SupportsSquareFootprintsAndOptionalFalloff()
    {
        var circle = new BrushInput(
            10f,
            0.5f,
            false,
            BrushShape.Circle,
            BrushFalloffProfile.Smooth);
        var square = circle with { Shape = BrushShape.Square };

        Assert.AreEqual(0f, BrushMath.CalculateInfluence(8f, 8f, circle));
        Assert.AreEqual(1f, BrushMath.CalculateInfluence(8f, 8f, square));
        Assert.AreEqual(1f, BrushMath.CalculateInfluence(9f, 0f, circle));

        var softened = circle with { HasFalloff = true };
        var edgeInfluence = BrushMath.CalculateInfluence(9f, 0f, softened);
        Assert.IsTrue(edgeInfluence > 0f && edgeInfluence < 1f);
        var linear = softened with { FalloffProfile = BrushFalloffProfile.Linear };
        var gaussian = softened with { FalloffProfile = BrushFalloffProfile.Gaussian };
        Assert.AreNotEqual(
            BrushMath.CalculateInfluence(6.25f, 0f, linear),
            BrushMath.CalculateInfluence(6.25f, 0f, softened));
        Assert.AreNotEqual(
            BrushMath.CalculateInfluence(6.25f, 0f, gaussian),
            BrushMath.CalculateInfluence(6.25f, 0f, softened));
        Assert.AreEqual(288, Marshal.SizeOf<ADTPerObjectCB>(),
            "The managed ADT constant buffer must retain the shader's 16-byte register layout.");
    }

    [TestMethod]
    public void NonlinearSlider_DoesNotOverwriteValueWhileRangeBindingsInitialize()
    {
        var slider = new NonlinearSlider
        {
            Value = 10d,
            Minimum = 1d
        };

        Assert.AreEqual(10d, slider.Value,
            "A temporary range must not write its clamp back through the two-way Value binding.");

        slider.Maximum = 1000d;
        Assert.AreEqual(10d, slider.Value);
    }

}
