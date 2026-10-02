using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class OpaquePresentationTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public unsafe void FinalPresentationKeepsEveryRgbChannelAndIgnoresMaterialAlpha(bool floatingPoint)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The DX11 WARP fixture requires Windows.");
        using var d3d = D3D11.GetApi();
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        SilkMarshal.ThrowHResult(d3d.CreateDevice(default(ComPtr<IDXGIAdapter>), D3DDriverType.Warp,
            default, 0, null, 0, D3D11.SdkVersion, ref device, null, ref context));
        ComPtr<ID3D11Texture2D> target = default, readback = default;
        ComPtr<ID3D11RenderTargetView> targetView = default;
        ComPtr<ID3D11DepthStencilState> rejectingDepth = default;
        ComPtr<ID3D11RasterizerState> clippingRasterizer = default;
        try
        {
            using var shaders = new ShaderManager(device, Path.Combine(AppContext.BaseDirectory, "Shaders"));
            using var presentation = new OpaquePresentationRenderer(device, context, shaders);
            const int width = 9, height = 3;
            var source = new Vector4[width * height];
            var packed = new byte[source.Length * 4];
            for (var index = 0; index < source.Length; index++)
            {
                // Includes zero and one, authored transition weights and
                // different RGB at every pixel. Float targets include RGB >1.
                var alpha = (index % 9) / 8f;
                source[index] = new Vector4((index + 1) / 15f, (index + 2) / 35f, (index + 3) / 40f, alpha);
                packed[4 * index] = (byte)(source[index].Z * 255);
                packed[4 * index + 1] = (byte)(source[index].Y * 255);
                packed[4 * index + 2] = (byte)(Math.Min(source[index].X, 1) * 255);
                packed[4 * index + 3] = (byte)(alpha * 255);
            }
            var description = new Texture2DDesc
            {
                Width = width, Height = height, MipLevels = 1, ArraySize = 1,
                Format = floatingPoint ? Format.FormatR32G32B32A32Float : Format.FormatB8G8R8A8Unorm,
                SampleDesc = new(1, 0), Usage = Usage.Default, BindFlags = (uint)BindFlag.RenderTarget
            };
            fixed (Vector4* floatPixels = source)
            fixed (byte* packedPixels = packed)
            {
                var data = new SubresourceData
                { PSysMem = floatingPoint ? floatPixels : packedPixels, SysMemPitch = (uint)(width * (floatingPoint ? 16 : 4)) };
                SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, in data, ref target));
            }
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(target, null, ref targetView));
            description.Usage = Usage.Staging; description.BindFlags = 0; description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref readback));
            var depthDescription = new DepthStencilDesc
            { DepthEnable = true, DepthWriteMask = DepthWriteMask.All, DepthFunc = ComparisonFunc.Never };
            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depthDescription, ref rejectingDepth));
            var rasterizerDescription = new RasterizerDesc
            { FillMode = FillMode.Wireframe, CullMode = CullMode.Front, ScissorEnable = true, DepthClipEnable = true };
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rasterizerDescription, ref clippingRasterizer));
            context.OMSetDepthStencilState(rejectingDepth, 0);
            context.RSSetState(clippingRasterizer);
            var tinyViewport = new Viewport { Width = 1, Height = 1, MaxDepth = 1 };
            context.RSSetViewports(1, in tinyViewport);
            Assert.AreEqual(1u, presentation.Render(targetView, width, height));
            context.CopyResource((ID3D11Resource*)readback.Handle, (ID3D11Resource*)target.Handle);
            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(context.Map((ID3D11Resource*)readback.Handle, 0, Map.Read, 0, ref mapped));
            try
            {
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var row = (byte*)mapped.PData + y * mapped.RowPitch;
                    if (floatingPoint)
                        Assert.AreEqual(new Vector4(source[index].X, source[index].Y, source[index].Z, 1), ((Vector4*)row)[x]);
                    else
                    {
                        for (var channel = 0; channel < 3; channel++)
                            Assert.AreEqual(packed[4 * index + channel], row[4 * x + channel], $"pixel {index}, channel {channel}");
                        Assert.AreEqual((byte)255, row[4 * x + 3], $"pixel {index}");
                    }
                }
            }
            finally { context.Unmap((ID3D11Resource*)readback.Handle, 0); }
        }
        finally
        {
            context.ClearState(); clippingRasterizer.Dispose(); rejectingDepth.Dispose(); targetView.Dispose();
            readback.Dispose(); target.Dispose(); context.Dispose(); device.Dispose();
        }
    }
}
