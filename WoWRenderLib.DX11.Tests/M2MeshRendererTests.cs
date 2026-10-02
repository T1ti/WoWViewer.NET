using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class M2MeshRendererTests
{
    [TestMethod]
    [DataRow(1, false)]
    [DataRow(1025, false)]
    [DataRow(1, true)]
    [DataRow(1025, true)]
    public unsafe void MeshSubmissionPreservesPhasePixelsChunkUploadsAndPoseVersions(int instanceCount, bool testWater)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The DX11 WARP fixture requires Windows.");
        using var d3d = D3D11.GetApi();
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        SilkMarshal.ThrowHResult(d3d.CreateDevice(default(ComPtr<IDXGIAdapter>), D3DDriverType.Warp,
            default, 0, null, 0, D3D11.SdkVersion, ref device, null, ref context));
        ComPtr<ID3D11Buffer> vertices = default, indices = default, fogBuffer = default;
        ComPtr<ID3D11Texture2D> target = default, readback = default, texture = default, depth = default;
        ComPtr<ID3D11RenderTargetView> targetView = default;
        ComPtr<ID3D11DepthStencilView> depthView = default;
        ComPtr<ID3D11ShaderResourceView> textureView = default;
        ComPtr<ID3D11SamplerState> sampler = default;
        ComPtr<ID3D11RasterizerState> oneSided = default, twoSided = default;
        var blendStates = new ComPtr<ID3D11BlendState>[14];
        try
        {
            using var shaders = new ShaderManager(device, Path.Combine(AppContext.BaseDirectory, "Shaders"));
            using var renderer = new M2MeshRenderer(device, context);
            renderer.Initialize();
            M2Vertex[] triangle = [
                new() { Position = new(-0.4f, -0.8f, 0.5f), Normal = Vector3.UnitZ, BoneWeights = 255 },
                new() { Position = new(0, 0.8f, 0.5f), Normal = Vector3.UnitZ, BoneWeights = 255 },
                new() { Position = new(0.4f, -0.8f, 0.5f), Normal = Vector3.UnitZ, BoneWeights = 255 }
            ];
            vertices = Buffer(device, triangle, BindFlag.VertexBuffer);
            indices = Buffer<ushort>(device, [0, 1, 2], BindFlag.IndexBuffer);
            var fog = new WrathFogCB();
            fogBuffer = Buffer(device, [fog], BindFlag.ConstantBuffer);
            var description = new Texture2DDesc
            {
                Width = 16, Height = 8, MipLevels = 1, ArraySize = 1,
                Format = Format.FormatR32G32B32A32Float, SampleDesc = new(1, 0),
                Usage = Usage.Default, BindFlags = (uint)BindFlag.RenderTarget
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref target));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(target, null, ref targetView));
            description.Usage = Usage.Staging;
            description.BindFlags = 0;
            description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref readback));
            description.Width = description.Height = 1;
            description.Usage = Usage.Default;
            description.BindFlags = (uint)BindFlag.ShaderResource;
            description.CPUAccessFlags = 0;
            var white = Vector4.One;
            var textureData = new SubresourceData { PSysMem = &white, SysMemPitch = 16 };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, in textureData, ref texture));
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(texture, null, ref textureView));
            description.Width = 16; description.Height = 8;
            description.Format = Format.FormatD32Float;
            description.BindFlags = (uint)BindFlag.DepthStencil;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref depth));
            SilkMarshal.ThrowHResult(device.CreateDepthStencilView(depth, null, ref depthView));
            var samplerDescription = new SamplerDesc
            {
                Filter = Filter.MinMagMipPoint, AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, MaxLOD = float.MaxValue
            };
            SilkMarshal.ThrowHResult(device.CreateSamplerState(in samplerDescription, ref sampler));
            var rasterizerDescription = new RasterizerDesc
            { FillMode = FillMode.Solid, CullMode = CullMode.Back, DepthClipEnable = true };
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rasterizerDescription, ref oneSided));
            rasterizerDescription.CullMode = CullMode.None;
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rasterizerDescription, ref twoSided));
            for (var i = 0; i < blendStates.Length; i++)
            {
                var blendDescription = SceneBlendPolicy.Description(i);
                SilkMarshal.ThrowHResult(device.CreateBlendState(in blendDescription, ref blendStates[i]));
            }
            var viewport = new Viewport { Width = 16, Height = 8, MaxDepth = 1 };
            context.RSSetViewports(1, in viewport);
            context.OMSetRenderTargets(1, ref targetView, depthView);
            context.VSSetConstantBuffers(4, 1, ref fogBuffer);
            context.PSSetConstantBuffers(4, 1, ref fogBuffer);
            var bindings = new M2MeshRenderBindings(shaders.GetOrCompileShader("m2"),
                oneSided, twoSided, blendStates, [sampler, sampler, sampler, sampler], _ => textureView);
            var model = new ParsedDoodadBatch
            {
                fileDataID = 7, vertexBuffer = vertices, indiceBuffer = indices, usesLegacyDepthFlags = true,
                boundingBox = new(new Vector3(-1), new Vector3(1)), boundingRadius = 1,
                submeshes = [
                    new() { numFaces = 3, material = [7], blendType = 0, renderFlags = 7 },
                    new() { numFaces = 3, material = [7], blendType = 2, renderFlags = 0x17 }
                ]
            };
            var placements = new List<M2Container>();
            for (var i = 0; i < instanceCount; i++)
            {
                var placement = (M2Container)RuntimeHelpers.GetUninitializedObject(typeof(M2Container));
                placement.FileDataId = 7;
                placement.Scale = 1;
                placements.Add(placement);
            }
            var packet = new M2InstancePacket(placements);
            packet.EnsureSpatialData(model);
            // Only the final instance covers the pixel: a missing second upload cannot pass.
            for (var i = 0; i < instanceCount; i++)
                packet.WorldMatrices[i] = i == instanceCount - 1 ? Matrix4x4.Identity : Matrix4x4.CreateTranslation(10, 0, 0);
            var pose = new M2AnimationPose
            {
                Version = 1,
                BonePalette = Enumerable.Repeat(Matrix4x4.Identity, M2Animation.MaxGpuBones).ToArray(),
                Materials = [
                    new(new(1, 0, 0, 1), Matrix4x4.Identity, Matrix4x4.Identity, false, false),
                    new(new(0, 1, 0, 0.5f), Matrix4x4.Identity, Matrix4x4.Identity, false, false)
                ]
            };
            var group = new M2AnimationDrawGroup();
            group.Reset(pose);
            for (var i = 0; i < instanceCount; i++) group.AddInstance(i);
            M2MeshSubmission[] submissions = [new(packet, model, [group], true, true)];
            var constants = new M2PerObjectCB
            {
                projection_matrix = Matrix4x4.Identity, view_matrix = Matrix4x4.Identity,
                model_matrix = Matrix4x4.Identity, globalOpacity = 1,
                ambientColor = Vector3.One, materialColor = Vector4.One
            };
            Clear();
            var opaque = renderer.Draw(submissions, false, constants, 1, fog, null, bindings);
            ExpectPixel(new(1, 0, 0, 1));
            var chunks = (uint)((instanceCount + 1023) / 1024);
            Assert.AreEqual(chunks, opaque.M2DrawCalls);
            Assert.AreEqual((uint)instanceCount, opaque.M2SubmittedInstances);
            Assert.AreEqual((ulong)instanceCount * 3, opaque.M2SubmittedIndices);
            Assert.AreEqual(chunks, opaque.InstanceBufferMapCalls);
            Assert.AreEqual(chunks + 1, opaque.VertexBufferBindings);
            Assert.AreEqual(1u, opaque.IndexBufferBindings);
            Assert.AreEqual(2u, opaque.ConstantBufferUpdates); // Palette + material, reused across chunks.
            Assert.AreEqual(chunks, opaque.TextureBindingCalls);
            Assert.AreEqual(1u, opaque.BlendStateBindings);

            // Simulate intervening water/debug state; the second phase rebinds its full mesh pipeline.
            context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
            context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
            context.VSSetConstantBuffers(0, 1, ref fogBuffer);
            var translucent = renderer.Draw(submissions, true, constants, 1, fog, null, bindings);
            ExpectPixel(new(0.5f, 0.5f, 0, 0.75f));
            Assert.AreEqual(chunks, translucent.M2DrawCalls);
            Assert.AreEqual((uint)instanceCount, translucent.M2SubmittedInstances);
            Assert.AreEqual((ulong)instanceCount * 3, translucent.M2SubmittedIndices);
            Assert.AreEqual(1u, translucent.ConstantBufferUpdates); // Same palette survives intervening passes.
            ExpectRestoredState();

            pose.BonePalette[0] = Matrix4x4.CreateTranslation(2, 0, 0);
            pose.Version++;
            Clear();
            var moved = renderer.Draw(submissions, false, constants, 2, fog, null, bindings);
            ExpectPixel(Vector4.Zero);
            Assert.AreEqual(2u, moved.ConstantBufferUpdates);
            pose.BonePalette[0] = Matrix4x4.Identity;
            pose.Materials[0] = pose.Materials[0] with { Color = new(0, 0, 1, 1) };
            pose.Version++;
            Clear();
            renderer.Draw(submissions, false, constants, 3, fog, null, bindings);
            ExpectPixel(new(0, 0, 1, 1));

            // Failure while filling a mapped upload must still unmap it for the next frame.
            group.Reset(pose);
            group.AddInstance(instanceCount);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                renderer.Draw(submissions, false, constants, 4, fog, null, bindings));
            ExpectRestoredState();
            group.Reset(pose);
            for (var i = 0; i < instanceCount; i++) group.AddInstance(i);
            Clear();
            renderer.Draw(submissions, false, constants, 5, fog, null, bindings);
            ExpectPixel(new(0, 0, 1, 1));

            // A cache failure occurs after applying read-only depth/two-sided rasterization.
            var failingBindings = bindings with { ResolveTexture = _ => throw new InvalidOperationException("Texture unavailable") };
            Assert.ThrowsException<InvalidOperationException>(() =>
                renderer.Draw(submissions, true, constants, 3, fog, null, failingBindings));
            ExpectRestoredState();

            if (testWater)
            {
                // Same skin and placement, spanning the world Z=.5 liquid plane.
                triangle[0].Position.Z = triangle[2].Position.Z = .2f;
                triangle[1].Position.Z = .8f;
                fixed (M2Vertex* source = triangle)
                    context.UpdateSubresource((ID3D11Resource*)vertices.Handle, 0, (Box*)null, source, 0, 0);
                pose.Materials[1] = pose.Materials[1] with { Color = new(0, 1, 0, .5f) };
                pose.BonePalette[0] = Matrix4x4.Identity;
                pose.Version++;
                var water = Enumerable.Repeat(new Wrath335M2MeshWater(new(true, true), new(0, 0, 1, -.5f)), instanceCount).ToArray();
                submissions[0] = submissions[0] with { Water = water };
                foreach (var side in new[] { Wrath335M2QueueMask.AboveWater, Wrath335M2QueueMask.BelowWater })
                {
                    Clear();
                    var clipped = renderer.Draw(submissions, true, constants, 6, fog, null, bindings, side);
                    Assert.AreEqual((uint)instanceCount, clipped.M2SubmittedInstances);
                    ExpectPixel(side == Wrath335M2QueueMask.AboveWater ? new(0, .5f, 0, .25f) : Vector4.Zero, 2);
                    ExpectPixel(side == Wrath335M2QueueMask.BelowWater ? new(0, .5f, 0, .25f) : Vector4.Zero, 6);
                    ExpectRestoredState();
                }
                // An animated skin can move a vertex across water; clip after skinning.
                pose.BonePalette[0] = Matrix4x4.CreateTranslation(0, 0, .35f);
                pose.Version++;
                Clear();
                renderer.Draw(submissions, true, constants, 7, fog, null, bindings, Wrath335M2QueueMask.BelowWater);
                ExpectPixel(Vector4.Zero, 6);
                renderer.Draw(submissions, true, constants, 7, fog, null, bindings, Wrath335M2QueueMask.AboveWater);
                ExpectPixel(new(0, .5f, 0, .25f), 6);
                pose.BonePalette[0] = Matrix4x4.Identity;
                pose.Version++;

                using var liquidRenderer = new WorldLiquidRenderer(device, context);
                liquidRenderer.Initialize(shaders);
                using var liquidVertices = Buffer<WorldLiquidVertex>(device, [
                    new() { Position = new(-1, -1, .5f) }, new() { Position = new(-1, 1, .5f) },
                    new() { Position = new(1, 1, .5f) }, new() { Position = new(1, -1, .5f) }
                ], BindFlag.VertexBuffer);
                using var liquidIndices = Buffer<uint>(device, [0, 1, 2, 0, 2, 3], BindFlag.IndexBuffer);
                var liquidConstants = new WorldLiquidPerObjectCB
                {
                    Model = Matrix4x4.Identity, View = Matrix4x4.Identity, Projection = Matrix4x4.Identity,
                    NativeParameters = new(1, 1, 0, 1), NativeVertexColor = new(0, 0, 1, .5f),
                    LightingAmbient = Vector4.One, LightDirection = new(0, 0, 1, 0)
                };
                foreach (var viewerId in new[] { 0u, 17u })
                {
                    Clear();
                    var order = Wrath335M2WaterQueues.PassOrder(viewerId);
                    renderer.Draw(submissions, true, constants, 8, fog, null, bindings, order.BeforeLiquid);
                    liquidRenderer.BeginSubmission();
                    var liquidStride = (uint)Unsafe.SizeOf<WorldLiquidVertex>();
                    uint offset = 0;
                    var vb = liquidVertices;
                    context.IASetVertexBuffers(0, 1, ref vb, in liquidStride, in offset);
                    context.IASetIndexBuffer(liquidIndices, Format.FormatR32Uint, 0);
                    liquidRenderer.SubmitBatch(liquidConstants, 6, 0, false, textureView, textureView);
                    liquidRenderer.EndSubmission();
                    renderer.Draw(submissions, true, constants, 8, fog, null, bindings, order.AfterLiquid);
                    // Viewer ID reverses which clipped half blends before the liquid.
                    var before = new Vector4(.5f, .75f, 1, .625f);
                    var after = new Vector4(.25f, .75f, .5f, .5f);
                    ExpectPixel(viewerId == 0 ? after : before, 2);
                    ExpectPixel(viewerId == 0 ? before : after, 6);
                    ExpectRestoredState();
                }
                // An unpartitioned following draw must not inherit the clip plane.
                Clear();
                renderer.Draw(submissions, true, constants, 9, fog, null, bindings);
                ExpectPixel(new(0, .5f, 0, .25f), 2);
                ExpectPixel(new(0, .5f, 0, .25f), 6);
                // Compact selected instances across source chunk boundaries.
                for (var i = 0; i < instanceCount; i++)
                    water[i] = new(new(i != instanceCount - 1, i == instanceCount - 1), default);
                Clear();
                var aboveOnly = renderer.Draw(submissions, true, constants, 10, fog, null, bindings,
                    Wrath335M2QueueMask.AboveWater);
                Assert.AreEqual((uint)instanceCount - 1, aboveOnly.M2SubmittedInstances);
                ExpectPixel(Vector4.Zero, 6);
                var belowOnly = renderer.Draw(submissions, true, constants, 10, fog, null, bindings,
                    Wrath335M2QueueMask.BelowWater);
                Assert.AreEqual(1u, belowOnly.M2SubmittedInstances);
                Assert.AreEqual(1u, belowOnly.InstanceBufferMapCalls);
                ExpectPixel(new(0, .5f, 0, .25f), 6);
                submissions[0] = submissions[0] with { Water = null };
                Clear();
                Assert.AreEqual(0u, renderer.Draw(submissions, true, constants, 10, fog, null, bindings,
                    Wrath335M2QueueMask.BelowWater).M2DrawCalls);
                renderer.Draw(submissions, true, constants, 10, fog, null, bindings, Wrath335M2QueueMask.AboveWater);
                ExpectPixel(new(0, .5f, 0, .25f), 6);
            }

            void Clear()
            {
                var clear = Vector4.Zero;
                context.ClearRenderTargetView(targetView, (float*)&clear);
                context.ClearDepthStencilView(depthView, (uint)ClearFlag.Depth, 1, 0);
            }

            void ExpectPixel(Vector4 expected, int row = 4)
            {
                context.CopyResource((ID3D11Resource*)readback.Handle, (ID3D11Resource*)target.Handle);
                MappedSubresource mapped = default;
                SilkMarshal.ThrowHResult(context.Map((ID3D11Resource*)readback.Handle, 0, Map.Read, 0, ref mapped));
                try
                {
                    var pixel = *(Vector4*)((byte*)mapped.PData + row * mapped.RowPitch + 8 * sizeof(Vector4));
                    Assert.IsTrue(Vector4.Distance(expected, pixel) < 0.00002f, $"Expected {expected}, got {pixel}");
                }
                finally { context.Unmap((ID3D11Resource*)readback.Handle, 0); }
            }

            void ExpectRestoredState()
            {
                ComPtr<ID3D11DepthStencilState> actualDepth = default;
                ComPtr<ID3D11RasterizerState> actualRasterizer = default;
                uint stencil = 0;
                context.OMGetDepthStencilState(ref actualDepth, ref stencil);
                context.RSGetState(ref actualRasterizer);
                try
                {
                    Assert.IsTrue(actualDepth.Handle == null, "Default depth must be restored.");
                    Assert.IsTrue(actualRasterizer.Handle == oneSided.Handle, "One-sided rasterizer must be restored.");
                }
                finally { actualRasterizer.Dispose(); actualDepth.Dispose(); }
            }
        }
        finally
        {
            context.ClearState();
            foreach (var blend in blendStates) blend.Dispose();
            twoSided.Dispose(); oneSided.Dispose(); sampler.Dispose(); textureView.Dispose();
            depthView.Dispose(); targetView.Dispose(); depth.Dispose(); texture.Dispose(); readback.Dispose(); target.Dispose();
            fogBuffer.Dispose(); indices.Dispose(); vertices.Dispose(); context.Dispose(); device.Dispose();
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
