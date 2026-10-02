using Microsoft.VisualStudio.TestTools.UnitTesting;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Numerics;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335LiquidTests
{
    [TestMethod]
    [DataRow(0, 21, .5f)]
    [DataRow(0, 42, 1f)]
    [DataRow(0, 255, 1f)]
    [DataRow(1, 51, .2f)]
    [DataRow(1, 255, 1f)]
    public void TerrainDepthConsumesTheMaterialRamp(int mode, int depth, float expected)
    {
        var descriptor = Material() with { Wrath335 = new(1, mode, 1, 0, 1, 1000) };
        var source = new WorldLiquidLayerInput
        {
            Width = 1, Height = 1, VertexFormat = WorldLiquidVertexFormat.HeightDepth,
            Depthmap = Enumerable.Repeat((byte)depth, 4).ToArray()
        };
        var native = WorldLiquidMeshBuilder.Build([source], new Catalog(descriptor));
        Assert.AreEqual(expected, native.Vertices[0].Depth, .000001f);
        var otherClient = WorldLiquidMeshBuilder.Build([source], new Catalog(descriptor with { Wrath335 = null }));
        Assert.AreEqual(depth / 255f, otherClient.Vertices[0].Depth);
    }

    [TestMethod]
    public void TerrainUnsignedUvAndWmoSignedUvRemainDistinct()
    {
        var descriptor = Material() with { WmoVertexFormat = 1 };
        var uvs = Enumerable.Repeat(new Vector2(-1, -256), 4).ToArray();
        var terrain = WorldLiquidMeshBuilder.Build([new WorldLiquidLayerInput
        {
            Width = 1, Height = 1, VertexFormat = WorldLiquidVertexFormat.HeightUv, Uvmap = uvs
        }], new Catalog(descriptor));
        var wmo = WmoLiquidMeshBuilder.Build(new WmoLiquidInput
        {
            XTiles = 1, YTiles = 1, XVertices = 2, YVertices = 2, Heights = [0, 0, 0, 0],
            Tiles = [0], AuthoredUvs = uvs, MaterialCount = 1,
            InteriorColor = new(.2f, .3f, .4f, .5f)
        }, new Catalog(descriptor));
        Assert.AreEqual(new Vector2(65535, 65280) * (3f / 256f), terrain.Vertices[0].TexCoord);
        Assert.AreEqual(new Vector2(-1, -256) / 256f, wmo.Vertices[0].TexCoord);
        Assert.AreEqual(.5f, wmo.Materials[0].ShallowColor.W);
    }

    [TestMethod]
    public void GeneratedGradientsRetainNativeRowsColumnSplitAndOceanEndpoint()
    {
        var pixels = new uint[512];
        var shallow = new Vector4(64 / 255f, 128 / 255f, 192 / 255f, 128 / 255f);
        var deep = new Vector4(128 / 255f, 64 / 255f, 0, 192 / 255f);
        Wrath335Liquid.FillGradient(pixels, WorldLiquidWaterType.River, shallow, deep);
        Assert.AreEqual(0x804080c0u, pixels[0]);
        Assert.AreEqual(0xa0606060u, pixels[32 * 8]);
        Assert.AreEqual(0xbf7f4103u, pixels[63 * 8]); // 63/64, never the deep endpoint.
        Wrath335Liquid.FillGradient(pixels, WorldLiquidWaterType.Wmo, shallow, deep);
        Assert.AreEqual(0x80804000u, pixels[0]);
        Assert.AreEqual(0x80ffffffu, pixels[4]);
        Assert.AreEqual(0xbf804000u, pixels[63 * 8]);
        Assert.AreEqual(0xbfffffffu, pixels[63 * 8 + 7]);
        Wrath335Liquid.FillGradient(pixels, WorldLiquidWaterType.Ocean, shallow, deep);
        Assert.AreEqual(0xff723a03u, pixels[63 * 8]); // HSV darken also writes alpha 255.
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++) Wrath335Liquid.FillGradient(pixels, WorldLiquidWaterType.River, shallow, deep);
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    [TestMethod]
    [DataRow(0L, 1200u, 3, 0)]
    [DataRow(400L, 1200u, 3, 1)]
    [DataRow(800L, 1200u, 3, 2)]
    [DataRow(1200L, 1200u, 3, 0)]
    [DataRow(4294967296L, 1200u, 3, 0)]
    [DataRow(900L, 0u, 30, 0)]
    public void NativeTextureClockWrapsAtUnsigned32Bits(long time, uint period, int count, int expected) =>
        Assert.AreEqual(expected, Wrath335Liquid.Frame(time, period, count));

    [TestMethod]
    public void MaterialSelectionAndMagmaScrollUseSeparateNativeContracts()
    {
        Assert.AreEqual(1, Wrath335Liquid.Program(1, false));
        Assert.AreEqual(2, Wrath335Liquid.Program(1, true));
        Assert.AreEqual(3, Wrath335Liquid.Program(2, true));
        Assert.AreEqual(0, Wrath335Liquid.Program(3, true)); // Separate procedural program, still open.
        Assert.AreEqual(.5f, Wrath335Liquid.Scroll(1000, .5f));
        Assert.AreEqual(0, Wrath335Liquid.Scroll(2000, .5f));
        Assert.AreEqual(0, Wrath335Liquid.Scroll(2000, 0));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SpecularUsesPackedSunBandAndFixedIndoorLight(bool interior)
    {
        var result = Wrath335Liquid.Specular(new(.5f, .8f, .1f), interior);
        Assert.AreEqual(interior ? Vector3.Zero : new Vector3(128, 204, 26) / 255f, result);
    }

    [TestMethod]
    [DataRow(1, false, false, 0f, false, false)]
    [DataRow(2, false, false, 0f, false, false)]
    [DataRow(1, true, false, 0f, false, false)]
    [DataRow(2, true, false, 0f, false, false)]
    [DataRow(3, true, false, 0f, false, false)]
    [DataRow(1, false, true, 0f, false, false)]
    [DataRow(3, false, true, 0f, false, false)]
    [DataRow(1, false, false, .5f, false, false)]
    [DataRow(1, false, false, 1f, false, false)]
    [DataRow(1, false, false, 0f, true, false)]
    [DataRow(2, false, false, 0f, false, true)]
    [DataRow(0, false, false, 0f, false, false)]
    [DataRow(0, false, true, 0f, false, false)]
    public unsafe void ProductionSubmissionMatchesBasicSm3PixelsAndRestoresState(int program, bool interior, bool fogEnabled,
        float depth, bool updatePalette, bool highlight)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("DX11 WARP requires Windows.");
        using var d3d = D3D11.GetApi();
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        SilkMarshal.ThrowHResult(d3d.CreateDevice(default(ComPtr<IDXGIAdapter>), D3DDriverType.Warp,
            default, 0, null, 0, D3D11.SdkVersion, ref device, null, ref context));
        ComPtr<ID3D11Texture2D> target = default, readback = default, waveTexture = default;
        ComPtr<ID3D11RenderTargetView> targetView = default;
        ComPtr<ID3D11ShaderResourceView> waveView = default;
        ComPtr<ID3D11Buffer> vertexBuffer = default, indexBuffer = default, fogBuffer = default;
        try
        {
            using var shaders = new ShaderManager(device, Path.Combine(AppContext.BaseDirectory, "Shaders"));
            using var renderer = new WorldLiquidRenderer(device, context);
            renderer.Initialize(shaders);
            using var gradients = new Wrath335LiquidTextures(device, context);
            var lighting = WorldLightingSettings.Defaults with
            {
                HasLiquidColorData = true, HasLiquidAlphaData = true,
                RiverFarColor = new(64 / 255f, 128 / 255f, 192 / 255f),
                WaterShallowAlpha = 128 / 255f, WaterDeepAlpha = 192 / 255f
            };
            gradients.Update(lighting);
            var gradient = gradients.Get(WorldLiquidWaterType.Wmo);
            gradients.Update(lighting);
            Assert.IsTrue(gradient.Handle == gradients.Get(WorldLiquidWaterType.Wmo).Handle);
            if (updatePalette)
            {
                lighting = lighting with { RiverFarColor = new(128 / 255f, 64 / 255f, 32 / 255f) };
                gradients.Update(lighting);
                Assert.IsTrue(gradient.Handle == gradients.Get(WorldLiquidWaterType.Wmo).Handle);
            }
            var wave = new Vector4(.1f, .2f, .3f, .8f);
            var description = new Texture2DDesc
            {
                Width = 1, Height = 1, MipLevels = 1, ArraySize = 1,
                Format = Format.FormatR32G32B32A32Float, SampleDesc = new(1, 0),
                Usage = Usage.Default, BindFlags = (uint)BindFlag.ShaderResource
            };
            var initial = new SubresourceData { PSysMem = &wave, SysMemPitch = 16 };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, in initial, ref waveTexture));
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(waveTexture, null, ref waveView));
            description.Width = description.Height = 16;
            description.BindFlags = (uint)BindFlag.RenderTarget;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref target));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(target, null, ref targetView));
            description.Usage = Usage.Staging; description.BindFlags = 0; description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref readback));
            WorldLiquidVertex[] vertices = [
                new() { Position = new(-1, -1, .5f), Depth = depth },
                new() { Position = new(-1, 3, .5f), Depth = depth },
                new() { Position = new(3, -1, .5f), Depth = depth }
            ];
            vertexBuffer = Buffer(device, vertices, BindFlag.VertexBuffer);
            indexBuffer = Buffer<uint>(device, [0, 1, 2], BindFlag.IndexBuffer);
            var fog = new WrathFogCB { Parameters = new(0, .5f, 1, fogEnabled ? 1 : 0), Color = new(.2f, .4f, .6f, 1) };
            fogBuffer = Buffer(device, [fog], BindFlag.ConstantBuffer);
            context.VSSetConstantBuffers(4, 1, ref fogBuffer);
            context.PSSetConstantBuffers(4, 1, ref fogBuffer);
            var stride = 32u; var offset = 0u;
            context.IASetVertexBuffers(0, 1, ref vertexBuffer, in stride, in offset);
            context.IASetIndexBuffer(indexBuffer, Format.FormatR32Uint, 0);
            var viewport = new Viewport { Width = 16, Height = 16, MaxDepth = 1 };
            context.RSSetViewports(1, in viewport);
            context.OMSetRenderTargets(1, ref targetView, default(ComPtr<ID3D11DepthStencilView>));
            var clear = new Vector4(.05f, .1f, .15f, 0);
            context.ClearRenderTargetView(targetView, (float*)&clear);
            var color = new Vector4(.5f, .75f, .25f, .4f);
            var cb = new WorldLiquidPerObjectCB
            {
                Model = Matrix4x4.Identity, View = Matrix4x4.Identity, Projection = Matrix4x4.Identity,
                NativeParameters = new(program, 1, 0, 1), NativeOffset = new(0, 0, interior ? 1 : 0, 0),
                NativeVertexColor = color, NativeSpecular = highlight ? new(.9f, .7f, .5f, 6) : new(0, 0, 0, 6),
                ShallowColor = color, FamilyParameters = new(0, 0, 1, 1), WmoParameters = new(2, 0, 1, 1),
                LightDirection = new(0, 0, highlight ? 1 : -1, 0), LightingAmbient = new(.5f, .25f, .75f, 0),
                LightingDiffuse = Vector4.One, LiquidColorParameters = new(0, 0, 0, interior ? 1 : 0)
            };
            renderer.BeginSubmission();
            renderer.SubmitBatch(cb, 3, 0, program == 3, waveView, gradient);
            renderer.EndSubmission();

            var tint = interior ? Vector3.One : lighting.RiverFarColor;
            var primary = new Vector3(color.X, color.Y, color.Z) * (interior ? Vector3.One : new Vector3(.5f, .25f, .75f));
            if (highlight) primary += new Vector3(color.X, color.Y, color.Z);
            var rgb = program is 0 or 3 ? new Vector3(color.X * wave.X, color.Y * wave.Y, color.Z * wave.Z) :
                primary * tint + new Vector3(wave.X, wave.Y, wave.Z);
            if (program == 2)
                rgb += new Vector3(.25f * wave.W);
            if (highlight)
            {
                // Exponent-six Blinn terms at the three authored vertices;
                // the center pixel has half its weight on the first vertex.
                var specular = .5f * (1f / 27f + MathF.Pow((1f - .5f / MathF.Sqrt(10.25f)) / 2f, 3));
                rgb += new Vector3(.9f, .7f, .5f) * (wave.W * specular);
            }
            var alpha = program is 0 or 3 ? 1 : color.W * (depth == 1 ? 191 / 255f : depth == .5f ? 159.5f / 255f : 128 / 255f);
            if (fogEnabled) rgb = Vector3.Lerp(new(.2f, .4f, .6f), rgb, .5f);
            var expected = program is 0 or 3 ? new Vector4(rgb, 1) :
                new Vector4(rgb * alpha + new Vector3(clear.X, clear.Y, clear.Z) * (1 - alpha), alpha);
            context.CopyResource((ID3D11Resource*)readback.Handle, (ID3D11Resource*)target.Handle);
            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(context.Map((ID3D11Resource*)readback.Handle, 0, Map.Read, 0, ref mapped));
            try
            {
                var pixel = *(Vector4*)((byte*)mapped.PData + 8 * mapped.RowPitch + 8 * sizeof(Vector4));
                Assert.IsTrue(Vector4.Distance(expected, pixel) < .00002f, $"Expected {expected}, got {pixel}");
            }
            finally { context.Unmap((ID3D11Resource*)readback.Handle, 0); }
            ComPtr<ID3D11ShaderResourceView> actualSrv = default;
            ComPtr<ID3D11DepthStencilState> actualDepth = default;
            ComPtr<ID3D11RasterizerState> actualRasterizer = default;
            uint stencil = 0;
            context.PSGetShaderResources(1, 1, ref actualSrv);
            context.OMGetDepthStencilState(ref actualDepth, ref stencil);
            context.RSGetState(ref actualRasterizer);
            try
            {
                Assert.IsTrue(actualSrv.Handle == null && actualDepth.Handle == null && actualRasterizer.Handle == null);
            }
            finally { actualSrv.Dispose(); actualDepth.Dispose(); actualRasterizer.Dispose(); }
        }
        finally
        {
            context.ClearState(); fogBuffer.Dispose(); indexBuffer.Dispose(); vertexBuffer.Dispose();
            waveView.Dispose(); targetView.Dispose(); waveTexture.Dispose(); readback.Dispose(); target.Dispose();
            context.Dispose(); device.Dispose();
        }
    }

    private static WorldLiquidMaterialDescriptor Material() => new(new(13, 0), WorldLiquidMaterialFamily.Water,
        Vector4.One, Vector4.One, 1, 0, 0, []) { Wrath335 = new(1, 0, 1, 0, 1, 1000) };

    private sealed class Catalog(WorldLiquidMaterialDescriptor descriptor) : IWorldLiquidMaterialCatalog
    {
        public WorldLiquidMaterialDescriptor Resolve(ushort type, ushort format) => descriptor;
    }

    private static unsafe ComPtr<ID3D11Buffer> Buffer<T>(ComPtr<ID3D11Device> device, T[] values, BindFlag bind) where T : unmanaged
    {
        var description = new BufferDesc { ByteWidth = (uint)(sizeof(T) * values.Length), Usage = Usage.Default, BindFlags = (uint)bind };
        ComPtr<ID3D11Buffer> result = default;
        fixed (T* source = values)
        {
            var data = new SubresourceData { PSysMem = source };
            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, in data, ref result));
        }
        return result;
    }
}
