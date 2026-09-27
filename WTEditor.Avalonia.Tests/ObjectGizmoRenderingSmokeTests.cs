using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Renderer;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class ObjectGizmoRenderingSmokeTests
{
    [TestMethod]
    public unsafe void AllGizmoShadersRenderColoredHandlesOnWarp()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Direct3D verification requires Windows.");
        // The headless WARP test has no native window or swapchain provider.
#pragma warning disable CS0618
        using var api = D3D11.GetApi();
#pragma warning restore CS0618
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        ComPtr<ID3D11Texture2D> target = default, staging = default;
        ComPtr<ID3D11RenderTargetView> targetView = default;
        try
        {
            SilkMarshal.ThrowHResult(api.CreateDevice(default(ComPtr<IDXGIAdapter>), D3DDriverType.Warp,
                default, 0, null, 0, D3D11.SdkVersion, ref device, null, ref context));
            using var shaders = new ShaderManager(device, Path.Combine(AppContext.BaseDirectory, "Shaders"));
            using var renderer = new ObjectGizmoRenderer(device, context);
            renderer.Initialize(shaders);
            const int size = 384;
            var description = new Texture2DDesc
            {
                Width = size, Height = size, ArraySize = 1, MipLevels = 1,
                Format = Format.FormatB8G8R8A8Unorm, SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default, BindFlags = (uint)BindFlag.RenderTarget
            };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref target));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(target, null, ref targetView));
            description.Usage = Usage.Staging;
            description.BindFlags = 0;
            description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref staging));
            var viewport = new Viewport(0, 0, size, size, 0, 1);
            context.RSSetViewports(1, in viewport);
            context.OMSetRenderTargets(1, ref targetView, default(ComPtr<ID3D11DepthStencilView>));
            var pixels = new byte[size * size * 4];
            float* clear = stackalloc float[4] { .04f, .05f, .07f, 1 };
            foreach (var mode in Enum.GetValues<ObjectGizmoMode>())
            {
                var (selection, controller, camera) = ObjectGizmoSmokeTests.CreateGesture();
                context.ClearRenderTargetView(targetView, clear);
                controller.Update(ObjectGizmoSmokeTests.Input(new Vector2(-100), mode), selection, camera, size, size, false);
                var stats = renderer.Render(controller, camera.GetViewMatrix() * camera.GetProjectionMatrix());
                Assert.AreEqual(1u, stats.DrawCalls);
                Assert.IsTrue(stats.SubmittedVertices > 0);
                Readback(mode.ToString());
                var red = 0;
                var green = 0;
                var blue = 0;
                for (var pixel = 0; pixel < pixels.Length; pixel += 4)
                {
                    if (pixels[pixel + 2] > 180 && pixels[pixel + 1] < 100) red++;
                    if (pixels[pixel + 1] > 180 && pixels[pixel + 2] < 100) green++;
                    if (pixels[pixel] > 180 && pixels[pixel + 2] < 100) blue++;
                }
                Assert.IsTrue(red > 20 && green > 20 && blue > 20, $"{mode}: red={red}, green={green}, blue={blue}");
                if (mode is ObjectGizmoMode.Rotate or ObjectGizmoMode.Transform)
                {
                    var white = 0;
                    for (var pixel = 0; pixel < pixels.Length; pixel += 4)
                        if (pixels[pixel] > 210 && pixels[pixel + 1] > 210 && pixels[pixel + 2] > 210) white++;
                    Assert.IsTrue(white > 200, "The camera-facing outer rotation circle must be visible.");
                }
                if (mode != ObjectGizmoMode.Transform)
                {
                    var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
                    var pointer = new Vector2(size / 2);
                    if (mode == ObjectGizmoMode.Rotate)
                    {
                        var view = GizmoView.Create(controller.Frame, vp);
                        var start = Vector3.Transform(view.Vertex(new(1.08f, 0, 0), GizmoHandle.ViewRotate), controller.Frame.World);
                        ObjectGizmoMath.Project(start, vp, new(size), out pointer);
                    }
                    var input = ObjectGizmoSmokeTests.Input(pointer, mode);
                    input.LeftMouseDown = true;
                    controller.Update(input, selection, camera, size, size, false);
                    Assert.IsTrue(controller.IsDragging);
                    input.MousePosition += mode == ObjectGizmoMode.Rotate ? new Vector2(-38, -65) : new Vector2(45, -30);
                    controller.Update(input, selection, camera, size, size, false);
                    context.ClearRenderTargetView(targetView, clear);
                    var dragStats = renderer.Render(controller, vp);
                    Assert.AreEqual(2u, dragStats.DrawCalls, "A gesture draws both handles and guides.");
                    Assert.IsTrue(dragStats.SubmittedVertices > stats.SubmittedVertices);
                    Assert.IsFalse(string.IsNullOrEmpty(controller.Feedback.Text));
                    Readback($"{mode}-drag");
                    if (mode == ObjectGizmoMode.Rotate)
                    {
                        var filled = 0;
                        for (var pixel = 0; pixel < pixels.Length; pixel += 4)
                            if (pixels[pixel + 2] > 40 && pixels[pixel + 2] < 120 && pixels[pixel + 2] > pixels[pixel] * 2) filled++;
                        Assert.IsTrue(filled > 500, "Rotation shows a translucent swept-angle sector.");
                    }
                    controller.Cancel();
                }
            }
            void Readback(string name)
            {
                context.CopyResource(staging, target);
                MappedSubresource mapped = default;
                SilkMarshal.ThrowHResult(context.Map(staging, 0, Map.Read, 0, ref mapped));
                try
                {
                    for (var row = 0; row < size; row++)
                        Marshal.Copy((nint)mapped.PData + row * (int)mapped.RowPitch, pixels, row * size * 4, size * 4);
                }
                finally { context.Unmap(staging, 0); }
                if (Environment.GetEnvironmentVariable("WTE_GIZMO_CAPTURE_DIRECTORY") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    // Encode the actual GPU readback; Avalonia's headless bitmap backend is a stub.
                    using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
                    using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    using var output = File.Create(Path.Combine(directory, $"gizmo-{name}.png"));
                    encoded.SaveTo(output);
                }
            }

        }
        finally
        {
            if (context.Handle != null) context.ClearState();
            targetView.Dispose();
            staging.Dispose();
            target.Dispose();
            context.Dispose();
            device.Dispose();
        }
    }
}
