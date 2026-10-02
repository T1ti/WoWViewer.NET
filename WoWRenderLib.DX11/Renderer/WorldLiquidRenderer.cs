using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WoWRenderLib.Cache;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct WorldLiquidRenderStats(
    int CandidateBatches,
    int VisibleBatches,
    uint DrawCalls,
    ulong SubmittedIndices,
    double CullingMilliseconds,
    double SubmissionMilliseconds);

internal readonly record struct WmoLiquidInstance(WMOContainer Instance, int GroupIndex);

internal readonly record struct WorldLiquidFrame(Camera Camera, long TimeMilliseconds,
    Vector3 LightDirection, Vector3 AmbientColor, Vector3 DiffuseColor,
    WorldLightingSettings Lighting, Vector3 SpecularColor, bool Specular);

[StructLayout(LayoutKind.Sequential)]
internal struct WorldLiquidPerObjectCB
{
    public Matrix4x4 Model;
    public Matrix4x4 View;
    public Matrix4x4 Projection;
    public Vector4 ShallowColor;
    public Vector4 DeepColor;
    public Vector4 FlowParameters;
    public Vector4 FamilyParameters;
    public Vector4 LightingAmbient;
    public Vector4 LightingDiffuse;
    public Vector4 OceanCloseColor;
    public Vector4 OceanFarColor;
    public Vector4 RiverCloseColor;
    public Vector4 RiverFarColor;
    public Vector4 LiquidColorParameters;
    public Vector4 DepthCoefficients;
    public Vector4 LightDirection;
    public Vector4 LiquidAlphaParameters;
    public Vector4 WmoWaterColor;
    public Vector4 WmoParameters;
    public Vector4 NativeParameters; // program, surface scale, rotation, depth scale
    public Vector4 NativeOffset; // magma scroll XY, gradient U, generated WMO UV
    public Vector4 NativeVertexColor;
    public Vector4 NativeSpecular;
}

