using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335WmoDoodadRenderingTests
{
    [TestMethod]
    public unsafe void MixedInstancesRenderBakedSunlitAndUnlitPixelsThroughTheLiveM2Shader()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The DX11 WARP fixture requires Windows.");
        using var d3d = D3D11.GetApi();
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        SilkMarshal.ThrowHResult(d3d.CreateDevice(default(ComPtr<IDXGIAdapter>), D3DDriverType.Warp,
            default, 0, null, 0, D3D11.SdkVersion, ref device, null, ref context));
        ComPtr<ID3D11Buffer> vertices = default, instances = default, constantsBuffer = default, fogBuffer = default;
        ComPtr<ID3D11Texture2D> target = default, readback = default, texture = default;
        ComPtr<ID3D11RenderTargetView> targetView = default;
        ComPtr<ID3D11ShaderResourceView> textureView = default;
        ComPtr<ID3D11SamplerState> sampler = default;
        ComPtr<ID3D11RasterizerState> rasterizer = default;
        ComPtr<ID3D11BlendState> fadeBlend = default, opaqueBlend = default;
        ComPtr<ID3D11Texture2D> depthTexture = default;
        ComPtr<ID3D11DepthStencilView> depthView = default;
        try
        {
            using var shaders = new ShaderManager(device, Path.Combine(AppContext.BaseDirectory, "Shaders"));
            var shader = shaders.GetOrCompileShader("m2");
            M2Vertex[] triangle = [
                new() { Position = new(-0.4f, -0.8f, 0.5f), Normal = Vector3.UnitZ },
                new() { Position = new(0, 0.8f, 0.5f), Normal = Vector3.UnitZ },
                new() { Position = new(0.4f, -0.8f, 0.5f), Normal = Vector3.UnitZ }
            ];
            vertices = Buffer(device, triangle, BindFlag.VertexBuffer);
            var model = new WorldModel
            {
                wrath335 = true, legacyLighting = true,
                doodadLighting = Wrath335WmoDoodadLighting.Build(
                    [new() { color = new(28f / 255, 14f / 255, 7f / 255, 1) }, default],
                    [new() { mogiFlags = 0, doodadReferences = [0] },
                     new() { mogiFlags = 8, doodadReferences = [1] }])
            };
            M2InstanceData[] data = [
                M2InstanceData.ForWmo(model, 0, Matrix4x4.CreateTranslation(-0.5f, 0, 0)),
                M2InstanceData.ForWmo(model, 1, Matrix4x4.CreateTranslation(0.5f, 0, 0))
            ];
            instances = Buffer(device, data, BindFlag.VertexBuffer);
            var constants = new M2PerObjectCB
            {
                projection_matrix = Matrix4x4.Identity, view_matrix = Matrix4x4.Identity,
                model_matrix = Matrix4x4.Identity, materialColor = Vector4.One,
                lightDirection = Vector3.UnitZ, ambientColor = new(0.1f, 0.2f, 0.3f),
                diffuseColor = Vector3.Zero, globalOpacity = 1, fogMode = 1, doodadMaterialLit = 1,
                alphaRef = -1
            };
            constantsBuffer = Buffer(device, [constants], BindFlag.ConstantBuffer);
            var sceneFog = WorldFogConstants.Create(new(0, 1, 1, 0xFF00FF00), true);
            fogBuffer = Buffer(device, [sceneFog], BindFlag.ConstantBuffer);
            var targetDescription = new Texture2DDesc
            {
                Width = 16, Height = 8, MipLevels = 1, ArraySize = 1,
                Format = Format.FormatR32G32B32A32Float, SampleDesc = new(1, 0),
                Usage = Usage.Default, BindFlags = (uint)BindFlag.RenderTarget
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in targetDescription, null, ref target));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(target, null, ref targetView));
            targetDescription.Usage = Usage.Staging;
            targetDescription.BindFlags = 0;
            targetDescription.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in targetDescription, null, ref readback));
            targetDescription.Width = targetDescription.Height = 1;
            targetDescription.Usage = Usage.Default;
            targetDescription.BindFlags = (uint)BindFlag.ShaderResource;
            targetDescription.CPUAccessFlags = 0;
            var white = Vector4.One;
            var textureData = new SubresourceData { PSysMem = &white, SysMemPitch = 16 };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in targetDescription, in textureData, ref texture));
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
            context.RSSetState(rasterizer);
            var viewport = new Viewport { Width = 16, Height = 8, MaxDepth = 1 };
            context.RSSetViewports(1, in viewport);
            context.OMSetRenderTargets(1, ref targetView, default(ComPtr<ID3D11DepthStencilView>));
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
            uint vertexStride = (uint)sizeof(M2Vertex), instanceStride = (uint)sizeof(M2InstanceData), offset = 0;
            context.IASetVertexBuffers(0, 1, ref vertices, in vertexStride, in offset);
            context.IASetVertexBuffers(1, 1, ref instances, in instanceStride, in offset);

            Draw();
            var direction = Vector3.Normalize(Wrath335WmoDoodadLighting.DirectionToLight);
            ExpectPixel(4, new((28f + 112f * direction.Z) / 255,
                (14f + 56f * direction.Z) / 255, (7f + 28f * direction.Z) / 255, 1));
            ExpectPixel(12, new(0.1f, 0.2f, 0.3f, 1));

            // The material lit gate applies independently of either instance's light bank.
            constants.doodadMaterialLit = 0;
            context.UpdateSubresource(constantsBuffer, 0, ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
            Draw();
            ExpectPixel(4, Vector4.One);
            ExpectPixel(12, Vector4.One);

            // Standalone/later-client/sky models use a zero-filled lighting extension.
            data[0] = new() { World = data[0].World };
            data[1] = new() { World = data[1].World };
            context.UpdateSubresource(instances, 0, ref Unsafe.NullRef<Box>(), ref data[0], 0, 0);
            Draw();
            ExpectPixel(4, new(0.1f, 0.2f, 0.3f, 1));
            ExpectPixel(12, new(0.1f, 0.2f, 0.3f, 1));

            // Independent banks in one draw must not inherit the global green bank.
            var currentFog = WorldFogConstants.Create(new(0, 1, 1, 0xFFCC3319), true);
            var stagedFog = WorldFogConstants.Create(new(0, 1, 1, 0xFF3366CC), true);
            data[0].SetFog(currentFog);
            data[1].SetFog(stagedFog);
            context.UpdateSubresource(instances, 0, ref Unsafe.NullRef<Box>(), ref data[0], 0, 0);
            constants.fogMode = 0;
            context.UpdateSubresource(constantsBuffer, 0, ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
            Draw();
            var lit = new Vector4(0.1f, 0.2f, 0.3f, 1);
            ExpectPixel(4, (currentFog.Color + lit) * 0.5f);
            ExpectPixel(12, (stagedFog.Color + lit) * 0.5f);
            // Material black/white/gray fog overrides either selected color.
            foreach (var (mode, color) in new[] { (2, 0f), (3, 1f), (4, 128f / 255) })
            {
                constants.fogMode = mode;
                context.UpdateSubresource(constantsBuffer, 0, ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
                Draw();
                var expected = (new Vector4(color, color, color, 1) + lit) * 0.5f;
                ExpectPixel(4, expected);
                ExpectPixel(12, expected);
            }
            // Disabled per-instance fog is explicit, rather than a fallback to scene fog.
            data[0].SetFog(WorldFogConstants.Create(new(0, 1, 1, 0xFFCC3319), false));
            data[1].SetFog(WorldFogConstants.Create(new(0, 1, 1, 0xFF3366CC), false));
            context.UpdateSubresource(instances, 0, ref Unsafe.NullRef<Box>(), ref data[0], 0, 0);
            constants.fogMode = 0;
            context.UpdateSubresource(constantsBuffer, 0, ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
            Draw();
            ExpectPixel(4, lit);
            ExpectPixel(12, lit);
            // Clearing the extension restores the shared scene bank on the next draw.
            data[0] = new() { World = data[0].World };
            data[1] = new() { World = data[1].World };
            context.UpdateSubresource(instances, 0, ref Unsafe.NullRef<Box>(), ref data[0], 0, 0);
            Draw();
            ExpectPixel(4, (sceneFog.Color + lit) * 0.5f);
            ExpectPixel(12, (sceneFog.Color + lit) * 0.5f);

            // Use the live renderer's blend descriptions and instance ABI for distance fades.
            var fadeDescription = SceneBlendPolicy.Description(2);
            var opaqueDescription = SceneBlendPolicy.Description(0);
            SilkMarshal.ThrowHResult(device.CreateBlendState(in fadeDescription, ref fadeBlend));
            SilkMarshal.ThrowHResult(device.CreateBlendState(in opaqueDescription, ref opaqueBlend));
            float blendFactor = 1;
            context.OMSetBlendState(fadeBlend, ref blendFactor, uint.MaxValue);
            var background = new Vector4(0.2f, 0.4f, 0.6f, 1);
            data[0] = M2InstanceData.ForWmo(model, 0, data[0].World);
            data[1] = M2InstanceData.ForWmo(model, 1, data[1].World);
            data[0].RenderParameters = new(0.25f, 1, 0, 0);
            data[1].RenderParameters = new(0.5f, 1, 0, 0);
            constants.fogMode = 1;
            constants.blendMode = 0;
            constants.pixelShader = 0;
            constants.materialColor = Vector4.One;
            constants.alphaRef = -1;
            UploadFade();
            Draw(background);
            ExpectPixel(4, BlendPixel(0.25f));
            ExpectPixel(12, BlendPixel(0.5f));

            // Animated material alpha composes with model fade; neither scales RGB twice.
            constants.blendMode = 2;
            constants.pixelShader = 1;
            constants.materialColor.W = 0.4f;
            constants.alphaRef = 1f / 255;
            UploadFade();
            Draw(background);
            ExpectPixel(4, BlendPixel(0.1f));
            ExpectPixel(12, BlendPixel(0.2f));

            // Native cutout compares texture*composed opacity with the scaled reference.
            data[0].RenderParameters.X = data[1].RenderParameters.X = 0.5f;
            constants.materialColor = Vector4.One;
            constants.blendMode = 1;
            constants.alphaRef = Wrath335M2FadeMaterial.Resolve(1, true, 0, 1, true, 0.5f).AlphaReference;
            white.W = 0.87f;
            context.UpdateSubresource(texture, 0, ref Unsafe.NullRef<Box>(), ref white, 16, 0);
            UploadFade();
            Draw(background);
            ExpectPixel(4, background); ExpectPixel(12, background);
            white.W = 0.9f;
            context.UpdateSubresource(texture, 0, ref Unsafe.NullRef<Box>(), ref white, 16, 0);
            Draw(background);
            ExpectPixel(4, BlendPixel(0.45f)); ExpectPixel(12, BlendPixel(0.45f));

            // Switching back to a full-opacity native cutout uses 224/255 and disables blending.
            data[0].RenderParameters.X = data[1].RenderParameters.X = 1;
            constants.alphaRef = Wrath335M2FadeMaterial.Resolve(1, true, 0, 1, true, 1).AlphaReference;
            context.OMSetBlendState(opaqueBlend, ref blendFactor, uint.MaxValue);
            white.W = 0.55f;
            context.UpdateSubresource(texture, 0, ref Unsafe.NullRef<Box>(), ref white, 16, 0);
            UploadFade();
            Draw(background);
            ExpectPixel(4, background); ExpectPixel(12, background);
            // The base material selects the pass row, while this key layer keeps
            // its own 224/255 cutout. A translucent base changes its blend to Alpha.
            white.W = 0.9f;
            context.UpdateSubresource(texture, 0, ref Unsafe.NullRef<Box>(), ref white, 16, 0);
            foreach (var baseBlend in new uint[] { 0, 2 })
            {
                var state = Wrath335M2FadeMaterial.Resolve(1, true, 0, 1, true, 1, baseBlend);
                constants.alphaRef = state.AlphaReference;
                context.OMSetBlendState(state.BlendState == 2 ? fadeBlend : opaqueBlend, ref blendFactor, uint.MaxValue);
                UploadFade();
                Draw(background);
                var expected = baseBlend == 2 ? BlendPixel(0.9f) : new Vector4(1, 1, 1, 0.9f);
                ExpectPixel(4, expected); ExpectPixel(12, expected);
            }
            context.OMSetBlendState(opaqueBlend, ref blendFactor, uint.MaxValue);
            constants.blendMode = constants.pixelShader = 0;
            constants.alphaRef = -1;
            white.W = 0; // Opaque program ignores texture alpha.
            context.UpdateSubresource(texture, 0, ref Unsafe.NullRef<Box>(), ref white, 16, 0);
            UploadFade();
            Draw(background);
            ExpectPixel(4, Vector4.One); ExpectPixel(12, Vector4.One);

            // A fading read-only material leaves depth for the following full-opacity draw.
            var depthDescription = new Texture2DDesc
            {
                Width = 16, Height = 8, MipLevels = 1, ArraySize = 1, Format = Format.FormatD32Float,
                SampleDesc = new(1, 0), Usage = Usage.Default, BindFlags = (uint)BindFlag.DepthStencil
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in depthDescription, null, ref depthTexture));
            SilkMarshal.ThrowHResult(device.CreateDepthStencilView(depthTexture, null, ref depthView));
            context.OMSetRenderTargets(1, ref targetView, depthView);
            context.ClearDepthStencilView(depthView, (uint)ClearFlag.Depth, 1, 0);
            using (var depthStates = new M2DepthStateController(device, context))
            {
                depthStates.BeginPass();
                depthStates.Apply(true, 0x10);
                data[0].RenderParameters.X = data[1].RenderParameters.X = 0.5f;
                context.OMSetBlendState(fadeBlend, ref blendFactor, uint.MaxValue);
                UploadFade();
                Draw(background);
                ExpectPixel(4, BlendPixel(0.5f));
                depthStates.EndPass();
                for (var i = 0; i < triangle.Length; i++) triangle[i].Position.Z = 0.75f;
                context.UpdateSubresource(vertices, 0, ref Unsafe.NullRef<Box>(), ref triangle[0], 0, 0);
                constants.materialColor = new(0, 0, 1, 1);
                data[0].RenderParameters.X = data[1].RenderParameters.X = 1;
                context.OMSetBlendState(opaqueBlend, ref blendFactor, uint.MaxValue);
                depthStates.BeginPass(); depthStates.Apply(true, 0);
                UploadFade();
                Draw(background);
                ExpectPixel(4, new(0, 0, 1, 1)); ExpectPixel(12, new(0, 0, 1, 1));
                // Default depth writing was restored: an even farther triangle is now rejected.
                for (var i = 0; i < triangle.Length; i++) triangle[i].Position.Z = 0.9f;
                context.UpdateSubresource(vertices, 0, ref Unsafe.NullRef<Box>(), ref triangle[0], 0, 0);
                Draw(background);
                ExpectPixel(4, background); ExpectPixel(12, background);
                depthStates.EndPass();
            }

            Vector4 BlendPixel(float alpha) => new(
                alpha + background.X * (1 - alpha), alpha + background.Y * (1 - alpha),
                alpha + background.Z * (1 - alpha), alpha * alpha + background.W * (1 - alpha));

            void UploadFade()
            {
                context.UpdateSubresource(instances, 0, ref Unsafe.NullRef<Box>(), ref data[0], 0, 0);
                context.UpdateSubresource(constantsBuffer, 0, ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
            }

            void Draw(Vector4 clear = default)
            {
                context.ClearRenderTargetView(targetView, (float*)&clear);
                context.DrawInstanced(3, 2, 0, 0);
                context.CopyResource((ID3D11Resource*)readback.Handle, (ID3D11Resource*)target.Handle);
            }

            void ExpectPixel(int x, Vector4 expected)
            {
                MappedSubresource mapped = default;
                SilkMarshal.ThrowHResult(context.Map((ID3D11Resource*)readback.Handle, 0, Map.Read, 0, ref mapped));
                try
                {
                    var pixel = *(Vector4*)((byte*)mapped.PData + 4 * mapped.RowPitch + x * sizeof(Vector4));
                    Assert.IsTrue(Vector4.Distance(expected, pixel) < 0.00002f, $"Pixel {x}: expected {expected}, got {pixel}");
                }
                finally { context.Unmap((ID3D11Resource*)readback.Handle, 0); }
            }
        }
        finally
        {
            context.ClearState();
            depthView.Dispose(); depthTexture.Dispose(); opaqueBlend.Dispose(); fadeBlend.Dispose();
            rasterizer.Dispose(); sampler.Dispose(); textureView.Dispose(); targetView.Dispose();
            texture.Dispose(); readback.Dispose(); target.Dispose();
            fogBuffer.Dispose(); constantsBuffer.Dispose(); instances.Dispose(); vertices.Dispose(); context.Dispose(); device.Dispose();
        }
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
