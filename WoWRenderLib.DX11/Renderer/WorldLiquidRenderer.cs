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
using WoWRenderLib.Raycasting;
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
    private readonly List<VisibleBatch> _visibleBatches = new(256);
    private ShaderManager? _shaderManager;
    private bool _initialized;

    private readonly record struct VisibleBatch(
        object Owner,
        WorldLiquidResources Liquid,
        Matrix4x4 Model,
        int BatchIndex,
        float ViewDepth,
        int TileOrder,
        int ChunkIndex,
        int LayerIndex);

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
        }

        _initialized = true;
    }

    public void RefreshShader()
    {
        if (_initialized && _shaderManager != null)
            _shader = _shaderManager.GetOrCompileShader("liquid");
    }

    public WorldLiquidRenderStats Render(
        Camera camera,
        IReadOnlyList<ADTContainer> adtContainers,
        IReadOnlyList<WmoLiquidInstance> wmoLiquids,
        IReadOnlySet<uint> coarseCulledTileRoots,
        float renderDistance,
        float wmoRenderDistance,
        long timeMilliseconds,
        Vector3 lightDirection,
        Vector3 ambientColor,
        Vector3 diffuseColor,
        WorldLightingSettings clientLighting)
    {
        if (!_initialized || (adtContainers.Count == 0 && wmoLiquids.Count == 0))
            return default;

        var cullingStarted = Stopwatch.GetTimestamp();
        _visibleBatches.Clear();
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix();
        var frustum = camera.GetFrustum();
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

            for (var batchIndex = 0; batchIndex < liquid.batches.Length; batchIndex++)
            {
                var batch = liquid.batches[batchIndex];
                candidateCount++;
                if (coarseCulledTileRoots.Contains(container.Terrain.rootADTFileDataID))
                    continue;

                var bounds = batch.Bounds;
                if (frustum.ClassifyAxisAlignedBox(bounds.Min, bounds.Max) == Frustum.BoxIntersection.Outside)
                    continue;

                var sphere = new BoundingSphere(
                    bounds.Center,
                    Vector3.Distance(bounds.Center, bounds.Max));
                if (!IsWithinRenderDistance(camera.Position, sphere.Center, sphere.Radius, renderDistance))
                    continue;

                var viewCenter = Vector3.Transform(bounds.Center, view).Z;
                _visibleBatches.Add(new VisibleBatch(
                    container,
                    liquid,
                    container.GetModelMatrix(),
                    batchIndex,
                    viewCenter,
                    tileOrder,
                    batch.ChunkIndex,
                    batch.LayerIndex));
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
            tileOrder++;
            for (var batchIndex = 0; batchIndex < liquid.batches.Length; batchIndex++)
            {
                var batch = liquid.batches[batchIndex];
                candidateCount++;
                var bounds = BoundingBox.Transform(batch.Bounds, matrix);
                if (frustum.ClassifyAxisAlignedBox(bounds.Min, bounds.Max) == Frustum.BoxIntersection.Outside)
                    continue;
                var radius = Vector3.Distance(bounds.Center, bounds.Max);
                if (!IsWithinRenderDistance(camera.Position, bounds.Center, radius, wmoRenderDistance))
                    continue;
                _visibleBatches.Add(new VisibleBatch(
                    liquid.batches, liquid, matrix, batchIndex,
                    Vector3.Transform(bounds.Center, view).Z,
                    tileOrder, wmoLiquid.GroupIndex, 0));
            }
        }

        var cullingMilliseconds = Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;
        _visibleBatches.Sort(static (left, right) =>
        {
            var depth = right.ViewDepth.CompareTo(left.ViewDepth);
            if (depth != 0)
                return depth;
            var tile = left.TileOrder.CompareTo(right.TileOrder);
            if (tile != 0)
                return tile;
            var chunk = left.ChunkIndex.CompareTo(right.ChunkIndex);
            return chunk != 0 ? chunk : left.LayerIndex.CompareTo(right.LayerIndex);
        });

        if (_visibleBatches.Count == 0)
            return new WorldLiquidRenderStats(
                candidateCount,
                0,
                0,
                0,
                cullingMilliseconds,
                0);

        var submissionStarted = Stopwatch.GetTimestamp();
        _deviceContext.RSSetState(_rasterizerState);
        _deviceContext.OMSetDepthStencilState(_depthStencilState, 0);
        _deviceContext.IASetPrimitiveTopology(
            D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        _deviceContext.IASetInputLayout(_shader.InputLayout);
        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        _deviceContext.VSSetShader(_shader.VertexShader, ref nullClassInstance, 0);
        _deviceContext.PSSetShader(_shader.PixelShader, ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _constantBuffer);
        _deviceContext.PSSetConstantBuffers(0, 1, ref _constantBuffer);

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
        var blendFactor = 1f;
        var currentBlend = -1;

        foreach (var visible in _visibleBatches)
        {
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

            var material = liquid.materials is { Length: > 0 } &&
                batch.MaterialIndex >= 0 &&
                batch.MaterialIndex < liquid.materials.Length
                ? liquid.materials[batch.MaterialIndex]
                : WorldLiquidMaterialCatalog.Shared.Resolve(0, 0);
            var isWater = IsWaterMaterial(material);
            // The current DX11 fallback is the simple forward liquid path. It
            // does not have the scene-color/depth inputs required by the
            // reference high-detail water material, so water retains the
            // known-working alpha-blended pass instead of pretending to be
            // that material with only its normal/foam textures.
            var isOpaque = !batch.IsWmo && UsesOpaqueComposition(material.Family);
            var blendState = isOpaque ? _opaqueBlendState : _alphaBlendState;
            var depthState = isOpaque ? _opaqueDepthStencilState : _depthStencilState;
            var blendKey = isOpaque ? 1 : 2;
            _deviceContext.OMSetDepthStencilState(depthState, 0);
            if (currentBlend != blendKey)
            {
                _deviceContext.OMSetBlendState(blendState, ref blendFactor, uint.MaxValue);
                currentBlend = blendKey;
            }

            // WMO surfaces animate slot zero. ADT water uses its first texture
            // as wave data, not albedo. The shader needs the load state to
            // choose the surface fallback while an asset is absent/loading.
            var wmoFrames = material.TextureSlots is { Length: > 0 }
                ? material.TextureSlots[0].Frames
                : [];
            var textureId = batch.IsWmo
                ? wmoFrames.Length > 0
                    ? wmoFrames[SelectWmoFrame(timeMilliseconds,
                        material.WmoAnimationPeriodMilliseconds, wmoFrames.Length)]
                    : 0u
                : material.TextureFileDataIds is { Length: > 0 }
                    ? material.TextureFileDataIds[0]
                    : 0u;
            ComPtr<ID3D11ShaderResourceView> texture = default;
            var hasLoadedTexture = textureId != 0 &&
                BLPCache.TryGetLoaded(textureId, out texture);
            if (!hasLoadedTexture)
                texture = _missingTexture;

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
                LightingAmbient = new Vector4(ClampColor(ambientColor), 0f),
                LightingDiffuse = new Vector4(ClampColor(diffuseColor), 0f),
                // Keep RGB and alpha as separate inputs. The simple pass uses
                // standard source-alpha blending to approximate the reference
                // shader's water-tint-over-scene/refraction mix.
                OceanCloseColor = new Vector4(ClampColor(oceanCloseColor), 1f),
                OceanFarColor = new Vector4(ClampColor(oceanFarColor), 1f),
                RiverCloseColor = new Vector4(ClampColor(riverCloseColor), 1f),
                RiverFarColor = new Vector4(ClampColor(riverFarColor), 1f),
                LiquidColorParameters = new Vector4(
                    useClientLiquidColors && isWater && !batch.IsWmoInterior ? 1f : 0f,
                    UsesRiverLightingPalette(material.WaterType) ? 1f : 0f,
                    clientLighting.HasLiquidAlphaData && isWater ? 1f : 0f,
                    batch.IsWmoInterior ? 1f : 0f),
                DepthCoefficients = material.DepthCoefficients,
                LightDirection = new Vector4(NormalizeLightDirection(lightDirection), 0f),
                LiquidAlphaParameters = new Vector4(
                    oceanShallowAlpha,
                    oceanDeepAlpha,
                    riverShallowAlpha,
                    riverDeepAlpha),
                WmoWaterColor = new Vector4(
                    batch.IsWmoInterior
                        ? Vector3.One
                        : useClientLiquidColors
                            ? ClampColor(riverCloseColor)
                            : WorldLiquidColorDefaults.RiverClose,
                    0f),
                WmoParameters = new Vector4(
                    material.WmoBasicClass,
                    material.WmoTextureRotation,
                    riverShallowAlpha,
                    riverDeepAlpha)
            };
            _deviceContext.UpdateSubresource(
                _constantBuffer,
                0,
                ref Unsafe.NullRef<Box>(),
                ref cb,
                0,
                0);

            // The high-detail reference material's slot 2/3 normal and foam
            // inputs are only valid together with scene backbuffer/depth.
            _deviceContext.PSSetShaderResources(0, 1, ref texture);
            _deviceContext.DrawIndexed(
                batch.IndexCount,
                batch.FirstIndex,
                0);
            drawCalls++;
            submittedIndices += batch.IndexCount;
        }

        // Do not leak liquid SRVs into the next pass. The caller restores its
        // opaque state immediately after this method returns.
        ComPtr<ID3D11ShaderResourceView> nullSrv = default;
        _deviceContext.PSSetShaderResources(0, 1, ref nullSrv);
        _deviceContext.OMSetBlendState(_opaqueBlendState, ref blendFactor, uint.MaxValue);
        ComPtr<ID3D11DepthStencilState> nullDepthStencilState = default;
        ComPtr<ID3D11RasterizerState> nullRasterizerState = default;
        _deviceContext.OMSetDepthStencilState(nullDepthStencilState, 0);
        _deviceContext.RSSetState(nullRasterizerState);

        return new WorldLiquidRenderStats(
            candidateCount,
            _visibleBatches.Count,
            drawCalls,
            submittedIndices,
            cullingMilliseconds,
            Stopwatch.GetElapsedTime(submissionStarted).TotalMilliseconds);
    }

    public void Dispose()
    {
        _missingTexture.Dispose();
        _opaqueBlendState.Dispose();
        _alphaBlendState.Dispose();
        _opaqueDepthStencilState.Dispose();
        _depthStencilState.Dispose();
        _rasterizerState.Dispose();
        _constantBuffer.Dispose();
        _visibleBatches.Clear();
        _initialized = false;
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
}