/// <summary>
/// Owns liquid-specific DX11 state and submission. SceneManager remains responsible
/// for pass ordering and supplies visible ADT and WMO liquid sources.
/// </summary>
internal sealed class WorldLiquidRenderer(
    ComPtr<ID3D11Device> device,
    ComPtr<ID3D11DeviceContext> deviceContext) : IDisposable
{
    private readonly ComPtr<ID3D11Device> _device = device;
    private readonly ComPtr<ID3D11DeviceContext> _deviceContext = deviceContext;
    private CompiledShader _shader;
    private ComPtr<ID3D11Buffer> _constantBuffer;
    private ComPtr<ID3D11RasterizerState> _rasterizerState;
    private ComPtr<ID3D11DepthStencilState> _depthStencilState;
    private ComPtr<ID3D11DepthStencilState> _opaqueDepthStencilState;
    private ComPtr<ID3D11BlendState> _alphaBlendState;
    private ComPtr<ID3D11BlendState> _opaqueBlendState;
    private ComPtr<ID3D11ShaderResourceView> _missingTexture;
    private ComPtr<ID3D11SamplerState> _surfaceSampler, _gradientSampler;
    private readonly Wrath335LiquidTextures _nativeTextures = new(device, deviceContext);
    private sealed class SlotState { public bool Ready; }
    private readonly ConditionalWeakTable<WorldLiquidTextureSlot, SlotState> _slotStates = new();
    private int _currentBlend = -1;
    private readonly List<VisibleBatch> _visibleBatches = new(256);
    private readonly List<WorldLiquidSortKey> _visibleBatchOrder = new(256);
    private readonly WorldLiquidBoundsCache _wmoBoundsCache = new();
    private readonly Wrath335LiquidInstances _instances = new();
    private readonly HashSet<uint> _submittedInstances = [];
    private static readonly Comparison<WorldLiquidSortKey> SortBatches =
        WorldLiquidBatchOrdering.Compare;
    private ShaderManager? _shaderManager;
    private bool _initialized;

    private readonly record struct VisibleBatch(
        object Owner,
        WorldLiquidResources Liquid,
        Matrix4x4 Model,
        int BatchIndex);

    public void Initialize(ShaderManager shaderManager)
    {
        ArgumentNullException.ThrowIfNull(shaderManager);
        if (_initialized)
            return;

        _shaderManager = shaderManager;
        _shader = shaderManager.GetOrCompileShader("liquid");
        // Keep the diagnostic placeholder visible until the real BLP finishes
        // decoding.  This makes an unresolved LiquidTypeXTexture link obvious
        // while still allowing the cache to replace it with the loaded asset.
        _missingTexture = BLPLoader.CreatePlaceholderTexture(_device);

        unsafe
        {
            var bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)Marshal.SizeOf<WorldLiquidPerObjectCB>(),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.ConstantBuffer
            };
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in bufferDesc,
                null,
                ref _constantBuffer));

            var rasterizerDesc = new RasterizerDesc
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.None,
                FrontCounterClockwise = false,
                DepthClipEnable = true
            };
            SilkMarshal.ThrowHResult(_device.CreateRasterizerState(
                in rasterizerDesc,
                ref _rasterizerState));

            var depthDesc = new DepthStencilDesc
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunc.LessEqual,
                StencilEnable = false
            };
            SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(
                in depthDesc,
                ref _depthStencilState));

            depthDesc.DepthWriteMask = DepthWriteMask.All;
            SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(
                in depthDesc,
                ref _opaqueDepthStencilState));

            var alphaDesc = new BlendDesc
            {
                AlphaToCoverageEnable = 0,
                IndependentBlendEnable = 0
            };
            alphaDesc.RenderTarget[0] = new RenderTargetBlendDesc
            {
                BlendEnable = (Silk.NET.Core.Bool32)1,
                SrcBlend = Blend.SrcAlpha,
                DestBlend = Blend.InvSrcAlpha,
                BlendOp = BlendOp.Add,
                SrcBlendAlpha = Blend.One,
                DestBlendAlpha = Blend.InvSrcAlpha,
                BlendOpAlpha = BlendOp.Add,
                RenderTargetWriteMask = (byte)ColorWriteEnable.All
            };
            SilkMarshal.ThrowHResult(_device.CreateBlendState(
                in alphaDesc,
                ref _alphaBlendState));

            var opaqueDesc = new BlendDesc
            {
                AlphaToCoverageEnable = 0,
                IndependentBlendEnable = 0
            };
            opaqueDesc.RenderTarget[0] = new RenderTargetBlendDesc
            {
                BlendEnable = (Silk.NET.Core.Bool32)0,
                SrcBlend = Blend.One,
                DestBlend = Blend.Zero,
                BlendOp = BlendOp.Add,
                SrcBlendAlpha = Blend.One,
                DestBlendAlpha = Blend.Zero,
                BlendOpAlpha = BlendOp.Add,
                RenderTargetWriteMask = (byte)ColorWriteEnable.All
            };
            SilkMarshal.ThrowHResult(_device.CreateBlendState(
                in opaqueDesc,
                ref _opaqueBlendState));

            var sampler = new SamplerDesc
            {
                Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap,
                MaxLOD = float.MaxValue
            };
            SilkMarshal.ThrowHResult(_device.CreateSamplerState(in sampler, ref _surfaceSampler));
            sampler.AddressU = sampler.AddressV = sampler.AddressW = TextureAddressMode.Clamp;
            SilkMarshal.ThrowHResult(_device.CreateSamplerState(in sampler, ref _gradientSampler));
        }

        _initialized = true;
    }

    public void RefreshShader()
    {
        if (_initialized && _shaderManager != null)
            _shader = _shaderManager.GetOrCompileShader("liquid");
    }

    public WorldLiquidRenderStats Prepare(
        Camera camera,
        IReadOnlyList<ADTContainer> adtContainers,
        IReadOnlyList<WmoLiquidInstance> wmoLiquids,
        IReadOnlySet<int> coarseCulledTileIndices,
        float renderDistance,
        float wmoRenderDistance)
    {
        _visibleBatches.Clear();
        _visibleBatchOrder.Clear();
        _submittedInstances.Clear();
        if (!_initialized || (adtContainers.Count == 0 && wmoLiquids.Count == 0))
            return default;

        var cullingStarted = Stopwatch.GetTimestamp();
        var view = camera.GetViewMatrix();
        var frustum = camera.GetFrustum();
        var cameraPosition = camera.Position;
        var candidateCount = 0;
        var tileOrder = 0;

        foreach (var container in adtContainers)
        {
            tileOrder++;
            if (!container.IsLoaded)
                continue;

            var liquid = container.Terrain.worldLiquid;
            if (!liquid.HasGeometry)
                continue;

            var batches = liquid.batches;
            var batchSpheres = liquid.batchSpheres;
            candidateCount += batches.Length;
            // Allocate the generation before visibility selection, so camera
            // movement does not assign identities in a new order.
            _instances.Get(container, batches, 0);
            if (coarseCulledTileIndices.Contains(container.mapTile.PositionIndex))
                continue;
            var modelMatrix = default(Matrix4x4);
            var hasModelMatrix = false;
            for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                var batch = batches[batchIndex];

                var sphere = batchSpheres[batchIndex];
                if (!IsWithinRenderDistance(cameraPosition, sphere.Center, sphere.Radius, renderDistance))
                    continue;
                var bounds = batch.Bounds;
                if (frustum.ClassifyAxisAlignedBox(bounds.Min, bounds.Max) == Frustum.BoxIntersection.Outside)
                    continue;

                if (!hasModelMatrix)
                {
                    modelMatrix = container.GetModelMatrix();
                    hasModelMatrix = true;
                }
                var viewCenter = Vector3.Transform(bounds.Center, view).Z;
                var visibleIndex = _visibleBatches.Count;
                var material = Material(liquid, batch);
                var identity = material.Wrath335 != null ? _instances.Get(container, batches, batchIndex) : 0;
                if (identity != 0 && !_submittedInstances.Add(identity)) continue;
                _visibleBatches.Add(new VisibleBatch(
                    container,
                    liquid,
                    modelMatrix,
                    batchIndex));
                _visibleBatchOrder.Add(new WorldLiquidSortKey(
                    visibleIndex, viewCenter, tileOrder,
                    batch.ChunkIndex, batch.LayerIndex, Wrath335LiquidInstances.Phase(material), identity));
            }
        }

        foreach (var wmoLiquid in wmoLiquids)
        {
            var model = wmoLiquid.Instance.GetWMO();
            if ((uint)wmoLiquid.GroupIndex >= (uint)model.groupBatches.Length)
                continue;
            var liquid = model.groupBatches[wmoLiquid.GroupIndex].liquid;
            if (!liquid.HasGeometry)
                continue;
            var matrix = wmoLiquid.Instance.GetModelMatrix();
            var transformedBounds = _wmoBoundsCache.GetOrUpdate(
                wmoLiquid.Instance, wmoLiquid.GroupIndex, liquid.batches, matrix);
            _instances.Get(wmoLiquid.Instance, liquid.batches, 0);
            tileOrder++;
            for (var batchIndex = 0; batchIndex < liquid.batches.Length; batchIndex++)
            {
                var batch = liquid.batches[batchIndex];
                candidateCount++;
                var cullBounds = transformedBounds[batchIndex];
                var sphere = cullBounds.Sphere;
                if (!IsWithinRenderDistance(cameraPosition, sphere.Center, sphere.Radius, wmoRenderDistance))
                    continue;
                var bounds = cullBounds.Box;
                if (frustum.ClassifyAxisAlignedBox(bounds.Min, bounds.Max) == Frustum.BoxIntersection.Outside)
                    continue;
                var visibleIndex = _visibleBatches.Count;
                var material = Material(liquid, batch);
                var identity = material.Wrath335 != null ? _instances.Get(wmoLiquid.Instance, liquid.batches, batchIndex) : 0;
                if (identity != 0 && !_submittedInstances.Add(identity)) continue;
                _visibleBatches.Add(new VisibleBatch(
                    liquid.batches, liquid, matrix, batchIndex));
                _visibleBatchOrder.Add(new WorldLiquidSortKey(
                    visibleIndex, Vector3.Transform(bounds.Center, view).Z,
                    tileOrder, wmoLiquid.GroupIndex, 0, Wrath335LiquidInstances.Phase(material), identity));
            }
        }

        if (_visibleBatchOrder.Count > 1)
            _visibleBatchOrder.Sort(SortBatches);
        var cullingMilliseconds = Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;

        return new(candidateCount, _visibleBatches.Count, 0, 0, cullingMilliseconds, 0);
    }

    /// <summary>Draw a prepared native list once; restores blend/depth/raster and unbinds borrowed textures.</summary>
    public WorldLiquidRenderStats RenderPrepared(in WorldLiquidFrame frame, WorldLiquidDrawPhase phase)
    {
        if (!_initialized || _visibleBatchOrder.Count == 0) return default;
        var view = frame.Camera.GetViewMatrix();
        var projection = frame.Camera.GetProjectionMatrix();
        var timeMilliseconds = frame.TimeMilliseconds;
        var lightDirection = frame.LightDirection;
        var ambientColor = frame.AmbientColor;
        var diffuseColor = frame.DiffuseColor;
        var clientLighting = frame.Lighting;
        var specularColor = frame.SpecularColor;
        var specular = frame.Specular;

        var submissionStarted = Stopwatch.GetTimestamp();
        _nativeTextures.Update(clientLighting);
        BeginSubmission();

        var drawCalls = 0U;
        ulong submittedIndices = 0;
        var useClientLiquidColors = clientLighting.HasLiquidColorData;
        var timeSeconds = (float)(timeMilliseconds * 0.001);
        var oceanCloseColor = useClientLiquidColors
            ? clientLighting.OceanCloseColor
            : Vector3.Zero;
        var oceanFarColor = useClientLiquidColors
            ? clientLighting.OceanFarColor
            : Vector3.Zero;
        var riverCloseColor = useClientLiquidColors
            ? clientLighting.RiverCloseColor
            : Vector3.Zero;
        var riverFarColor = useClientLiquidColors
            ? clientLighting.RiverFarColor
            : Vector3.Zero;
        var lightingAmbient = new Vector4(ClampColor(ambientColor), 0f);
        var lightingDiffuse = new Vector4(ClampColor(diffuseColor), 0f);
        var clampedRiverCloseColor = ClampColor(riverCloseColor);
        var oceanCloseLighting = new Vector4(ClampColor(oceanCloseColor), 1f);
        var oceanFarLighting = new Vector4(ClampColor(oceanFarColor), 1f);
        var riverCloseLighting = new Vector4(clampedRiverCloseColor, 1f);
        var riverFarLighting = new Vector4(ClampColor(riverFarColor), 1f);
        var normalizedLightDirection = new Vector4(NormalizeLightDirection(lightDirection), 0f);
        var oceanShallowAlpha = clientLighting.HasLiquidAlphaData
            ? clientLighting.OceanShallowAlpha
            : 1f;
        var oceanDeepAlpha = clientLighting.HasLiquidAlphaData
            ? clientLighting.OceanDeepAlpha
            : 1f;
        var riverShallowAlpha = clientLighting.HasLiquidAlphaData
            ? clientLighting.WaterShallowAlpha
            : 1f;
        var riverDeepAlpha = clientLighting.HasLiquidAlphaData
            ? clientLighting.WaterDeepAlpha
            : 1f;
        object? boundOwner = null;
        var vertexStride = (uint)Marshal.SizeOf<WorldLiquidVertex>();
        var vertexOffset = 0U;

        try
        {
            for (var orderIndex = 0; orderIndex < _visibleBatchOrder.Count; orderIndex++)
            {
                var sortKey = _visibleBatchOrder[orderIndex];
                if (sortKey.Phase != phase || sortKey.VisibleIndex < 0) continue;
                // Consume before drawing: repeat flushes and texture-not-ready
                // entries cannot submit the same instance again in this frame.
                _visibleBatchOrder[orderIndex] = sortKey with { VisibleIndex = -1 };
                var visible = _visibleBatches[sortKey.VisibleIndex];
                var liquid = visible.Liquid;
                var batch = liquid.batches[visible.BatchIndex];
                if (!ReferenceEquals(boundOwner, visible.Owner))
                {
                    var vertexBuffer = liquid.vertexBuffer;
                    _deviceContext.IASetVertexBuffers(
                        0,
                        1,
                        ref vertexBuffer,
                        in vertexStride,
                        in vertexOffset);
                    _deviceContext.IASetIndexBuffer(
                        liquid.indexBuffer,
                        Format.FormatR32Uint,
                        0);
                    boundOwner = visible.Owner;
                }

                var material = Material(liquid, batch);
                var isWater = IsWaterMaterial(material);
                var native = material.Wrath335;
                var program = native != null ? Wrath335Liquid.Program(native.MaterialId, specular) : 0;
                var isOpaque = program != 0 ? program == 3 :
                    !batch.IsWmo && UsesOpaqueComposition(material.Family);

                // 12340 terrain and WMO share animated slot-zero surface data.
                // Other clients retain the existing surface fallback selection.
                var wmoFrames = material.TextureSlots is { Length: > 0 }
                    ? material.TextureSlots[0].Frames
                    : [];
                var textureId = program != 0
                    ? wmoFrames.Length > 0
                        ? wmoFrames[Wrath335Liquid.Frame(timeMilliseconds,
                            program == 3 ? 1250u : native!.AnimationPeriodMilliseconds, wmoFrames.Length)]
                        : 0u
                    : batch.IsWmo
                    ? wmoFrames.Length > 0
                        ? wmoFrames[SelectWmoFrame(timeMilliseconds,
                            material.WmoAnimationPeriodMilliseconds, wmoFrames.Length)]
                        : 0u
                    : material.TextureFileDataIds is { Length: > 0 }
                        ? material.TextureFileDataIds[0]
                        : 0u;
                ComPtr<ID3D11ShaderResourceView> texture = default;
                var hasLoadedTexture = program != 0
                    ? material.TextureSlots.Length > 0 && TryResolveNativeSlot(material.TextureSlots[0],
                        timeMilliseconds, program == 3 ? 1250u : native!.AnimationPeriodMilliseconds, out texture)
                    : textureId != 0 && BLPCache.TryGetLoaded(textureId, out texture);
                if (program != 0 && !hasLoadedTexture)
                    continue; // Native draws wait for all animated frames to become resident.
                if (!hasLoadedTexture)
                    texture = _missingTexture;
                ComPtr<ID3D11ShaderResourceView> gradient = _missingTexture;
                var clampGradient = false;
                if (program is 1 or 2)
                {
                    if (material.TextureSlots.Length > 1)
                    {
                        var slot = material.TextureSlots[1];
                        if (slot.ProceduralDepth != WorldLiquidWaterType.Unknown)
                        {
                            gradient = _nativeTextures.Get(slot.ProceduralDepth);
                            clampGradient = true;
                        }
                        else if (slot.Frames.Length > 0)
                        {
                            if (!TryResolveNativeSlot(slot, timeMilliseconds, 1250, out gradient))
                                continue;
                        }
                        else continue;
                    }
                    else continue;
                }

                var cb = new WorldLiquidPerObjectCB
                {
                    Model = visible.Model,
                    View = view,
                    Projection = projection,
                    ShallowColor = material.ShallowColor,
                    DeepColor = material.DeepColor,
                    FlowParameters = new Vector4(
                        timeSeconds,
                        material.UvScale,
                        material.FlowDirectionRadians,
                        material.FlowSpeed),
                    FamilyParameters = new Vector4(
                        isWater ? 1f : 0f,
                        material.Family == WorldLiquidMaterialFamily.Magma ? 1f : 0f,
                        hasLoadedTexture ? 1f : 0f,
                        batch.IsWmo ? 1f : 0f),
                    LightingAmbient = lightingAmbient,
                    LightingDiffuse = lightingDiffuse,
                    // Keep RGB and alpha as separate inputs. The simple pass uses
                    // standard source-alpha blending to approximate the reference
                    // shader's water-tint-over-scene/refraction mix.
                    OceanCloseColor = oceanCloseLighting,
                    OceanFarColor = oceanFarLighting,
                    RiverCloseColor = riverCloseLighting,
                    RiverFarColor = riverFarLighting,
                    LiquidColorParameters = new Vector4(
                        useClientLiquidColors && isWater && !batch.IsWmoInterior ? 1f : 0f,
                        UsesRiverLightingPalette(material.WaterType) ? 1f : 0f,
                        clientLighting.HasLiquidAlphaData && isWater ? 1f : 0f,
                        batch.IsWmoInterior ? 1f : 0f),
                    DepthCoefficients = material.DepthCoefficients,
                    LightDirection = normalizedLightDirection,
                    LiquidAlphaParameters = new Vector4(
                        oceanShallowAlpha,
                        oceanDeepAlpha,
                        riverShallowAlpha,
                        riverDeepAlpha),
                    WmoWaterColor = new Vector4(
                        batch.IsWmoInterior
                            ? Vector3.One
                            : useClientLiquidColors
                                ? clampedRiverCloseColor
                                : WorldLiquidColorDefaults.RiverClose,
                        0f),
                    WmoParameters = new Vector4(
                        material.WmoBasicClass,
                        material.WmoTextureRotation,
                        riverShallowAlpha,
                        riverDeepAlpha),
                    NativeParameters = native != null ? new Vector4(program,
                        native.TextureScale, native.TextureRotation, native.DepthScale) : default,
                    NativeOffset = native != null ? new Vector4(
                        program == 3 ? Wrath335Liquid.Scroll(timeMilliseconds, native.TextureScale) : 0,
                        program == 3 ? Wrath335Liquid.Scroll(timeMilliseconds, native.TextureRotation) : 0,
                        batch.IsWmoInterior ? 1 : 0,
                        batch.IsWmo && material.WmoVertexFormat != 1 ? 1 : 0) : default,
                    NativeVertexColor = batch.IsWmo ? material.ShallowColor : Vector4.One,
                    NativeSpecular = new Vector4(Wrath335Liquid.Specular(specularColor, batch.IsWmoInterior), 6)
                };
                SubmitBatch(cb, batch.IndexCount, batch.FirstIndex, isOpaque, texture, gradient, clampGradient);
                drawCalls++;
                submittedIndices += batch.IndexCount;
            }
        }
        finally { EndSubmission(); }
        return new(0, 0, drawCalls, submittedIndices, 0,
            Stopwatch.GetElapsedTime(submissionStarted).TotalMilliseconds);
    }

    public void Dispose()
    {
        _nativeTextures.Dispose();
        _gradientSampler.Dispose();
        _surfaceSampler.Dispose();
        _missingTexture.Dispose();
        _opaqueBlendState.Dispose();
        _alphaBlendState.Dispose();
        _opaqueDepthStencilState.Dispose();
        _depthStencilState.Dispose();
        _rasterizerState.Dispose();
        _constantBuffer.Dispose();
        _visibleBatches.Clear();
        _visibleBatchOrder.Clear();
        _slotStates.Clear();
        _instances.Clear();
        _submittedInstances.Clear();
        _initialized = false;
    }

    internal void BeginSubmission()
    {
        _currentBlend = -1;
        _deviceContext.RSSetState(_rasterizerState);
        _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        _deviceContext.IASetInputLayout(_shader.InputLayout);
        ComPtr<ID3D11ClassInstance> instance = default;
        _deviceContext.VSSetShader(_shader.VertexShader, ref instance, 0);
        _deviceContext.PSSetShader(_shader.PixelShader, ref instance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _constantBuffer);
        _deviceContext.PSSetConstantBuffers(0, 1, ref _constantBuffer);
        _deviceContext.PSSetSamplers(0, 1, ref _surfaceSampler);
        _deviceContext.PSSetSamplers(1, 1, ref _gradientSampler);
    }

    private bool TryResolveNativeSlot(WorldLiquidTextureSlot slot, long time, uint period,
        out ComPtr<ID3D11ShaderResourceView> texture)
    {
        texture = default;
        if (slot.Frames.Length == 0) return false;
        var state = _slotStates.GetValue(slot, static _ => new SlotState());
        if (!state.Ready)
        {
            foreach (var frame in slot.Frames)
                if (frame == 0 || !BLPCache.TryGetLoaded(frame, out _)) return false;
            state.Ready = true;
        }
        if (BLPCache.TryGetLoaded(slot.Frames[Wrath335Liquid.Frame(time, period, slot.Frames.Length)], out texture))
            return true;
        state.Ready = false;
        return false;
    }

    // Textures and geometry are borrowed. Caller owns the render target, fog
    // bank and IA buffers; the same submission entry is exercised by WARP.
    internal void SubmitBatch(WorldLiquidPerObjectCB cb, uint count, uint first,
        bool opaque, ComPtr<ID3D11ShaderResourceView> surface,
        ComPtr<ID3D11ShaderResourceView> gradient, bool clampGradient = true)
    {
        var key = opaque ? 1 : 2;
        if (_currentBlend != key)
        {
            var factor = 1f;
            _deviceContext.OMSetDepthStencilState(opaque ? _opaqueDepthStencilState : _depthStencilState, 0);
            _deviceContext.OMSetBlendState(opaque ? _opaqueBlendState : _alphaBlendState, ref factor, uint.MaxValue);
            _currentBlend = key;
        }
        _deviceContext.UpdateSubresource(_constantBuffer, 0, ref Unsafe.NullRef<Box>(), ref cb, 0, 0);
        _deviceContext.PSSetShaderResources(0, 1, ref surface);
        _deviceContext.PSSetShaderResources(1, 1, ref gradient);
        var sampler = clampGradient ? _gradientSampler : _surfaceSampler;
        _deviceContext.PSSetSamplers(1, 1, ref sampler);
        _deviceContext.DrawIndexed(count, first, 0);
    }

    internal void EndSubmission()
    {
        ComPtr<ID3D11ShaderResourceView> srv = default;
        _deviceContext.PSSetShaderResources(0, 1, ref srv);
        _deviceContext.PSSetShaderResources(1, 1, ref srv);
        ComPtr<ID3D11SamplerState> sampler = default;
        _deviceContext.PSSetSamplers(0, 1, ref sampler);
        _deviceContext.PSSetSamplers(1, 1, ref sampler);
        var factor = 1f;
        _deviceContext.OMSetBlendState(_opaqueBlendState, ref factor, uint.MaxValue);
        ComPtr<ID3D11DepthStencilState> depth = default;
        ComPtr<ID3D11RasterizerState> rasterizer = default;
        _deviceContext.OMSetDepthStencilState(depth, 0);
        _deviceContext.RSSetState(rasterizer);
    }

    private static bool IsWithinRenderDistance(
        Vector3 cameraPosition,
        Vector3 center,
        float radius,
        float distance)
    {
        var maxDistance = Math.Max(0f, distance) + Math.Max(0f, radius);
        return Vector3.DistanceSquared(cameraPosition, center) <= maxDistance * maxDistance;
    }

    private static Vector3 NormalizeLightDirection(Vector3 direction)
    {
        var lengthSquared = direction.LengthSquared();
        return lengthSquared > 0.000001f
            ? direction / MathF.Sqrt(lengthSquared)
            : Vector3.UnitZ;
    }

    /// <summary>
    /// Only intrinsically opaque liquid families use the opaque path. Water
    /// stays on the simple alpha-blended fallback until the renderer supplies
    /// the scene-color/depth inputs required by the client refraction shader.
    /// In the simple fallback, source-alpha blending approximates the
    /// reference shader's LightParams-controlled tint-over-scene mix.
    /// </summary>
    internal static bool UsesOpaqueComposition(WorldLiquidMaterialFamily family) =>
        family is WorldLiquidMaterialFamily.Magma or
            WorldLiquidMaterialFamily.Mercury or
            WorldLiquidMaterialFamily.Fel;

    internal static bool IsWaterMaterial(WorldLiquidMaterialDescriptor material) =>
        material.Family == WorldLiquidMaterialFamily.Water ||
        material.WaterType != WorldLiquidWaterType.Unknown;

    // The reference material selects the ocean palette only for waterType 0.
    // River, WMO, and unclassified non-ocean water use the river palette.
    internal static bool UsesRiverLightingPalette(WorldLiquidWaterType waterType) =>
        waterType != WorldLiquidWaterType.Ocean;

    internal static int SelectWmoFrame(long timeMilliseconds, uint periodMilliseconds, int frameCount)
    {
        if (frameCount <= 1 || periodMilliseconds == 0)
            return 0;
        var period = (long)periodMilliseconds;
        var phase = ((timeMilliseconds % period) + period) % period;
        return (int)(phase * frameCount / period);
    }

    private static Vector3 ClampColor(Vector3 color) => new(
        float.IsFinite(color.X) ? Math.Clamp(color.X, 0f, 4f) : 0f,
        float.IsFinite(color.Y) ? Math.Clamp(color.Y, 0f, 4f) : 0f,
        float.IsFinite(color.Z) ? Math.Clamp(color.Z, 0f, 4f) : 0f);

    private static WorldLiquidMaterialDescriptor Material(WorldLiquidResources liquid, ParsedWorldLiquidBatch batch) =>
        liquid.materials is { Length: > 0 } && (uint)batch.MaterialIndex < (uint)liquid.materials.Length
            ? liquid.materials[batch.MaterialIndex] : WorldLiquidMaterialCatalog.Shared.Resolve(0, 0);
}
