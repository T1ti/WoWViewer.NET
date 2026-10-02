using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;
using GroupFlags = WoWLib.Formats.WMO.Group.Chunks.GroupFlags;
using HeaderFlags = WoWLib.Formats.WMO.Root.Chunks.HeaderFlags;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335WmoTransitionTests
{
    [TestMethod]
    public void StormwindFlagsPreserveAuthoredTransitionColorAndAlpha()
    {
        Vector4[] colors = [new(31f / 255, 19f / 255, 7f / 255, 73f / 255),
            new(49f / 255, 27f / 255, 11f / 255, 1)];
        var authored = (Vector4[])colors.Clone();
        const HeaderFlags flags = (HeaderFlags)0xf;
        WMOLoader.LegacyFixColorVertexAlpha(colors, 1, flags);
        Attenuate(colors, [Vector3.Zero], flags, Square, Vector3.UnitZ);
        CollectionAssert.AreEqual(authored, colors);
    }

    [TestMethod]
    public void ColorFixAndAttenuationFlagsControlIndependentStages()
    {
        var authored = new Vector4(128f / 255, 64f / 255, 32f / 255, 73f / 255);
        Vector4[] colors = [authored];
        WMOLoader.LegacyFixColorVertexAlpha(colors, 1, HeaderFlags.DoNotAttenuateVertices);
        Attenuate(colors, [Vector3.Zero], HeaderFlags.DoNotAttenuateVertices, Square, Vector3.UnitZ);
        Assert.AreEqual(new Vector4(64f / 255, 32f / 255, 16f / 255, authored.W), colors[0]);

        colors[0] = authored;
        WMOLoader.LegacyFixColorVertexAlpha(colors, 1, HeaderFlags.DoNotFixVertexColorAlpha);
        Attenuate(colors, [Vector3.Zero], HeaderFlags.DoNotFixVertexColorAlpha, Square, Vector3.UnitZ);
        Assert.AreEqual(new Vector4(127f / 255, 127f / 255, 127f / 255, 1), colors[0]);
    }

    [TestMethod]
    public void ConcavePortalUsesCrossingsRatherThanConvexEdgeSigns()
    {
        Vector3[] polygon = [new(0, 0, 0), new(3, 0, 0), new(3, 1, 0),
            new(1, 1, 0), new(1, 3, 0), new(0, 3, 0)];
        Vector4[] colors = [Vector4.Zero, Vector4.Zero];
        Attenuate(colors, [new(0.5f, 2, 0), new(2, 2, 0)], 0, polygon, Vector3.UnitZ);
        Assert.AreEqual(1f, colors[0].W);
        Assert.AreEqual(216f / 255, colors[1].W); // one unit from the nearest edge
    }

    [TestMethod]
    public void NativeOppositePolygonBoundariesKeepDifferentDistancePaths()
    {
        // Inside uses the signed plane distance (negative is clamped to zero).
        // Outside uses positive distance to a 3D edge, even on the boundary.
        Vector4[] colors = [Vector4.Zero, Vector4.Zero];
        Attenuate(colors, [new(0, 1, -2), new(0, -1, -2)], 0, Square, Vector3.UnitZ);
        Assert.AreEqual(1f, colors[0].W);
        Assert.AreEqual(178f / 255, colors[1].W);
    }

    [TestMethod]
    public void NonUnitPortalNormalProjectsBeforeCrossingTest()
    {
        // Plane z=x. An orthogonal projection falls inside this small portal;
        // subtracting N * distance without dividing by N dot N falls outside.
        Vector3[] polygon = [new(-0.5f, -1, -0.5f), new(0.5f, -1, 0.5f),
            new(0.5f, 1, 0.5f), new(-0.5f, 1, -0.5f)];
        Vector4[] colors = [Vector4.Zero];
        Attenuate(colors, [new(1, 0, -1)], 0, polygon, new(-1, 0, 1));
        Assert.AreEqual(1f, colors[0].W); // signed plane distance -2
    }

    [TestMethod]
    public void NearPlaneToleranceKeepsTheOriginalPointForPolygonCrossing()
    {
        Vector3[] polygon = [new(-0.5f, -1, -0.5f), new(0.5f, -1, 0.5f),
            new(0.5f, 1, 0.5f), new(-0.5f, 1, -0.5f)];
        Vector4[] colors = [Vector4.Zero];
        Attenuate(colors, [new(0.501f, 0, 0.499f)], 0, polygon, new(-1, 0, 1));
        // Within the 0.01 ray/plane tolerance, this stays just outside. The
        // positive edge distance gives alpha 254; projecting would give 255.
        Assert.AreEqual(254f / 255, colors[0].W);
    }

    [TestMethod]
    public void NearbyInteriorPortalOverridesAccumulatedExteriorWeight()
    {
        Vector4[] colors = [new(21f / 255, 11f / 255, 3f / 255, 1)];
        PreppedWMOPortal[] portals = [new() { VertexCount = 4, Normal = Vector3.UnitZ }];
        PreppedWMOPortalReference[] references = [
            new() { PortalIndex = 0, GroupIndex = 0, Side = 1 },
            new() { PortalIndex = 0, GroupIndex = 1, Side = 1 }];
        WMOLoader.AttenuateTransitionColors(colors, [new(0, 0, 0.5f)], 0, 0, 2,
            Square, portals, references, [GroupFlags.Exterior, 0], wrath335: true);
        Assert.AreEqual(new Vector4(21f / 255, 11f / 255, 3f / 255, 0), colors[0]);
    }

    [TestMethod]
    public void TransitionSubmissionRequiresUnifiedPathOrPrimaryVertexColors()
    {
        Assert.AreEqual(2, WmoMaterialPolicy.PassCount(true, true, 0xf, true, 0));
        Assert.AreEqual(2, WmoMaterialPolicy.PassCount(true, true, 0x2, false, 0));
        Assert.AreEqual(2, WmoMaterialPolicy.PassCount(true, true, 0, true, 0));
        Assert.AreEqual(1, WmoMaterialPolicy.PassCount(true, true, 0, false, 0));
        Assert.AreEqual(1, WmoMaterialPolicy.PassCount(true, true, 0x2, true, 1));
        Assert.AreEqual(1, WmoMaterialPolicy.PassCount(false, false, 0x2, true, 0));
        Assert.AreEqual(2, WmoMaterialPolicy.PassCount(true, false, 0, false, 0));
        Assert.AreEqual(0, WmoMaterialPolicy.ResolveLightingMode(true, 0, 0, false, 0, 1, wrath335: true));
        Assert.AreEqual(1, WmoMaterialPolicy.ResolveLightingMode(true, 0, 0, false, 0, 0x20, wrath335: true));
        Assert.AreEqual(1, WmoMaterialPolicy.ResolveLightingMode(true, 0, 0, false, 0, 1, wrath335: false));
        Assert.AreEqual(2, WmoMaterialPolicy.ResolveLightingMode(true, 0, 0, false, 0, 0x20, wrath335: false));
    }

    [TestMethod]
    public void UnifiedOpaqueExteriorForcesStagedFogDespitePropagation()
    {
        Assert.IsFalse(WmoMaterialPolicy.UsesCurrentFogForPass(true, 0x48, 0, 1, 0,
            propagatedFromInterior: true, rootFlags: 0x2, wrath335: true));
        Assert.IsTrue(WmoMaterialPolicy.UsesCurrentFogForPass(true, 0x48, 0, 0, 1,
            propagatedFromInterior: true, rootFlags: 0x2, wrath335: true));
        Assert.IsTrue(WmoMaterialPolicy.UsesCurrentFogForPass(false, 0x48, 0, 1, 0,
            propagatedFromInterior: true, rootFlags: 0x2));
        Assert.IsTrue(WmoMaterialPolicy.UsesCurrentFogForPass(true, 0x48, 0, 1, 0,
            propagatedFromInterior: true, rootFlags: 0x2, wrath335: false));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(4)]
    public unsafe void LiveShaderCombinesTransitionLightingAndFogWithAuthoredAlpha(int pixelShader)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The DX11 WARP fixture requires Windows.");
        using var d3d = D3D11.GetApi();
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        SilkMarshal.ThrowHResult(d3d.CreateDevice(default(ComPtr<IDXGIAdapter>), D3DDriverType.Warp,
            default, 0, null, 0, D3D11.SdkVersion, ref device, null, ref context));
        ComPtr<ID3D11Buffer> vertices = default, instances = default, constantsBuffer = default, fogBuffer = default;
        ComPtr<ID3D11Texture2D> target = default, readback = default, texture = default, depth = default;
        ComPtr<ID3D11RenderTargetView> targetView = default;
        ComPtr<ID3D11DepthStencilView> depthView = default;
        ComPtr<ID3D11DepthStencilState> depthState = default;
        ComPtr<ID3D11ShaderResourceView> textureView = default;
        ComPtr<ID3D11SamplerState> sampler = default;
        ComPtr<ID3D11RasterizerState> rasterizer = default;
        ComPtr<ID3D11BlendState> firstBlend = default, secondBlend = default;
        try
        {
            using var shaders = new ShaderManager(device, Path.Combine(AppContext.BaseDirectory, "Shaders"));
            using var presentation = new OpaquePresentationRenderer(device, context, shaders);
            var shader = shaders.GetOrCompileShader("wmo");
            var mocv = new Vector3(21f / 255, 11f / 255, 3f / 255);
            WMOVertex[] triangle = [
                new() { Position = new(-1, -1, 0.5f), Normal = Vector3.UnitZ, Color = new(mocv, 1) },
                new() { Position = new(0, 1, 0.5f), Normal = Vector3.UnitZ, Color = new(mocv, 1) },
                new() { Position = new(1, -1, 0.5f), Normal = Vector3.UnitZ, Color = new(mocv, 1) }
            ];
            vertices = Buffer(device, triangle, BindFlag.VertexBuffer);
            instances = Buffer(device, [Matrix4x4.Identity], BindFlag.VertexBuffer);
            var constants = new WMOPerObjectCB
            {
                projection_matrix = Matrix4x4.Identity, view_matrix = Matrix4x4.Identity,
                model_matrix = Matrix4x4.Identity, vertexShader = 0, pixelShader = pixelShader,
                lightDirection = Vector3.UnitZ, ambientColor = new(51f / 255, 77f / 255, 102f / 255),
                diffuseColor = new(26f / 255, 38f / 255, 51f / 255),
                rootAmbientColor = new(33f / 255), useLegacyLighting = 2, unifiedMocv = 1,
                alphaRef = 0
            };
            constantsBuffer = Buffer(device, [constants], BindFlag.ConstantBuffer);
            var stagedFog = WorldFogConstants.Create(new(0, 1, 1, 0xff3366cc), true);
            var currentFog = WorldFogConstants.Create(new(0, 1, 1, 0xffcc3319), true);
            fogBuffer = Buffer(device, [stagedFog], BindFlag.ConstantBuffer);
            var description = new Texture2DDesc
            {
                Width = 8, Height = 8, MipLevels = 1, ArraySize = 1,
                Format = Format.FormatR32G32B32A32Float, SampleDesc = new(1, 0),
                Usage = Usage.Default, BindFlags = (uint)BindFlag.RenderTarget
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref target));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(target, null, ref targetView));
            description.Usage = Usage.Staging;
            description.BindFlags = 0;
            description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref readback));
            description.Usage = Usage.Default;
            description.CPUAccessFlags = 0;
            description.Format = Format.FormatD32Float;
            description.BindFlags = (uint)BindFlag.DepthStencil;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref depth));
            SilkMarshal.ThrowHResult(device.CreateDepthStencilView(depth, null, ref depthView));
            var depthDescription = new DepthStencilDesc
            { DepthEnable = true, DepthWriteMask = DepthWriteMask.All, DepthFunc = ComparisonFunc.LessEqual };
            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depthDescription, ref depthState));
            description.Width = description.Height = 1;
            description.Format = Format.FormatR32G32B32A32Float;
            description.BindFlags = (uint)BindFlag.ShaderResource;
            var texel = new Vector4(0.6f, 0.4f, 0.2f, 0.25f);
            var textureData = new SubresourceData { PSysMem = &texel, SysMemPitch = 16 };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, in textureData, ref texture));
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(texture, null, ref textureView));
            var samplerDescription = new SamplerDesc
            {
                Filter = Filter.MinMagMipPoint, AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, MaxLOD = float.MaxValue
            };
            SilkMarshal.ThrowHResult(device.CreateSamplerState(in samplerDescription, ref sampler));
            var rasterizerDescription = new RasterizerDesc
            { FillMode = FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = true };
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rasterizerDescription, ref rasterizer));
            firstBlend = BlendState(Blend.SrcAlpha, Blend.Zero);
            secondBlend = BlendState(Blend.InvSrcAlpha, Blend.One);
            context.RSSetState(rasterizer);
            var viewport = new Viewport { Width = 8, Height = 8, MaxDepth = 1 };
            context.RSSetViewports(1, in viewport);
            context.OMSetRenderTargets(1, ref targetView, depthView);
            context.OMSetDepthStencilState(depthState, 0);
            context.IASetInputLayout(shader.InputLayout);
            context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
            ComPtr<ID3D11ClassInstance> noClass = default;
            context.VSSetShader(shader.VertexShader, ref noClass, 0);
            context.PSSetShader(shader.PixelShader, ref noClass, 0);
            context.VSSetConstantBuffers(0, 1, ref constantsBuffer);
            context.PSSetConstantBuffers(0, 1, ref constantsBuffer);
            context.VSSetConstantBuffers(4, 1, ref fogBuffer);
            context.PSSetConstantBuffers(4, 1, ref fogBuffer);
            context.PSSetShaderResources(0, 1, ref textureView);
            context.PSSetSamplers(0, 1, ref sampler);
            uint vertexStride = (uint)sizeof(WMOVertex), instanceStride = 64, offset = 0;
            context.IASetVertexBuffers(0, 1, ref vertices, in vertexStride, in offset);
            context.IASetVertexBuffers(1, 1, ref instances, in instanceStride, in offset);

            // Includes genuine alpha zero: the first pass writes depth, and the
            // second must still draw at equal depth to supply all interior RGB.
            foreach (var alpha in new[] { 0f, 73f / 255, 254f / 255, 1f })
            {
                foreach (var fogged in new[] { false, true })
                {
                    for (var index = 0; index < triangle.Length; index++)
                        triangle[index].Color.W = alpha;
                    context.UpdateSubresource(vertices, 0, ref Unsafe.NullRef<Box>(), ref triangle[0], 0, 0);
                    var clear = new Vector4(0.7f, 0.9f, 0.1f, 1);
                    context.ClearRenderTargetView(targetView, (float*)&clear);
                    context.ClearDepthStencilView(depthView, (uint)ClearFlag.Depth, 1, 0);
                    constants.unfogged = fogged ? 0 : 1;
                    constants.lightingMode = 1;
                    Draw(firstBlend, stagedFog);
                    constants.lightingMode = 3;
                    Draw(secondBlend, currentFog);
                    context.CopyResource((ID3D11Resource*)readback.Handle, (ID3D11Resource*)target.Handle);

                    // Independently recovered SM3 c28/c10/c11/MOCV formula and
                    // DX9 states 9 then 7; no backdrop contribution survives.
                    var textureRgb = new Vector3(texel.X, texel.Y, texel.Z);
                    var outside = 2 * textureRgb * (mocv + (127f / 255) *
                        (constants.ambientColor + constants.diffuseColor));
                    var inside = 2 * textureRgb * (mocv + (127f / 255) * constants.rootAmbientColor);
                    if (fogged)
                    {
                        outside = (outside + new Vector3(stagedFog.Color.X, stagedFog.Color.Y, stagedFog.Color.Z)) / 2;
                        inside = (inside + new Vector3(currentFog.Color.X, currentFog.Color.Y, currentFog.Color.Z)) / 2;
                    }
                    var weight = alpha * (pixelShader == 0 ? texel.W : 1);
                    var expected = weight * outside + (1 - weight) * inside;
                    MappedSubresource mapped = default;
                    SilkMarshal.ThrowHResult(context.Map((ID3D11Resource*)readback.Handle, 0, Map.Read, 0, ref mapped));
                    try
                    {
                        var pixel = *(Vector4*)((byte*)mapped.PData + 4 * mapped.RowPitch + 4 * sizeof(Vector4));
                        var rgb = new Vector3(pixel.X, pixel.Y, pixel.Z);
                        Assert.IsTrue(Vector3.Distance(expected, rgb) < 0.00002f,
                            $"shader={pixelShader}, alpha={alpha}, fog={fogged}: expected {expected}, got {rgb}");
                        Assert.AreEqual(weight, pixel.W, 0.00002f,
                            "The native transition weight must survive scene blending until presentation.");
                    }
                    finally { context.Unmap((ID3D11Resource*)readback.Handle, 0); }

                    // The external GUI image must be opaque, while preserving
                    // the exact RGB from both native transition passes.
                    presentation.Render(targetView, 8, 8);
                    context.CopyResource((ID3D11Resource*)readback.Handle, (ID3D11Resource*)target.Handle);
                    SilkMarshal.ThrowHResult(context.Map((ID3D11Resource*)readback.Handle, 0, Map.Read, 0, ref mapped));
                    try
                    {
                        var pixel = *(Vector4*)((byte*)mapped.PData + 4 * mapped.RowPitch + 4 * sizeof(Vector4));
                        Assert.IsTrue(Vector3.Distance(expected, new(pixel.X, pixel.Y, pixel.Z)) < 0.00002f);
                        Assert.AreEqual(1f, pixel.W);
                    }
                    finally { context.Unmap((ID3D11Resource*)readback.Handle, 0); }
                    // Restore the geometry pipeline for the next test case.
                    context.OMSetRenderTargets(1, ref targetView, depthView);
                    context.OMSetDepthStencilState(depthState, 0);
                    context.IASetInputLayout(shader.InputLayout);
                    context.VSSetShader(shader.VertexShader, ref noClass, 0);
                    context.PSSetShader(shader.PixelShader, ref noClass, 0);
                }
            }

            void Draw(ComPtr<ID3D11BlendState> blend, WrathFogCB fog)
            {
                context.UpdateSubresource(constantsBuffer, 0, ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
                context.UpdateSubresource(fogBuffer, 0, ref Unsafe.NullRef<Box>(), ref fog, 0, 0);
                float factor = 1;
                context.OMSetBlendState(blend, ref factor, uint.MaxValue);
                context.DrawInstanced(3, 1, 0, 0);
            }

            ComPtr<ID3D11BlendState> BlendState(Blend source, Blend destination)
            {
                var blendDescription = new BlendDesc();
                blendDescription.RenderTarget[0] = new RenderTargetBlendDesc
                {
                    BlendEnable = true, SrcBlend = source, DestBlend = destination, BlendOp = BlendOp.Add,
                    SrcBlendAlpha = source, DestBlendAlpha = destination, BlendOpAlpha = BlendOp.Add,
                    RenderTargetWriteMask = (byte)ColorWriteEnable.All
                };
                ComPtr<ID3D11BlendState> result = default;
                SilkMarshal.ThrowHResult(device.CreateBlendState(in blendDescription, ref result));
                return result;
            }
        }
        finally
        {
            context.ClearState();
            secondBlend.Dispose(); firstBlend.Dispose(); depthState.Dispose(); depthView.Dispose(); depth.Dispose();
            rasterizer.Dispose(); sampler.Dispose(); textureView.Dispose(); targetView.Dispose();
            texture.Dispose(); readback.Dispose(); target.Dispose();
            fogBuffer.Dispose(); constantsBuffer.Dispose(); instances.Dispose(); vertices.Dispose(); context.Dispose(); device.Dispose();
        }
    }

    private static readonly Vector3[] Square = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)];

    private static void Attenuate(Vector4[] colors, Vector3[] positions, HeaderFlags flags,
        Vector3[] polygon, Vector3 normal) =>
        WMOLoader.AttenuateTransitionColors(colors, positions, flags, 0, 1, polygon,
            [new() { VertexCount = (ushort)polygon.Length, Normal = normal }],
            [new() { PortalIndex = 0, GroupIndex = 0, Side = 1 }], [GroupFlags.Exterior], wrath335: true);

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
