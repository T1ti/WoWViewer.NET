using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Managers;

namespace WoWRenderLib.DX11.Renderer;

[StructLayout(LayoutKind.Sequential)]
internal struct SceneGlowParameters
{
    public Vector2 TexelStep;
    public float Mix;
    public float Strength;
    public int PassIndex;
    private Vector3 _padding;
}

/// <summary>
/// The 3.3.5 full-screen glow pass. The scene is rendered into a texture,
/// reduced to half resolution, blurred in two directions, and composed into
/// the caller's target. Targets are reused until the viewport changes size.
/// </summary>
internal sealed unsafe class SceneGlowRenderer(
    ComPtr<ID3D11Device> device,
    ComPtr<ID3D11DeviceContext> context) : IDisposable
{
    private readonly ComPtr<ID3D11Device> _device = device;
    private readonly ComPtr<ID3D11DeviceContext> _context = context;
    private ShaderManager? _shaderManager;
    private CompiledShader _shader;
    private ComPtr<ID3D11Buffer> _parameters;
    private ComPtr<ID3D11SamplerState> _sampler;
    private Target? _scene;
    private Target? _halfA;
    private Target? _halfB;
    private uint _width;
    private uint _height;
    private bool _initialized;

    private sealed class Target : IDisposable
    {
        public ComPtr<ID3D11Texture2D> Texture;
        public ComPtr<ID3D11RenderTargetView> RenderTarget;
        public ComPtr<ID3D11ShaderResourceView> ShaderResource;

        public void Dispose()
        {
            ShaderResource.Dispose();
            RenderTarget.Dispose();
            Texture.Dispose();
        }
    }

    public void Initialize(ShaderManager shaderManager)
    {
        ArgumentNullException.ThrowIfNull(shaderManager);
        if (_initialized)
            return;
        _shaderManager = shaderManager;
        _shader = shaderManager.GetOrCompileShader("glow");
        var bufferDescription = new BufferDesc
        {
            ByteWidth = (uint)Marshal.SizeOf<SceneGlowParameters>(),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ConstantBuffer
        };
        SilkMarshal.ThrowHResult(_device.CreateBuffer(
            in bufferDescription, null, ref _parameters));

        var samplerDescription = new SamplerDesc
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MaxAnisotropy = 1,
            MinLOD = float.MinValue,
            MaxLOD = float.MaxValue
        };
        SilkMarshal.ThrowHResult(_device.CreateSamplerState(
            in samplerDescription, ref _sampler));
        _initialized = true;
    }

    public void RefreshShader()
    {
        if (_shaderManager is not null)
            _shader = _shaderManager.GetOrCompileShader("glow");
    }

    public ComPtr<ID3D11RenderTargetView> GetSceneTarget(uint width, uint height)
    {
        if (_scene is null || _width != width || _height != height)
        {
            ReleaseTargets();
            _width = width;
            _height = height;
            _scene = CreateTarget(width, height);
            _halfA = CreateTarget(Math.Max(1, width / 2), Math.Max(1, height / 2));
            _halfB = CreateTarget(Math.Max(1, width / 2), Math.Max(1, height / 2));
        }
        return _scene.RenderTarget;
    }

    private Target CreateTarget(uint width, uint height)
    {
        var target = new Target();
        try
        {
            var description = new Texture2DDesc
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.FormatB8G8R8A8Unorm,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
                BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource)
            };
            SilkMarshal.ThrowHResult(_device.CreateTexture2D(
                in description, null, ref target.Texture));
            SilkMarshal.ThrowHResult(_device.CreateRenderTargetView(
                target.Texture, null, ref target.RenderTarget));
            SilkMarshal.ThrowHResult(_device.CreateShaderResourceView(
                target.Texture, null, ref target.ShaderResource));
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    public unsafe void Composite(ComPtr<ID3D11RenderTargetView> output)
    {
        if (_scene is null || _halfA is null || _halfB is null)
            return;

        // The scene SRV may still be bound by a previous frame; remove any
        // read bindings before targeting the same texture next frame.
        ComPtr<ID3D11ShaderResourceView> nullResource = default;
        _context.PSSetShaderResources(0, 1, ref nullResource);
        _context.PSSetShaderResources(1, 1, ref nullResource);

        ComPtr<ID3D11InputLayout> nullLayout = default;
        ComPtr<ID3D11GeometryShader> nullGeometry = default;
        ComPtr<ID3D11ClassInstance> nullClass = default;
        ComPtr<ID3D11RasterizerState> nullRasterizer = default;
        ComPtr<ID3D11BlendState> nullBlend = default;
        float blendFactor = 1f;
        _context.IASetInputLayout(nullLayout);
        _context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        _context.VSSetShader(_shader.VertexShader, ref nullClass, 0);
        _context.GSSetShader(nullGeometry, ref nullClass, 0);
        _context.PSSetShader(_shader.PixelShader, ref nullClass, 0);
        _context.PSSetConstantBuffers(0, 1, ref _parameters);
        _context.PSSetSamplers(0, 1, ref _sampler);
        _context.RSSetState(nullRasterizer);
        _context.OMSetBlendState(nullBlend, ref blendFactor, uint.MaxValue);

        var halfWidth = Math.Max(1, _width / 2);
        var halfHeight = Math.Max(1, _height / 2);
        DrawPass(0, _halfA.RenderTarget, _scene.ShaderResource, default,
            halfWidth, halfHeight, Vector2.Zero);
        DrawPass(1, _halfB.RenderTarget, _halfA.ShaderResource, default,
            halfWidth, halfHeight, new Vector2(1f / halfWidth, 0));
        DrawPass(2, _halfA.RenderTarget, _halfB.ShaderResource, default,
            halfWidth, halfHeight, new Vector2(0, 1f / halfHeight));
        DrawPass(3, output, _scene.ShaderResource, _halfA.ShaderResource,
            _width, _height, Vector2.Zero);

        _context.PSSetShaderResources(0, 1, ref nullResource);
        _context.PSSetShaderResources(1, 1, ref nullResource);
    }

    private void DrawPass(
        int passIndex,
        ComPtr<ID3D11RenderTargetView> output,
        ComPtr<ID3D11ShaderResourceView> source,
        ComPtr<ID3D11ShaderResourceView> blur,
        uint width,
        uint height,
        Vector2 texelStep)
    {
        ComPtr<ID3D11DepthStencilView> noDepth = default;
        _context.OMSetRenderTargets(1, ref output, noDepth);
        var viewport = new Viewport
        {
            Width = width,
            Height = height,
            MinDepth = 0,
            MaxDepth = 1
        };
        _context.RSSetViewports(1, in viewport);
        var parameters = new SceneGlowParameters
        {
            TexelStep = texelStep,
            Mix = 0f,
            Strength = 0.8f,
            PassIndex = passIndex
        };
        _context.UpdateSubresource(_parameters, 0,
            ref Unsafe.NullRef<Box>(), ref parameters, 0, 0);
        _context.PSSetShaderResources(0, 1, ref source);
        _context.PSSetShaderResources(1, 1, ref blur);
        _context.Draw(3, 0);
        ComPtr<ID3D11ShaderResourceView> nullResource = default;
        _context.PSSetShaderResources(0, 1, ref nullResource);
        _context.PSSetShaderResources(1, 1, ref nullResource);
    }

    public void ReleaseTargets()
    {
        _halfB?.Dispose();
        _halfA?.Dispose();
        _scene?.Dispose();
        _halfB = null;
        _halfA = null;
        _scene = null;
        _width = 0;
        _height = 0;
    }

    public void Dispose()
    {
        ReleaseTargets();
        _sampler.Dispose();
        _parameters.Dispose();
    }
}
