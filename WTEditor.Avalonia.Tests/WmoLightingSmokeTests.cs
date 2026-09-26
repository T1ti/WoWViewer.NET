using System.Numerics;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class WmoLightingSmokeTests
{
    [TestMethod]
    public void PrimaryMocvKeepsBlackAndReadsBgraAndSecondLayer()
    {
        // Two MOCV chunks, two vertices each. The first vertex is genuinely
        // black and must not be treated as a missing-color neutral fill.
        byte[] groupBytes = [
            (byte)'V', (byte)'C', (byte)'O', (byte)'M', 8, 0, 0, 0,
            0, 0, 0, 255, 4, 8, 16, 128,
            (byte)'V', (byte)'C', (byte)'O', (byte)'M', 8, 0, 0, 0,
            30, 20, 10, 255, 0, 0, 0, 0
        ];

        var colors = WMOLoader.ReadVertexColorChunks(groupBytes, 2);

        Assert.AreEqual(new Vector4(0f, 0f, 0f, 1f), colors[0][0]);
        Assert.AreEqual(new Vector4(16f / 255f, 8f / 255f, 4f / 255f, 128f / 255f), colors[0][1]);
        Assert.AreEqual(new Vector4(10f / 255f, 20f / 255f, 30f / 255f, 1f), colors[1][0]);
    }

    [TestMethod]
    public void GroupChunksAfterTwoBytePayloadStillLoadVertexColorsAndUvs()
    {
        // MOPY with one triangle occupies two bytes, so the following MOCV
        // header is not on a four-byte boundary inside MOGP.
        var bytes = new byte[8 + 68 + 10 + 12 + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0x4D4F4750); // PGOM
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), bytes.Length - 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(76, 4), 0x4D4F5059); // YPOM
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(80, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(86, 4), 0x4D4F4356); // VCOM
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(90, 4), 4);
        bytes[94] = 12; bytes[95] = 34; bytes[96] = 56; bytes[97] = 128;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(98, 4), 0x4D4F5456); // VTOM
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(102, 4), 8);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(106, 4), 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(110, 4), 0.75f);

        var colors = WMOLoader.ReadVertexColorChunks(bytes, 1);
        var uvs = WMOLoader.ReadTextureCoordinateChunks(bytes, 1);
        Assert.AreEqual(new Vector4(56f / 255f, 34f / 255f, 12f / 255f, 128f / 255f), colors[0][0]);
        Assert.AreEqual(new Vector2(0.25f, 0.75f), uvs[0][0]);
    }

    [TestMethod]
    public void MissingLegacySecondUvStreamUsesPrimaryCoordinates()
    {
        Vector2[][] sets = [[new Vector2(0.2f, 0.8f)], null!, null!, null!];
        var first = WMOLoader.GetTextureCoordinate(sets, 0, 0);
        Assert.AreEqual(first, WMOLoader.GetTextureCoordinate(sets, 1, 0, first));
    }

    [TestMethod]
    public void ColorChunkMayContainMoreColorsThanTheVertexStream()
    {
        byte[] chunk = [
            (byte)'V', (byte)'C', (byte)'O', (byte)'M', 8, 0, 0, 0,
            3, 2, 1, 255, 9, 8, 7, 255
        ];
        var colors = WMOLoader.ReadVertexColorChunks(chunk, 1);
        Assert.AreEqual(new Vector4(1f / 255f, 2f / 255f, 3f / 255f, 1f), colors[0][0]);
    }

    [TestMethod]
    public void LegacyMocvFixUsesTransitionBoundaryAndRootFlag()
    {
        var colors = new[] { new Vector4(128f / 255f, 64f / 255f, 32f / 255f, 0.5f),
            new Vector4(128f / 255f, 64f / 255f, 32f / 255f, 1f) };
        WMOLoader.LegacyFixColorVertexAlpha(colors, 1, 0);

        Assert.AreEqual(64f / 255f, colors[0].X);
        Assert.AreEqual(0.5f, colors[0].W);
        Assert.AreEqual(1f, colors[1].W);
        Assert.AreEqual(1f, colors[1].X); // 128 + (255 * 128 >> 6), then half, clamped

        var untouched = new[] { Vector4.One };
        WMOLoader.LegacyFixColorVertexAlpha(untouched, 0, 0x8);
        Assert.AreEqual(Vector4.One, untouched[0]);
    }

    [TestMethod]
    public void LegacyLightingSelectsUnlitWindowExteriorAndFlatBanks()
    {
        Assert.AreEqual(0, WmoMaterialPolicy.ResolveLightingMode(true, 0, 0, true, 1, 0));
        Assert.AreEqual(1, WmoMaterialPolicy.ResolveLightingMode(true, 0, 0, false, 1, 0));
        Assert.AreEqual(2, WmoMaterialPolicy.ResolveLightingMode(true, 0, 0, true, 2, 0x20));
        Assert.AreEqual(3, WmoMaterialPolicy.ResolveLightingMode(true, 0x2, 0, true, 1, 0));
        Assert.AreEqual(1, WmoMaterialPolicy.ResolveLightingMode(true, 0x2, 0x48, true, 1, 0));
        Assert.AreEqual(0, WmoMaterialPolicy.ResolveLightingMode(true, 0x2, 0x48, true, 1, 0x1));
        Assert.AreEqual(2, WmoMaterialPolicy.ResolveLightingMode(true, 0x2, 0, true, 1, 0x20));
        Assert.AreEqual(-1, WmoMaterialPolicy.ResolveLightingMode(false, 0, 0, true, 1, 0));
    }

    [TestMethod]
    public void LegacyMissingSecondTextureUsesOpaqueShaderOnlyForTwoStageFamilies()
    {
        Assert.AreEqual(4, WmoMaterialPolicy.ResolveShader(true, 3, false));
        Assert.AreEqual(4, WmoMaterialPolicy.ResolveShader(true, 5, false));
        Assert.AreEqual(4, WmoMaterialPolicy.ResolveShader(true, 6, false));
        Assert.AreEqual(6, WmoMaterialPolicy.ResolveShader(true, 6, true));
        Assert.AreEqual(6, WmoMaterialPolicy.ResolveShader(false, 6, false));
    }

    [TestMethod]
    public void WmoCpuBuffersMatchTheShaderInputAndConstantBufferLayout()
    {
        Assert.AreEqual(104, Marshal.SizeOf<WMOVertex>());
        Assert.AreEqual(24, (int)Marshal.OffsetOf<WMOVertex>(nameof(WMOVertex.TexCoord)));
        Assert.AreEqual(56, (int)Marshal.OffsetOf<WMOVertex>(nameof(WMOVertex.Color)));
        Assert.AreEqual(72, (int)Marshal.OffsetOf<WMOVertex>(nameof(WMOVertex.Color2)));
        Assert.AreEqual(88, (int)Marshal.OffsetOf<WMOVertex>(nameof(WMOVertex.Color3)));

        Assert.AreEqual(336, Marshal.SizeOf<WMOPerObjectCB>());
        Assert.AreEqual(208, (int)Marshal.OffsetOf<WMOPerObjectCB>(nameof(WMOPerObjectCB.lightDirection)));
        Assert.AreEqual(256, (int)Marshal.OffsetOf<WMOPerObjectCB>(nameof(WMOPerObjectCB.sidnColor)));
        Assert.AreEqual(284, (int)Marshal.OffsetOf<WMOPerObjectCB>(nameof(WMOPerObjectCB.lightingMode)));
        Assert.AreEqual(320, (int)Marshal.OffsetOf<WMOPerObjectCB>(nameof(WMOPerObjectCB.windowDiffuseColor)));
    }

    [TestMethod]
    public void WindowBankConvertsThroughClientByteMidpoint()
    {
        var (ambient, diffuse) = WmoMaterialPolicy.WindowLighting(Vector3.Zero, Vector3.One);
        Assert.AreEqual(new Vector3(143f / 255f), ambient);
        Assert.AreEqual(new Vector3(127f / 255f), diffuse);
    }

    [TestMethod]
    public void TransitionPortalWeightChangesVertexAlphaGradually()
    {
        var colors = new[] { Vector4.Zero, Vector4.Zero, Vector4.Zero };
        Vector3[] positions = [new(0, 0, 0), new(0, 0, 4), new(0, 0, 8)];
        Vector3[] polygon = [new(-1, -1, 0), new(1, -1, 0),
            new(1, 1, 0), new(-1, 1, 0)];
        var portals = new[] { new PreppedWMOPortal
        {
            StartVertex = 0, VertexCount = 4, Normal = Vector3.UnitZ
        } };
        var references = new[] { new PreppedWMOPortalReference
        {
            PortalIndex = 0, GroupIndex = 0, Side = 1
        } };

        WMOLoader.AttenuateTransitionColors(colors, positions, 0, 0, 1,
            polygon, portals, references, [0x48u]);

        Assert.AreEqual(1f, colors[0].W);
        Assert.AreEqual(101f / 255f, colors[1].W);
        Assert.AreEqual(0f, colors[2].W);
        Assert.AreEqual(127f / 255f, colors[0].X);
        Assert.AreEqual(50f / 255f, colors[1].X);

        colors.AsSpan().Clear();
        WMOLoader.AttenuateTransitionColors(colors, positions, 0x1, 0, 1,
            polygon, portals, references, [0x48u]);
        Assert.AreEqual(Vector4.Zero, colors[0]);
    }

    [TestMethod]
    public void SpecularFallsWithDaylightAndUsesTheSunTint()
    {
        var sky = WorldSkyLighting.None with
        {
            SunColor = new Vector3(0.25f, 0.5f, 0.75f),
            HasSunCloudData = true
        };
        Assert.AreEqual(sky.SunColor, WorldLightingCatalog.ResolveWmoSpecularColor(sky, 1440));
        Assert.AreEqual(sky.SunColor * 0.5f,
            WorldLightingCatalog.ResolveWmoSpecularColor(sky, 780));
        Assert.AreEqual(Vector3.Zero, WorldLightingCatalog.ResolveWmoSpecularColor(sky, 0));
        Assert.AreEqual(Vector3.Zero,
            WorldLightingCatalog.ResolveWmoSpecularColor(WorldSkyLighting.None, 0));
    }
}
