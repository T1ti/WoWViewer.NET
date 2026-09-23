using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using M2MaterialFlags = WoWLib.Formats.M2.Root.Record.MaterialFlags;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct SkyRenderStats(
    uint DrawCalls,
    ulong SubmittedIndices,
    double SubmissionMilliseconds);

[StructLayout(LayoutKind.Sequential)]
internal struct SkyGradientCB
{
    public Vector4 TopColor;
    public Vector4 MiddleColor;
    public Vector4 Band1Color;
    public Vector4 Band2Color;
    public Vector4 SmogColor;
    public Vector4 FogColor;
    public Vector4 CameraFront;
    public Vector4 CameraRight;
    public Vector4 CameraUp;
    public Vector4 ProjectionScale;
}

/// <summary>
/// Owns the camera-centred LightData sky gradient and optional LightSkybox M2
/// override pass. Models use the existing asynchronous M2/BLP caches and never
/// perform client I/O during frame submission.
/// </summary>
internal sealed class SkyRenderer(
    ComPtr<ID3D11Device> device,
    ComPtr<ID3D11DeviceContext> deviceContext) : IDisposable
{
    private const uint CacheOwnerId = 0xFFFF_FFFEu;
    private readonly ComPtr<ID3D11Device> _device = device;
    private readonly ComPtr<ID3D11DeviceContext> _deviceContext = deviceContext;
    private readonly ComPtr<ID3D11BlendState>[] _blendStates = new ComPtr<ID3D11BlendState>[14];
    private readonly ComPtr<ID3D11SamplerState>[] _samplers = new ComPtr<ID3D11SamplerState>[4];
    private CompiledShader _gradientShader;
    private CompiledShader _m2Shader;
    private ComPtr<ID3D11Buffer> _gradientConstantBuffer;
    private ComPtr<ID3D11Buffer> _m2ConstantBuffer;
    private ComPtr<ID3D11Buffer> _instanceBuffer;
    private ComPtr<ID3D11DepthStencilState> _depthDisabledState;
    private ComPtr<ID3D11RasterizerState> _cullState;
    private ComPtr<ID3D11RasterizerState> _twoSidedState;
    private ComPtr<ID3D11ShaderResourceView> _missingTexture;
    private ShaderManager? _shaderManager;
    private WorldSkyLighting _lighting = WorldSkyLighting.None;
    private readonly HashSet<uint> _trackedSkyboxFileDataIds = [];
    private bool _initialized;

    public void Initialize(ShaderManager shaderManager, CompiledShader m2Shader)
    {
        ArgumentNullException.ThrowIfNull(shaderManager);
        if (_initialized)
            return;

        _shaderManager = shaderManager;
        _gradientShader = shaderManager.GetOrCompileShader("sky");
        _m2Shader = m2Shader;
        _missingTexture = BLPLoader.CreatePlaceholderTexture(_device);

        unsafe
        {
            var bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)Marshal.SizeOf<SkyGradientCB>(),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.ConstantBuffer
            };
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in bufferDesc,
                null,
                ref _gradientConstantBuffer));

            bufferDesc.ByteWidth = (uint)Marshal.SizeOf<M2PerObjectCB>();
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in bufferDesc,
                null,
                ref _m2ConstantBuffer));

            var skyboxTransform = Matrix4x4.CreateRotationZ(MathF.PI);
            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)Marshal.SizeOf<Matrix4x4>(),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.VertexBuffer
            };
            var initialData = new SubresourceData { PSysMem = &skyboxTransform };
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in bufferDesc,
                in initialData,
                ref _instanceBuffer));

            var depthDesc = new DepthStencilDesc
            {
                DepthEnable = false,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunc.Always,
                StencilEnable = false
            };
            SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(
                in depthDesc,
                ref _depthDisabledState));

            var rasterizerDesc = new RasterizerDesc
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.Back,
                FrontCounterClockwise = true,
                DepthClipEnable = true
            };
            SilkMarshal.ThrowHResult(_device.CreateRasterizerState(
                in rasterizerDesc,
                ref _cullState));
            rasterizerDesc.CullMode = CullMode.None;
            SilkMarshal.ThrowHResult(_device.CreateRasterizerState(
                in rasterizerDesc,
                ref _twoSidedState));

            CreateSamplers();
            CreateBlendStates();
        }

        _initialized = true;
    }

    public void RefreshShaders()
    {
        if (!_initialized || _shaderManager == null)
            return;
        _gradientShader = _shaderManager.GetOrCompileShader("sky");
        _m2Shader = _shaderManager.GetOrCompileShader("m2");
    }

    public void SetLighting(WorldSkyLighting lighting)
    {
        var normalizedSkyboxes = NormalizeSkyboxes(lighting.Skyboxes);
        _lighting = lighting with { Skyboxes = normalizedSkyboxes };

        var requestedFileDataIds = normalizedSkyboxes
            .Select(static skybox => skybox.FileDataId)
            .ToHashSet();
        var staleFileDataIds = _trackedSkyboxFileDataIds
            .Where(fileDataId => !requestedFileDataIds.Contains(fileDataId))
            .ToArray();
        foreach (var fileDataId in staleFileDataIds)
        {
            M2Cache.Release(fileDataId, CacheOwnerId);
            _trackedSkyboxFileDataIds.Remove(fileDataId);
        }

        foreach (var fileDataId in requestedFileDataIds)
        {
            if (_trackedSkyboxFileDataIds.Add(fileDataId))
                M2Cache.GetOrLoad(_device, fileDataId, CacheOwnerId, keepTrack: true);
        }
    }

    public unsafe SkyRenderStats Render(Camera camera)
    {
        if (!_initialized || (!_lighting.HasColorData && !_lighting.HasSkyboxes))
            return default;

        var started = Stopwatch.GetTimestamp();
        uint drawCalls = 0;
        ulong submittedIndices = 0;
        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        ComPtr<ID3D11GeometryShader> nullGeometryShader = default;
        _deviceContext.GSSetShader(nullGeometryShader, ref nullClassInstance, 0);
        _deviceContext.OMSetDepthStencilState(_depthDisabledState, 0);

        if (_lighting.HasColorData)
        {
            var projection = camera.GetProjectionMatrix();
            var overrideWithFog = _lighting.OverrideColorsWithFog;
            var topColor = overrideWithFog ? _lighting.FogColor : _lighting.TopColor;
            var middleColor = overrideWithFog ? _lighting.FogColor : _lighting.MiddleColor;
            var band1Color = overrideWithFog ? _lighting.FogColor : _lighting.Band1Color;
            var band2Color = overrideWithFog ? _lighting.FogColor : _lighting.Band2Color;
            var smogColor = overrideWithFog ? _lighting.FogColor : _lighting.SmogColor;
            var constants = new SkyGradientCB
            {
                TopColor = new Vector4(topColor, 1f),
                MiddleColor = new Vector4(middleColor, 1f),
                Band1Color = new Vector4(band1Color, 1f),
                Band2Color = new Vector4(band2Color, 1f),
                SmogColor = new Vector4(smogColor, 1f),
                FogColor = new Vector4(_lighting.FogColor, 1f),
                CameraFront = new Vector4(camera.Front, 0f),
                CameraRight = new Vector4(camera.Right, 0f),
                CameraUp = new Vector4(camera.Up, 0f),
                ProjectionScale = new Vector4(1f / projection.M11, 1f / projection.M22, 0f, 0f)
            };
            _deviceContext.UpdateSubresource(
                _gradientConstantBuffer,
                0,
                ref Unsafe.NullRef<Box>(),
                ref constants,
                0,
                0);
            ComPtr<ID3D11InputLayout> noInputLayout = default;
            _deviceContext.IASetInputLayout(noInputLayout);
            _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
            _deviceContext.VSSetShader(_gradientShader.VertexShader, ref nullClassInstance, 0);
            _deviceContext.PSSetShader(_gradientShader.PixelShader, ref nullClassInstance, 0);
            _deviceContext.PSSetConstantBuffers(0, 1, ref _gradientConstantBuffer);
            ApplyBlendMode(0);
            _deviceContext.RSSetState(_twoSidedState);
            _deviceContext.Draw(3, 0);
            drawCalls++;
        }

        foreach (var skybox in _lighting.Skyboxes)
        {
            if (skybox.FileDataId == 0 || skybox.Opacity <= 0f)
                continue;

            var model = M2Cache.GetOrLoad(
                _device,
                skybox.FileDataId,
                CacheOwnerId,
                keepTrack: false);
            if (model.fileDataID == skybox.FileDataId &&
                model.vertexBuffer.Handle != null &&
                model.indiceBuffer.Handle != null)
            {
                RenderSkyboxModel(
                    camera,
                    model,
                    skybox.Opacity,
                    ref drawCalls,
                    ref submittedIndices);
            }
        }

        ComPtr<ID3D11BlendState> defaultBlend = default;
        float blendFactor = 1f;
        _deviceContext.OMSetBlendState(defaultBlend, ref blendFactor, uint.MaxValue);
        ComPtr<ID3D11DepthStencilState> defaultDepth = default;
        _deviceContext.OMSetDepthStencilState(defaultDepth, 0);

        return new SkyRenderStats(
            drawCalls,
            submittedIndices,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private void RenderSkyboxModel(
        Camera camera,
        ParsedDoodadBatch model,
        float opacity,
        ref uint drawCalls,
        ref ulong submittedIndices)
    {
        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        var view = camera.GetViewMatrix();
        view.M41 = 0f;
        view.M42 = 0f;
        view.M43 = 0f;
        var constants = new M2PerObjectCB
        {
            projection_matrix = camera.GetProjectionMatrix(),
            view_matrix = view,
            model_matrix = Matrix4x4.Identity,
            texMatrix1 = Matrix4x4.Identity,
            texMatrix2 = Matrix4x4.Identity,
            lightDirection = Vector3.UnitZ,
            ambientColor = Vector3.One,
            diffuseColor = Vector3.Zero,
            materialColor = Vector4.One,
            globalOpacity = opacity
        };

        _deviceContext.IASetInputLayout(_m2Shader.InputLayout);
        _deviceContext.VSSetShader(_m2Shader.VertexShader, ref nullClassInstance, 0);
        _deviceContext.PSSetShader(_m2Shader.PixelShader, ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _m2ConstantBuffer);
        _deviceContext.PSSetConstantBuffers(0, 1, ref _m2ConstantBuffer);

        var vertexStride = (uint)Marshal.SizeOf<M2Vertex>();
        var vertexOffset = 0u;
        var instanceStride = (uint)Marshal.SizeOf<Matrix4x4>();
        var instanceOffset = 0u;
        var vertexBuffer = model.vertexBuffer;
        var indexBuffer = model.indiceBuffer;
        _deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in vertexStride, in vertexOffset);
        _deviceContext.IASetVertexBuffers(1, 1, ref _instanceBuffer, in instanceStride, in instanceOffset);
        _deviceContext.IASetIndexBuffer(indexBuffer, Format.FormatR16Uint, 0);

        foreach (var batch in model.submeshes)
        {
            constants.vertexShader = checked((int)batch.vertexShaderID);
            constants.pixelShader = checked((int)batch.pixelShaderID);
            constants.blendMode = batch.blendType;
            constants.alphaRef = batch.blendType == 1 ? 128f / 255f : -1f;
            _deviceContext.UpdateSubresource(
                _m2ConstantBuffer,
                0,
                ref Unsafe.NullRef<Box>(),
                ref constants,
                0,
                0);

            _deviceContext.RSSetState(IsTwoSided(batch.renderFlags) ? _twoSidedState : _cullState);
            ApplySkyboxBlendMode(checked((int)batch.blendType), opacity);

            for (var index = 0; index < batch.material.Length; index++)
            {
                var texture = batch.material[index] == 0
                    ? _missingTexture
                    : BLPCache.GetCurrent(batch.material[index], _missingTexture);
                _deviceContext.PSSetShaderResources((uint)index, 1, ref texture);
                var flags = batch.textureFlags is { } textureFlags && index < textureFlags.Length
                    ? textureFlags[index]
                    : 0;
                var sampler = _samplers[GetSamplerIndex(flags)];
                _deviceContext.PSSetSamplers((uint)index, 1, ref sampler);
            }

            _deviceContext.DrawIndexedInstanced(batch.numFaces, 1, batch.firstFace, 0, 0);
            drawCalls++;
            submittedIndices += batch.numFaces;
        }
    }

    private unsafe void CreateSamplers()
    {
        for (var wrapX = 0; wrapX <= 1; wrapX++)
        {
            for (var wrapY = 0; wrapY <= 1; wrapY++)
            {
                var samplerDesc = new SamplerDesc
                {
                    Filter = Filter.MinMagMipLinear,
                    AddressU = wrapX != 0 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                    AddressV = wrapY != 0 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    MaxAnisotropy = 1,
                    MinLOD = float.MinValue,
                    MaxLOD = float.MaxValue
                };
                samplerDesc.BorderColor[3] = 1f;
                var index = (wrapX << 1) | wrapY;
                SilkMarshal.ThrowHResult(_device.CreateSamplerState(in samplerDesc, ref _samplers[index]));
            }
        }
    }

    private unsafe void CreateBlendStates()
    {
        static RenderTargetBlendDesc Make(bool enabled, Blend source, Blend destination, Blend sourceAlpha, Blend destinationAlpha) => new()
        {
            BlendEnable = enabled ? (Silk.NET.Core.Bool32)1 : (Silk.NET.Core.Bool32)0,
            SrcBlend = source,
            DestBlend = destination,
            BlendOp = BlendOp.Add,
            SrcBlendAlpha = sourceAlpha,
            DestBlendAlpha = destinationAlpha,
            BlendOpAlpha = BlendOp.Add,
            RenderTargetWriteMask = (byte)ColorWriteEnable.All
        };

        (bool Enabled, Blend Source, Blend Destination, Blend SourceAlpha, Blend DestinationAlpha)[] configurations =
        [
            (false, Blend.One, Blend.Zero, Blend.One, Blend.Zero),
            (false, Blend.One, Blend.Zero, Blend.One, Blend.Zero),
            (true, Blend.SrcAlpha, Blend.InvSrcAlpha, Blend.SrcAlpha, Blend.InvSrcAlpha),
            (true, Blend.SrcAlpha, Blend.One, Blend.Zero, Blend.One),
            (true, Blend.DestColor, Blend.Zero, Blend.DestAlpha, Blend.Zero),
            (true, Blend.DestColor, Blend.SrcColor, Blend.DestAlpha, Blend.SrcAlpha),
            (true, Blend.DestColor, Blend.One, Blend.DestAlpha, Blend.One),
            (true, Blend.InvSrcAlpha, Blend.One, Blend.InvSrcAlpha, Blend.One),
            (true, Blend.InvSrcAlpha, Blend.Zero, Blend.InvSrcAlpha, Blend.Zero),
            (true, Blend.SrcAlpha, Blend.Zero, Blend.SrcAlpha, Blend.Zero),
            (true, Blend.One, Blend.One, Blend.Zero, Blend.One),
            (true, Blend.BlendFactor, Blend.InvBlendFactor, Blend.BlendFactor, Blend.InvBlendFactor),
            (true, Blend.InvDestColor, Blend.One, Blend.One, Blend.Zero),
            (true, Blend.One, Blend.InvSrcAlpha, Blend.One, Blend.InvSrcAlpha)
        ];

        for (var index = 0; index < configurations.Length; index++)
        {
            var configuration = configurations[index];
            var blendDesc = new BlendDesc { AlphaToCoverageEnable = 0, IndependentBlendEnable = 0 };
            blendDesc.RenderTarget[0] = Make(
                configuration.Enabled,
                configuration.Source,
                configuration.Destination,
                configuration.SourceAlpha,
                configuration.DestinationAlpha);
            SilkMarshal.ThrowHResult(_device.CreateBlendState(in blendDesc, ref _blendStates[index]));
        }
    }

    private unsafe void ApplyBlendMode(int blendMode)
    {
        if ((uint)blendMode >= (uint)_blendStates.Length)
            blendMode = 0;
        var blendFactor = 1f;
        _deviceContext.OMSetBlendState(_blendStates[blendMode], ref blendFactor, uint.MaxValue);
    }

    private void ApplySkyboxBlendMode(int blendMode, float opacity)
    {
        // Opaque and alpha-key materials need an alpha blend state while a
        // whole skybox is crossfading. Other material modes already consume
        // the shader opacity through their native blend equations.
        if (opacity < 0.9999f && blendMode is 0 or 1)
            blendMode = 2;
        ApplyBlendMode(blendMode);
    }

    private static IReadOnlyList<WorldSkyboxLayer> NormalizeSkyboxes(
        IReadOnlyList<WorldSkyboxLayer>? skyboxes)
    {
        if (skyboxes == null || skyboxes.Count == 0)
            return Array.Empty<WorldSkyboxLayer>();

        var normalized = new List<WorldSkyboxLayer>(skyboxes.Count);
        foreach (var skybox in skyboxes)
        {
            if (skybox.FileDataId == 0)
                continue;

            var opacity = Math.Clamp(skybox.Opacity, 0f, 1f);
            if (opacity <= 0.0001f)
                continue;

            var existingIndex = normalized.FindIndex(
                layer => layer.FileDataId == skybox.FileDataId);
            if (existingIndex < 0)
            {
                normalized.Add(skybox with { Opacity = opacity });
                continue;
            }

            var existing = normalized[existingIndex];
            normalized[existingIndex] = existing with
            {
                Flags = existing.Flags | skybox.Flags,
                Opacity = Math.Clamp(existing.Opacity + opacity, 0f, 1f)
            };
        }

        return normalized.Count == 0
            ? Array.Empty<WorldSkyboxLayer>()
            : normalized.ToArray();
    }

    private static bool IsTwoSided(ushort renderFlags) =>
        (renderFlags & (ushort)M2MaterialFlags.TwoSided) != 0;

    private static int GetSamplerIndex(uint textureFlags) =>
        ((textureFlags & 0x1) != 0 ? 2 : 0) |
        ((textureFlags & 0x2) != 0 ? 1 : 0);

    public void Dispose()
    {
        foreach (var fileDataId in _trackedSkyboxFileDataIds)
            M2Cache.Release(fileDataId, CacheOwnerId);
        _trackedSkyboxFileDataIds.Clear();
        foreach (var blendState in _blendStates)
            blendState.Dispose();
        foreach (var sampler in _samplers)
            sampler.Dispose();
        _missingTexture.Dispose();
        _twoSidedState.Dispose();
        _cullState.Dispose();
        _depthDisabledState.Dispose();
        _instanceBuffer.Dispose();
        _m2ConstantBuffer.Dispose();
        _gradientConstantBuffer.Dispose();
    }
}
