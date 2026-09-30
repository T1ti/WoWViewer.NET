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
        ComPtr<ID3D11Buffer> vertices = default, instances = default, constantsBuffer = default;
        ComPtr<ID3D11Texture2D> target = default, readback = default, texture = default;
        ComPtr<ID3D11RenderTargetView> targetView = default;
        ComPtr<ID3D11ShaderResourceView> textureView = default;
        ComPtr<ID3D11SamplerState> sampler = default;
        ComPtr<ID3D11RasterizerState> rasterizer = default;
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

            void Draw()
            {
                var clear = Vector4.Zero;
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
            rasterizer.Dispose(); sampler.Dispose(); textureView.Dispose(); targetView.Dispose();
            texture.Dispose(); readback.Dispose(); target.Dispose();
            constantsBuffer.Dispose(); instances.Dispose(); vertices.Dispose(); context.Dispose(); device.Dispose();
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
