using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using WoWRenderLib.DX11.Managers;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Makes a completed world image opaque for GUI composition without changing
/// its RGB or the alpha used by any scene material or intermediate pass.
/// </summary>
internal sealed unsafe class OpaquePresentationRenderer : IDisposable
{
    private readonly ComPtr<ID3D11DeviceContext> _context;
    private readonly ShaderManager _shaders;
    private ComPtr<ID3D11BlendState> _alphaWriteState;
    private ComPtr<ID3D11DepthStencilState> _noDepthState;
    private ComPtr<ID3D11RasterizerState> _rasterizer;

    internal OpaquePresentationRenderer(ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context, ShaderManager shaders)
    {
        _context = context;
        _shaders = shaders;
        _ = shaders.GetOrCompileShader("presentation_opaque");
        try
        {
            var blend = new BlendDesc();
            blend.RenderTarget[0] = new RenderTargetBlendDesc
            {
                BlendEnable = false,
                SrcBlend = Blend.One, DestBlend = Blend.Zero, BlendOp = BlendOp.Add,
                SrcBlendAlpha = Blend.One, DestBlendAlpha = Blend.Zero, BlendOpAlpha = BlendOp.Add,
                RenderTargetWriteMask = (byte)ColorWriteEnable.Alpha
            };
            SilkMarshal.ThrowHResult(device.CreateBlendState(in blend, ref _alphaWriteState));
            var depth = new DepthStencilDesc
            { DepthEnable = false, DepthWriteMask = DepthWriteMask.Zero, DepthFunc = ComparisonFunc.Always };
            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depth, ref _noDepthState));
            var rasterizer = new RasterizerDesc
            { FillMode = FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = true };
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rasterizer, ref _rasterizer));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Returns the draw count. Leaves the output bound without a depth target,
    /// a full-size viewport, and fullscreen shaders with no geometry stage or
    /// input layout. Restores default blend/depth states; subsequent geometry
    /// must bind its pipeline and depth target, as SceneManager does each frame.
    /// </summary>
    internal uint Render(ComPtr<ID3D11RenderTargetView> output, uint width, uint height)
    {
        if (width == 0 || height == 0)
            return 0;

        ComPtr<ID3D11ClassInstance> noClass = default;
        ComPtr<ID3D11GeometryShader> noGeometry = default;
        ComPtr<ID3D11InputLayout> noLayout = default;
        ComPtr<ID3D11DepthStencilView> noDepth = default;
        var viewport = new Viewport { Width = width, Height = height, MaxDepth = 1 };
        var shader = _shaders.GetOrCompileShader("presentation_opaque");
        _context.OMSetRenderTargets(1, ref output, noDepth);
        _context.OMSetDepthStencilState(_noDepthState, 0);
        _context.OMSetBlendState(_alphaWriteState, (float*)null, uint.MaxValue);
        _context.RSSetState(_rasterizer);
        _context.RSSetViewports(1, in viewport);
        _context.IASetInputLayout(noLayout);
        _context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        _context.VSSetShader(shader.VertexShader, ref noClass, 0);
        _context.GSSetShader(noGeometry, ref noClass, 0);
        _context.PSSetShader(shader.PixelShader, ref noClass, 0);
        _context.Draw(3, 0);
        ComPtr<ID3D11BlendState> defaultBlend = default;
        _context.OMSetBlendState(defaultBlend, (float*)null, uint.MaxValue);
        ComPtr<ID3D11DepthStencilState> defaultDepth = default;
        _context.OMSetDepthStencilState(defaultDepth, 0);
        return 1;
    }

    public void Dispose()
    {
        _rasterizer.Dispose(); _rasterizer = default;
        _noDepthState.Dispose(); _noDepthState = default;
        _alphaWriteState.Dispose(); _alphaWriteState = default;
    }
}
