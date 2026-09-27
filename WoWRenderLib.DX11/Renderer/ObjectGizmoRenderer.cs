using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Managers;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct GizmoRenderStatistics(uint DrawCalls, uint SubmittedVertices, double SubmissionMilliseconds);

/// <summary>
/// Final overlay pass, after scene effects. Owns buffers and pipeline states; borrows shaders from ShaderManager.
/// Uses a private depth surface so handles occlude each other without being hidden by the scene.
/// Restores render targets and rasterizer/depth/blend states. Vertex/constant buffers, shaders and topology remain bound;
/// the next scene pass must bind its own pipeline (as at the start of every scene frame).
/// </summary>
internal sealed unsafe class ObjectGizmoRenderer(
    ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context) : IDisposable
{
    private readonly ComPtr<ID3D11Buffer>[] _meshes = new ComPtr<ID3D11Buffer>[4];
    private ComPtr<ID3D11Buffer> _parameters;
    private ComPtr<ID3D11RasterizerState> _rasterizer;
    private ComPtr<ID3D11DepthStencilState> _depth;
    private CompiledShader _shader;
    private ComPtr<ID3D11Texture2D> _depthTexture;
    private ComPtr<ID3D11DepthStencilView> _depthView;
    private ComPtr<ID3D11DepthStencilState> _overlayDepth;
    private ComPtr<ID3D11BlendState> _overlayBlend;
    private ComPtr<ID3D11Buffer> _guideBuffer;
    private readonly GizmoDragGeometry _guides = new();
    private Vector2 _depthSize;
    private const int GuideCapacity = 4096;

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public Matrix4x4 WorldViewProjection;
        public Vector4 Highlight;
        public Matrix4x4 ScreenToLocal;
        public Vector4 CameraLocal;
    }

    public void Initialize(ShaderManager shaders)
    {
        RefreshShader(shaders);
        for (var mode = 0; mode < 4; mode++)
        {
            var vertices = ObjectGizmoGeometry.For((ObjectGizmoMode)mode).Vertices;
            var description = new BufferDesc
            {
                ByteWidth = (uint)(vertices.Length * sizeof(Vector4)), Usage = Usage.Immutable,
                BindFlags = (uint)BindFlag.VertexBuffer
            };
            fixed (Vector4* data = vertices)
            {
                var initial = new SubresourceData { PSysMem = data };
                SilkMarshal.ThrowHResult(device.CreateBuffer(in description, in initial, ref _meshes[mode]));
            }
        }
        var constants = new BufferDesc
        {
            ByteWidth = (uint)sizeof(Parameters), Usage = Usage.Default, BindFlags = (uint)BindFlag.ConstantBuffer
        };
        SilkMarshal.ThrowHResult(device.CreateBuffer(in constants, null, ref _parameters));
        var rasterizer = new RasterizerDesc { FillMode = FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = true };
        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rasterizer, ref _rasterizer));
        var depth = new DepthStencilDesc { DepthEnable = true, DepthWriteMask = DepthWriteMask.All, DepthFunc = ComparisonFunc.LessEqual };
        SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depth, ref _depth));
        depth.DepthEnable = false;
        depth.DepthWriteMask = DepthWriteMask.Zero;
        SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depth, ref _overlayDepth));
        var blend = new BlendDesc();
        blend.RenderTarget[0] = new RenderTargetBlendDesc
        {
            BlendEnable = true, SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha,
            BlendOp = BlendOp.Add, SrcBlendAlpha = Blend.One, DestBlendAlpha = Blend.InvSrcAlpha,
            BlendOpAlpha = BlendOp.Add, RenderTargetWriteMask = (byte)ColorWriteEnable.All
        };
        SilkMarshal.ThrowHResult(device.CreateBlendState(in blend, ref _overlayBlend));
        var guides = new BufferDesc
        {
            ByteWidth = GuideCapacity * 16, Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.VertexBuffer, CPUAccessFlags = (uint)CpuAccessFlag.Write
        };
        SilkMarshal.ThrowHResult(device.CreateBuffer(in guides, null, ref _guideBuffer));
    }

    public void RefreshShader(ShaderManager shaders) => _shader = shaders.GetOrCompileShader("object_gizmo");

    public GizmoRenderStatistics Render(ObjectGizmoController gizmo, Matrix4x4 viewProjection)
    {
        if (!gizmo.IsVisible)
            return default;
        var started = Stopwatch.GetTimestamp();
        EnsureDepth(gizmo.ViewportSize);
        ComPtr<ID3D11RenderTargetView> oldTarget = default;
        ComPtr<ID3D11DepthStencilView> oldDepthView = default;
        context.OMGetRenderTargets(1, ref oldTarget, ref oldDepthView);
        ComPtr<ID3D11RasterizerState> oldRasterizer = default;
        ComPtr<ID3D11DepthStencilState> oldDepth = default;
        ComPtr<ID3D11BlendState> oldBlend = default;
        uint oldStencil = 0, oldMask = 0;
        float* oldFactors = stackalloc float[4];
        context.RSGetState(ref oldRasterizer);
        context.OMGetDepthStencilState(ref oldDepth, ref oldStencil);
        context.OMGetBlendState(ref oldBlend, oldFactors, ref oldMask);
        try
        {
            context.OMSetRenderTargets(1, ref oldTarget, _depthView);
            context.ClearDepthStencilView(_depthView, (uint)ClearFlag.Depth, 1, 0);
            var view = GizmoView.Create(gizmo.Frame, viewProjection);
            var parameters = new Parameters
            {
                WorldViewProjection = gizmo.Frame.World * viewProjection,
                Highlight = new Vector4((float)gizmo.Handle, (float)gizmo.Mode, gizmo.ScaleEnabled ? 1 : 0, 0),
                ScreenToLocal = view.ScreenToLocal,
                CameraLocal = new Vector4(view.CameraLocal, 0)
            };
            context.UpdateSubresource(_parameters, 0, null, &parameters, 0, 0);
            ComPtr<ID3D11ClassInstance> noClass = default;
            ComPtr<ID3D11GeometryShader> noGeometry = default;
            context.VSSetShader(_shader.VertexShader, ref noClass, 0);
            context.GSSetShader(noGeometry, ref noClass, 0);
            context.PSSetShader(_shader.PixelShader, ref noClass, 0);
            context.VSSetConstantBuffers(0, 1, ref _parameters);
            context.PSSetConstantBuffers(0, 1, ref _parameters);
            context.IASetInputLayout(_shader.InputLayout);
            context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
            uint stride = (uint)sizeof(Vector4), offset = 0;
            context.IASetVertexBuffers(0, 1, ref _meshes[(int)gizmo.Mode], in stride, in offset);
            context.RSSetState(_rasterizer);
            context.OMSetDepthStencilState(_depth, 0);
            context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), null, uint.MaxValue);
            var count = (uint)ObjectGizmoGeometry.For(gizmo.Mode).Vertices.Length;
            context.Draw(count, 0);
            uint drawCalls = 1;
            if (gizmo.IsDragging)
            {
                _guides.Build(gizmo.DragVisual, viewProjection);
                var guides = CollectionsMarshal.AsSpan(_guides.Vertices);
                if (guides.Length > GuideCapacity) throw new InvalidOperationException("Gizmo guide capacity exceeded.");
                if (guides.Length > 0)
                {
                    MappedSubresource mapped = default;
                    SilkMarshal.ThrowHResult(context.Map(_guideBuffer, 0, Map.WriteDiscard, 0, ref mapped));
                    try
                    {
                        fixed (Vector4* data = guides)
                            Buffer.MemoryCopy(data, mapped.PData, GuideCapacity * sizeof(Vector4), guides.Length * sizeof(Vector4));
                    }
                    finally { context.Unmap(_guideBuffer, 0); }
                    parameters.WorldViewProjection = viewProjection;
                    parameters.Highlight = new Vector4(0, -1, 1, 0);
                    context.UpdateSubresource(_parameters, 0, null, &parameters, 0, 0);
                    context.IASetVertexBuffers(0, 1, ref _guideBuffer, in stride, in offset);
                    context.OMSetDepthStencilState(_overlayDepth, 0);
                    context.OMSetBlendState(_overlayBlend, null, uint.MaxValue);
                    context.Draw((uint)guides.Length, 0);
                    count += (uint)guides.Length;
                    drawCalls++;
                }
            }
            return new(drawCalls, count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        finally
        {
            context.OMSetRenderTargets(1, ref oldTarget, oldDepthView);
            oldTarget.Dispose();
            oldDepthView.Dispose();
            context.RSSetState(oldRasterizer);
            context.OMSetDepthStencilState(oldDepth, oldStencil);
            context.OMSetBlendState(oldBlend, oldFactors, oldMask);
            oldRasterizer.Dispose();
            oldDepth.Dispose();
            oldBlend.Dispose();
        }
    }

    private void EnsureDepth(Vector2 size)
    {
        if (_depthSize == size) return;
        _depthView.Dispose();
        _depthTexture.Dispose();
        _depthView = default;
        _depthTexture = default;
        var description = new Texture2DDesc
        {
            Width = (uint)size.X, Height = (uint)size.Y, MipLevels = 1, ArraySize = 1,
            Format = Format.FormatD32Float, SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default, BindFlags = (uint)BindFlag.DepthStencil
        };
        SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref _depthTexture));
        SilkMarshal.ThrowHResult(device.CreateDepthStencilView(_depthTexture, null, ref _depthView));
        _depthSize = size;
    }

    public void Dispose()
    {
        _depthView.Dispose();
        _depthTexture.Dispose();
        _overlayDepth.Dispose();
        _overlayBlend.Dispose();
        _guideBuffer.Dispose();
        foreach (var mesh in _meshes)
            mesh.Dispose();
        _parameters.Dispose();
        _rasterizer.Dispose();
        _depth.Dispose();
    }
}
