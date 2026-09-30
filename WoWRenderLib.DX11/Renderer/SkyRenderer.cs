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
    public Vector4 SkyGlowParameters;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SkyCelestialCB
{
    public Vector4 BodyCenter;
    public Vector4 BillboardHorizontal;
    public Vector4 BillboardVertical;
    public Vector4 CameraRight;
    public Vector4 CameraUp;
    public Vector4 CameraFront;
    public Vector4 ProjectionTerms;
}

/// <summary>
/// Owns the camera-centred LightData sky, including the 3.3.5 vertex-color
/// dome, and the optional LightSkybox M2 pass. Models use the existing
/// asynchronous M2/BLP caches without client I/O during frame submission.
/// </summary>
internal sealed class SkyRenderer(
    ComPtr<ID3D11Device> device,
    ComPtr<ID3D11DeviceContext> deviceContext) : IDisposable
{
    private const uint CacheOwnerId = 0xFFFF_FFFEu;
    private const float ClearWeatherBlend = 0f;
    private static readonly Matrix4x4 SkyboxTransform = Matrix4x4.CreateRotationZ(MathF.PI);
    private static readonly Wrath335CloudNoise ProcessCloudNoise =
        Wrath335CloudNoise.CreateProcessRandom();
    private readonly ComPtr<ID3D11Device> _device = device;
    private readonly ComPtr<ID3D11DeviceContext> _deviceContext = deviceContext;
    private readonly ComPtr<ID3D11BlendState>[] _blendStates = new ComPtr<ID3D11BlendState>[14];
    private readonly ComPtr<ID3D11SamplerState>[] _samplers = new ComPtr<ID3D11SamplerState>[4];
    private CompiledShader _gradientShader;
    private CompiledShader _wrathDomeShader;
    private CompiledShader _celestialShader;
    private CompiledShader _cloudShader;
    private CompiledShader _glareQueryShader;
    private CompiledShader _m2Shader;
    private ComPtr<ID3D11Buffer> _gradientConstantBuffer;
    private ComPtr<ID3D11Buffer> _celestialConstantBuffer;
    private ComPtr<ID3D11Buffer> _celestialVertexBuffer;
    private ComPtr<ID3D11Buffer> _celestialIndexBuffer;
    private ComPtr<ID3D11Buffer> _domeVertexBuffer;
    private ComPtr<ID3D11Buffer> _domeIndexBuffer;
    private ComPtr<ID3D11Buffer> _cloudVertexBuffer;
    private ComPtr<ID3D11Buffer> _cloudIndexBuffer;
    private readonly ComPtr<ID3D11Texture2D>[] _cloudTextures = new ComPtr<ID3D11Texture2D>[2];
    private readonly ComPtr<ID3D11ShaderResourceView>[] _cloudResources =
        new ComPtr<ID3D11ShaderResourceView>[2];
    private Wrath335CloudTextureGenerator _cloudGenerator =
        new(Wrath335CloudTextureGenerator.DefaultLod, ProcessCloudNoise);
    private Task<Wrath335CloudTextureGenerator>? _pendingCloudGenerator;
    private int _pendingCloudLod = -1;
    private int _requestedCloudLod;
    private int _failedCloudLod = -1;
    private long _lastCloudTimestamp;
    private ComPtr<ID3D11Buffer> _m2ConstantBuffer;
    private ComPtr<ID3D11Buffer> _bonePaletteConstantBuffer;
    private ComPtr<ID3D11Buffer> _instanceBuffer;
    private ComPtr<ID3D11DepthStencilState> _depthDisabledState;
    private ComPtr<ID3D11DepthStencilState> _glareQueryDepthState;
    private ComPtr<ID3D11BlendState> _glareQueryBlendState;
    private ComPtr<ID3D11RasterizerState> _cullState;
    private ComPtr<ID3D11RasterizerState> _twoSidedState;
    private ComPtr<ID3D11RasterizerState> _skyScissorCullState;
    private ComPtr<ID3D11RasterizerState> _skyScissorTwoSidedState;
    private bool _skyScissorEnabled;
    private ComPtr<ID3D11RasterizerState> SkyCullState =>
        _skyScissorEnabled ? _skyScissorCullState : _cullState;
    private ComPtr<ID3D11RasterizerState> SkyTwoSidedState =>
        _skyScissorEnabled ? _skyScissorTwoSidedState : _twoSidedState;
    private ComPtr<ID3D11ShaderResourceView> _missingTexture;
    private ShaderManager? _shaderManager;
    private WorldSkyLighting _lighting = WorldSkyLighting.None;
    private readonly SkyDomeVertex[] _domeVertices = SkyDomeMesh.CreateVertices();
    private readonly Wrath335CelestialVertex[] _celestialVertices =
        new Wrath335CelestialVertex[Wrath335CelestialQuad.MaxVertices];
    private sealed record CelestialTextureIds(uint Sun, uint Moon1, uint Moon02);
    private static readonly CelestialTextureIds EmptyCelestialTextures = new(0, 0, 0);
    private CelestialTextureIds _requestedCelestialTextures = EmptyCelestialTextures;
    private CelestialTextureIds _activeCelestialTextures = EmptyCelestialTextures;
    private readonly HashSet<uint> _trackedCelestialTextureIds = [];
    private sealed record GlareTextureIds(uint Sun, uint Moon);
    private static readonly GlareTextureIds EmptyGlareTextures = new(0, 0);
    private GlareTextureIds _requestedGlareTextures = EmptyGlareTextures;
    private GlareTextureIds _activeGlareTextures = EmptyGlareTextures;
    private readonly HashSet<uint> _trackedGlareTextureIds = [];
    private readonly Wrath335GlareEvaluator _sunGlare = new(moon: false);
    private readonly Wrath335GlareEvaluator _moonGlare = new(moon: true);
    private readonly GlareOcclusion _sunOcclusion = new();
    private readonly GlareOcclusion _moonOcclusion = new();
    private long _lastGlareTimestamp;
    private uint _requestedStarModelId;
    private uint _activeStarModelId;
    private bool _domeColorsDirty = true;
    private float _lastDomeGlow;
    private float _lastDomeSunAzimuth;
    private readonly HashSet<uint> _trackedSkyboxFileDataIds = [];
    private readonly Dictionary<uint, M2AnimationPoseCache> _animationCaches = [];
    private readonly Dictionary<uint, (M2Animation Animation, M2AnimationPose Pose)> _lastSkyboxPoses = [];
    private M2AnimationPose? _uploadedPose;
    private long _uploadedPoseVersion;
    private bool _initialized;

    private sealed class GlareOcclusion : IDisposable
    {
        public readonly ComPtr<ID3D11Query>[] Queries = new ComPtr<ID3D11Query>[2];
        public readonly bool[] Pending = new bool[2];
        public readonly float[] ProjectedAreas = new float[2];
        public float LastVisibility;

        public void Dispose()
        {
            foreach (var query in Queries)
                query.Dispose();
        }
    }

    public void Initialize(ShaderManager shaderManager, CompiledShader m2Shader)
    {
        ArgumentNullException.ThrowIfNull(shaderManager);
        if (_initialized)
            return;

        _shaderManager = shaderManager;
        _gradientShader = shaderManager.GetOrCompileShader("sky");
        _wrathDomeShader = shaderManager.GetOrCompileShader("sky_wrath");
        _celestialShader = shaderManager.GetOrCompileShader("sky_celestial");
        _cloudShader = shaderManager.GetOrCompileShader("sky_cloud");
        _glareQueryShader = shaderManager.GetOrCompileShader("sky_glare_query");
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

            bufferDesc.ByteWidth = (uint)Marshal.SizeOf<SkyCelestialCB>();
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in bufferDesc,
                null,
                ref _celestialConstantBuffer));

            bufferDesc.ByteWidth = (uint)Marshal.SizeOf<M2PerObjectCB>();
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in bufferDesc,
                null,
                ref _m2ConstantBuffer));

            bufferDesc.ByteWidth = (uint)(M2Animation.MaxGpuBones * sizeof(Matrix4x4));
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in bufferDesc,
                null,
                ref _bonePaletteConstantBuffer));

            var skyboxTransform = SkyboxTransform;
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

            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(_domeVertices.Length * Marshal.SizeOf<SkyDomeVertex>()),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.VertexBuffer
            };
            fixed (SkyDomeVertex* domeVertices = _domeVertices)
            {
                var domeData = new SubresourceData { PSysMem = domeVertices };
                SilkMarshal.ThrowHResult(_device.CreateBuffer(
                    in bufferDesc, in domeData, ref _domeVertexBuffer));
            }

            var domeIndices = SkyDomeMesh.CreateTriangleIndices();
            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(domeIndices.Length * sizeof(ushort)),
                Usage = Usage.Immutable,
                BindFlags = (uint)BindFlag.IndexBuffer
            };
            fixed (ushort* indexData = domeIndices)
            {
                var domeData = new SubresourceData { PSysMem = indexData };
                SilkMarshal.ThrowHResult(_device.CreateBuffer(
                    in bufferDesc, in domeData, ref _domeIndexBuffer));
            }

            var cloudVertices = Wrath335CloudMesh.CreateVertices();
            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(cloudVertices.Length *
                    Marshal.SizeOf<Wrath335CloudVertex>()),
                Usage = Usage.Immutable,
                BindFlags = (uint)BindFlag.VertexBuffer
            };
            fixed (Wrath335CloudVertex* vertexData = cloudVertices)
            {
                var cloudData = new SubresourceData { PSysMem = vertexData };
                SilkMarshal.ThrowHResult(_device.CreateBuffer(
                    in bufferDesc, in cloudData, ref _cloudVertexBuffer));
            }

            var cloudIndices = Wrath335CloudMesh.CreateStripIndices();
            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(cloudIndices.Length * sizeof(ushort)),
                Usage = Usage.Immutable,
                BindFlags = (uint)BindFlag.IndexBuffer
            };
            fixed (ushort* indexData = cloudIndices)
            {
                var cloudData = new SubresourceData { PSysMem = indexData };
                SilkMarshal.ThrowHResult(_device.CreateBuffer(
                    in bufferDesc, in cloudData, ref _cloudIndexBuffer));
            }

            RecreateCloudTextures(_cloudGenerator.Size);

            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(_celestialVertices.Length *
                    Marshal.SizeOf<Wrath335CelestialVertex>()),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.VertexBuffer
            };
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in bufferDesc, null, ref _celestialVertexBuffer));

            var celestialIndices = Wrath335CelestialQuad.SixVertexIndices.ToArray();
            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(celestialIndices.Length * sizeof(ushort)),
                Usage = Usage.Immutable,
                BindFlags = (uint)BindFlag.IndexBuffer
            };
            fixed (ushort* indexData = celestialIndices)
            {
                var indexDataSource = new SubresourceData { PSysMem = indexData };
                SilkMarshal.ThrowHResult(_device.CreateBuffer(
                    in bufferDesc, in indexDataSource, ref _celestialIndexBuffer));
            }

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
            depthDesc.DepthEnable = true;
            depthDesc.DepthFunc = ComparisonFunc.LessEqual;
            SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(
                in depthDesc, ref _glareQueryDepthState));

            var queryBlendDesc = new BlendDesc
            {
                AlphaToCoverageEnable = 0,
                IndependentBlendEnable = 0
            };
            queryBlendDesc.RenderTarget[0] = new RenderTargetBlendDesc
            {
                BlendEnable = 0,
                SrcBlend = Blend.One,
                DestBlend = Blend.Zero,
                BlendOp = BlendOp.Add,
                SrcBlendAlpha = Blend.One,
                DestBlendAlpha = Blend.Zero,
                BlendOpAlpha = BlendOp.Add,
                RenderTargetWriteMask = 0
            };
            SilkMarshal.ThrowHResult(_device.CreateBlendState(
                in queryBlendDesc, ref _glareQueryBlendState));
            var occlusionDescription = new QueryDesc(Query.Occlusion, 0);
            foreach (var occlusion in new[] { _sunOcclusion, _moonOcclusion })
            {
                for (var index = 0; index < occlusion.Queries.Length; index++)
                    SilkMarshal.ThrowHResult(_device.CreateQuery<ID3D11Query>(
                        in occlusionDescription, ref occlusion.Queries[index]));
            }

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
            rasterizerDesc.ScissorEnable = true;
            SilkMarshal.ThrowHResult(_device.CreateRasterizerState(
                in rasterizerDesc, ref _skyScissorTwoSidedState));
            rasterizerDesc.CullMode = CullMode.Back;
            SilkMarshal.ThrowHResult(_device.CreateRasterizerState(
                in rasterizerDesc, ref _skyScissorCullState));

            CreateSamplers();
            CreateBlendStates();
        }

        _initialized = true;
    }

    /// <summary>SkyCloudLOD is a 3.3.5 CVar clamped to integer levels 0..3.</summary>
    public void SetCloudLod(int lod)
    {
        var clamped = Math.Clamp(lod, 0, Wrath335CloudNoise.MaximumCloudLod);
        if (clamped == _requestedCloudLod)
            return;
        _requestedCloudLod = clamped;
        _failedCloudLod = -1;
    }

    private unsafe void RecreateCloudTextures(int size)
    {
        var description = new Texture2DDesc
        {
            Width = (uint)size,
            Height = (uint)size,
            MipLevels = Wrath335CloudTextureGenerator.ClientMipCount,
            ArraySize = 1,
            Format = Format.FormatB8G8R8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource
        };
        for (var index = 0; index < _cloudTextures.Length; index++)
        {
            _cloudResources[index].Dispose();
            _cloudTextures[index].Dispose();
            _cloudResources[index] = default;
            _cloudTextures[index] = default;
            SilkMarshal.ThrowHResult(_device.CreateTexture2D(
                in description, null, ref _cloudTextures[index]));
            SilkMarshal.ThrowHResult(_device.CreateShaderResourceView(
                _cloudTextures[index], null, ref _cloudResources[index]));
        }
    }

    private void RefreshCloudLod(long lightTime)
    {
        var pending = _pendingCloudGenerator;
        if (pending is { IsCompleted: true })
        {
            _pendingCloudGenerator = null;
            var completedLod = _pendingCloudLod;
            _pendingCloudLod = -1;
            if (pending.IsCompletedSuccessfully &&
                pending.Result.Lod == _requestedCloudLod)
            {
                _cloudGenerator = pending.Result;
                RecreateCloudTextures(_cloudGenerator.Size);
                for (var index = 0; index < _cloudTextures.Length; index++)
                {
                    UploadCloudRows(index, 0, _cloudGenerator.Size);
                    UploadCloudMip(index);
                }
                _lastCloudTimestamp = 0;
            }
            else if (pending.IsFaulted && completedLod == _requestedCloudLod)
            {
                _failedCloudLod = _requestedCloudLod;
                Console.WriteLine($"SkyCloudLOD update failed: {pending.Exception}");
            }
        }

        if (_pendingCloudGenerator != null ||
            _cloudGenerator.Lod == _requestedCloudLod ||
            _failedCloudLod == _requestedCloudLod)
            return;

        var lod = _requestedCloudLod;
        _pendingCloudLod = lod;
        var density = _lighting.CloudDensity;
        var body = _lighting.LegacyCloudBodyColor;
        var emissive = _lighting.LegacyCloudEmissiveColor;
        var ambient = _lighting.LegacyCloudAmbientColor;
        var celestial = Wrath335CelestialEvaluator.Evaluate((int)lightTime);
        var light = Wrath335CloudTextureGenerator.UseMoon(lightTime)
            ? celestial.Moon1.CameraRelativeCenter
            : celestial.Sun.CameraRelativeCenter;
        _pendingCloudGenerator = Task.Run(() =>
        {
            var generated = new Wrath335CloudTextureGenerator(lod, ProcessCloudNoise);
            generated.Update(0f, density, body, emissive, ambient,
                light, ClearWeatherBlend);
            return generated;
        });
    }

    public void RefreshShaders()
    {
        if (!_initialized || _shaderManager == null)
            return;
        _gradientShader = _shaderManager.GetOrCompileShader("sky");
        _wrathDomeShader = _shaderManager.GetOrCompileShader("sky_wrath");
        _celestialShader = _shaderManager.GetOrCompileShader("sky_celestial");
        _cloudShader = _shaderManager.GetOrCompileShader("sky_cloud");
        _glareQueryShader = _shaderManager.GetOrCompileShader("sky_glare_query");
        _m2Shader = _shaderManager.GetOrCompileShader("m2");
    }

    /// <summary>Publish wowlib-resolved MPQ cache keys from content initialization.</summary>
    public void ConfigureWrathCelestialTextures(uint sun, uint moon1, uint moon02) =>
        Volatile.Write(ref _requestedCelestialTextures,
            new CelestialTextureIds(sun, moon1, moon02));

    /// <summary>Publish the wowlib-resolved stars M2 cache key once per client.</summary>
    public void ConfigureWrathStarModel(uint modelId) =>
        Volatile.Write(ref _requestedStarModelId, modelId);

    public void ConfigureWrathGlareTextures(uint sun, uint moon) =>
        Volatile.Write(ref _requestedGlareTextures, new GlareTextureIds(sun, moon));

    private void SynchronizeGlareTextures()
    {
        var requested = Volatile.Read(ref _requestedGlareTextures);
        if (ReferenceEquals(requested, _activeGlareTextures))
            return;
        foreach (var id in _trackedGlareTextureIds.ToArray())
        {
            if (id == requested.Sun || id == requested.Moon)
                continue;
            BLPCache.Release(id, CacheOwnerId);
            _trackedGlareTextureIds.Remove(id);
        }
        Track(requested.Sun);
        Track(requested.Moon);
        _activeGlareTextures = requested;

        void Track(uint id)
        {
            if (id != 0 && _trackedGlareTextureIds.Add(id))
                BLPCache.GetOrLoad(_device, id, CacheOwnerId);
        }
    }

    private void SynchronizeStarModel()
    {
        var requested = Volatile.Read(ref _requestedStarModelId);
        if (requested == _activeStarModelId)
            return;

        if (_activeStarModelId != 0)
        {
            M2Cache.Release(_activeStarModelId, CacheOwnerId);
            _animationCaches.Remove(_activeStarModelId);
            _lastSkyboxPoses.Remove(_activeStarModelId);
        }

        if (requested != 0)
            M2Cache.GetOrLoad(_device, requested, CacheOwnerId, keepTrack: true);
        _activeStarModelId = requested;
    }

    private void SynchronizeCelestialTextures()
    {
        var requested = Volatile.Read(ref _requestedCelestialTextures);
        if (ReferenceEquals(requested, _activeCelestialTextures))
            return;

        foreach (var id in _trackedCelestialTextureIds.ToArray())
        {
            if (id == requested.Sun || id == requested.Moon1 || id == requested.Moon02)
                continue;
            BLPCache.Release(id, CacheOwnerId);
            _trackedCelestialTextureIds.Remove(id);
        }

        Track(requested.Sun);
        Track(requested.Moon1);
        Track(requested.Moon02);
        _activeCelestialTextures = requested;

        void Track(uint id)
        {
            if (id != 0 && _trackedCelestialTextureIds.Add(id))
                BLPCache.GetOrLoad(_device, id, CacheOwnerId);
        }
    }

    public void SetLighting(WorldSkyLighting lighting)
    {
        var normalizedSkyboxes = NormalizeSkyboxes(lighting.Skyboxes);
        _lighting = lighting with { Skyboxes = normalizedSkyboxes };
        _domeColorsDirty = true;

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
            _animationCaches.Remove(fileDataId);
            _lastSkyboxPoses.Remove(fileDataId);
        }

        foreach (var fileDataId in requestedFileDataIds)
        {
            if (_trackedSkyboxFileDataIds.Add(fileDataId))
                M2Cache.GetOrLoad(_device, fileDataId, CacheOwnerId, keepTrack: true);
        }
    }

    /// <summary>
    /// Optionally clips the complete sky pass to a Wrath NDC view. Returns with
    /// scissor disabled and default blend/depth state for subsequent world passes.
    /// </summary>
    public unsafe SkyRenderStats Render(
        Camera camera, bool animateModels, long sceneTimeMilliseconds, long lightTime,
        Vector3 lightDirection, bool enableDayNightSkyColors,
        WmoPortalRect? skyView = null, uint viewportWidth = 0, uint viewportHeight = 0)
    {
        if (!_initialized)
            return default;
        if (skyView is { } view)
        {
            if (!Wrath335SkyScissor.TryCreate(view, viewportWidth, viewportHeight, out var rect))
                return default;
            var scissor = new Silk.NET.Maths.Box2D<int>(
                new(rect.Left, rect.Top), new(rect.Right, rect.Bottom));
            _deviceContext.RSSetScissorRects(1, in scissor);
            _skyScissorEnabled = true;
        }
        try
        {
            return RenderCore(camera, animateModels, sceneTimeMilliseconds, lightTime,
                lightDirection, enableDayNightSkyColors);
        }
        finally
        {
            if (_skyScissorEnabled)
            {
                _skyScissorEnabled = false;
                _deviceContext.RSSetScissorRects(0, (Silk.NET.Maths.Box2D<int>*)null);
                _deviceContext.RSSetState(_twoSidedState);
            }
        }
    }

    private unsafe SkyRenderStats RenderCore(
        Camera camera, bool animateModels, long sceneTimeMilliseconds, long lightTime,
        Vector3 lightDirection, bool enableDayNightSkyColors)
    {
        if (!_initialized)
            return default;
        SynchronizeCelestialTextures();
        SynchronizeGlareTextures();
        SynchronizeStarModel();
        if (!_lighting.HasColorData && !_lighting.HasSkyboxes)
            return default;

        var started = Stopwatch.GetTimestamp();
        uint drawCalls = 0;
        ulong submittedIndices = 0;
        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        ComPtr<ID3D11GeometryShader> nullGeometryShader = default;
        _deviceContext.GSSetShader(nullGeometryShader, ref nullClassInstance, 0);
        _deviceContext.OMSetDepthStencilState(_depthDisabledState, 0);

        // DayNight::RenderSky (0x7F09B0) skips the procedural sky when a
        // drawable, nearly opaque skybox does not request combination.
        var renderProceduralSky = true;
        if (enableDayNightSkyColors && _lighting.HasSkyboxes)
        {
            foreach (var skybox in _lighting.Skyboxes)
            {
                if (skybox.FileDataId == 0 ||
                    skybox.Opacity <= Wrath335SkyReference.SkyboxSuppressionOpacity ||
                    (skybox.Flags & Wrath335SkyReference.SkyboxCombineFlag) != 0)
                    continue;

                var model = M2Cache.GetOrLoad(
                    _device, skybox.FileDataId, CacheOwnerId, keepTrack: false);
                if (SuppressesProceduralSky(skybox,
                    model.fileDataID == skybox.FileDataId &&
                    model.vertexBuffer.Handle != null &&
                    model.indiceBuffer.Handle != null))
                {
                    renderProceduralSky = false;
                    break;
                }
            }
        }

        if (renderProceduralSky && _lighting.HasColorData)
        {
            var projection = camera.GetProjectionMatrix();
            var overrideWithFog = !enableDayNightSkyColors &&
                _lighting.OverrideColorsWithFog;
            var topColor = overrideWithFog ? _lighting.FogColor : _lighting.TopColor;
            var middleColor = overrideWithFog ? _lighting.FogColor : _lighting.MiddleColor;
            var band1Color = overrideWithFog ? _lighting.FogColor : _lighting.Band1Color;
            var band2Color = overrideWithFog ? _lighting.FogColor : _lighting.Band2Color;
            var smogColor = overrideWithFog ? _lighting.FogColor : _lighting.SmogColor;
            var glowStrength = enableDayNightSkyColors && !overrideWithFog
                ? DayNight.CalculateSkyGlowStrength(
                    (int)(lightTime % DayNight.GameDayLength),
                    _lighting.HighlightSkyStrength)
                : 0f;
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
                CameraUp = new Vector4(enableDayNightSkyColors
                    ? Vector3.Cross(camera.Right, camera.Front)
                    : camera.Up, 0f),
                ProjectionScale = new Vector4(1f / projection.M11, 1f / projection.M22, 0f, 0f),
                SkyGlowParameters = new Vector4(
                    glowStrength,
                    MathF.Atan2(lightDirection.Y, lightDirection.X),
                    Wrath335SkyReference.MinimumSkyDepth,
                    0f)
            };
            _deviceContext.UpdateSubresource(
                _gradientConstantBuffer,
                0,
                ref Unsafe.NullRef<Box>(),
                ref constants,
                0,
                0);
            // The client draws alpha-blended celestial bodies first, then
            // blends the DNSky dome additively over them.
            if (!enableDayNightSkyColors)
                ApplyBlendMode(0);
            _deviceContext.RSSetState(SkyTwoSidedState);
            _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
            if (enableDayNightSkyColors)
            {
                RenderWrathStars(camera, animateModels,
                    sceneTimeMilliseconds, lightTime,
                    ref drawCalls, ref submittedIndices);
                // M2 materials change rasterizer state; restore the sky state
                // before the client billboard and dome passes.
                _deviceContext.RSSetState(SkyTwoSidedState);
                RenderWrathCelestials(camera, lightTime,
                    ref drawCalls, ref submittedIndices);
                ApplyBlendMode(3);
                // The client colors each dome vertex, and the rasterizer
                // interpolates across the azimuth glow sign boundary. The old
                // per-pixel branch exposed that boundary as a vertical seam.
                var clientSunAzimuth = MathF.Atan2(-lightDirection.Y, -lightDirection.X);
                if (_domeColorsDirty || glowStrength != _lastDomeGlow ||
                    clientSunAzimuth != _lastDomeSunAzimuth)
                {
                    SkyDomeMesh.SetColors(
                        _domeVertices, _lighting, glowStrength, clientSunAzimuth);
                    _deviceContext.UpdateSubresource(
                        _domeVertexBuffer,
                        0,
                        ref Unsafe.NullRef<Box>(),
                        ref _domeVertices[0],
                        0,
                        0);
                    _lastDomeGlow = glowStrength;
                    _lastDomeSunAzimuth = clientSunAzimuth;
                    _domeColorsDirty = false;
                }

                var vertexStride = (uint)Marshal.SizeOf<SkyDomeVertex>();
                var vertexOffset = 0u;
                _deviceContext.IASetInputLayout(_wrathDomeShader.InputLayout);
                _deviceContext.IASetVertexBuffers(
                    0, 1, ref _domeVertexBuffer, in vertexStride, in vertexOffset);
                _deviceContext.IASetIndexBuffer(_domeIndexBuffer, Format.FormatR16Uint, 0);
                _deviceContext.VSSetShader(_wrathDomeShader.VertexShader, ref nullClassInstance, 0);
                _deviceContext.PSSetShader(_wrathDomeShader.PixelShader, ref nullClassInstance, 0);
                _deviceContext.VSSetConstantBuffers(0, 1, ref _gradientConstantBuffer);
                _deviceContext.DrawIndexed(SkyDomeMesh.TriangleIndexCount, 0, 0);
                submittedIndices += SkyDomeMesh.TriangleIndexCount;
                RenderWrathClouds(lightTime, ref drawCalls, ref submittedIndices);
            }
            else
            {
                ComPtr<ID3D11InputLayout> noInputLayout = default;
                _deviceContext.IASetInputLayout(noInputLayout);
                _deviceContext.VSSetShader(_gradientShader.VertexShader, ref nullClassInstance, 0);
                _deviceContext.PSSetShader(_gradientShader.PixelShader, ref nullClassInstance, 0);
                _deviceContext.PSSetConstantBuffers(0, 1, ref _gradientConstantBuffer);
                _deviceContext.Draw(3, 0);
            }
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
                    skybox.Flags,
                    skybox.Opacity,
                    animateModels,
                    sceneTimeMilliseconds,
                    lightTime,
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

    private unsafe void RenderWrathStars(
        Camera camera, bool animateModels,
        long sceneTimeMilliseconds, long lightTime,
        ref uint drawCalls, ref ulong submittedIndices)
    {
        if (_activeStarModelId == 0)
            return;
        var opacity = Wrath335StarEvaluator.EvaluateOpacity(lightTime);
        if (opacity <= 0f)
            return;

        var model = M2Cache.GetOrLoad(
            _device, _activeStarModelId, CacheOwnerId, keepTrack: false);
        if (model.fileDataID != _activeStarModelId ||
            model.vertexBuffer.Handle == null || model.indiceBuffer.Handle == null)
            return;

        // DNStars::Render precedes all celestial bodies. The M2 is centered on
        // the camera and advances on real scene time, regardless of the sky
        // lighting clock. Its alpha byte controls whole-model opacity.
        RenderSkyboxModel(camera, model, flags: 0, opacity,
            animateModels, sceneTimeMilliseconds, lightTime,
            ref drawCalls, ref submittedIndices);
    }

    private unsafe void RenderWrathClouds(
        long lightTime, ref uint drawCalls, ref ulong submittedIndices)
    {
        if (!_lighting.HasLegacyCloudData)
            return;

        RefreshCloudLod(lightTime);

        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = _lastCloudTimestamp == 0
            ? 0f
            : (float)Stopwatch.GetElapsedTime(_lastCloudTimestamp, now).TotalSeconds;
        _lastCloudTimestamp = now;
        var celestialFrame = Wrath335CelestialEvaluator.Evaluate((int)lightTime);
        var lightDirection = Wrath335CloudTextureGenerator.UseMoon(lightTime)
            ? celestialFrame.Moon1.CameraRelativeCenter
            : celestialFrame.Sun.CameraRelativeCenter;
        var update = _cloudGenerator.Update(
            elapsedSeconds, _lighting.CloudDensity,
            _lighting.LegacyCloudBodyColor,
            _lighting.LegacyCloudEmissiveColor,
            _lighting.LegacyCloudAmbientColor,
            lightDirection, ClearWeatherBlend);
        if (update.InitializeBoth)
        {
            for (var index = 0; index < _cloudTextures.Length; index++)
            {
                UploadCloudRows(index, 0, _cloudGenerator.Size);
                UploadCloudMip(index);
            }
        }
        else
        {
            UploadCloudRows(update.TextureIndex, update.FirstRow, update.RowCount);
            if (update.FirstRow + update.RowCount == _cloudGenerator.Size)
                UploadCloudMip(update.TextureIndex);
        }

        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        var vertexStride = (uint)Marshal.SizeOf<Wrath335CloudVertex>();
        var vertexOffset = 0u;
        _deviceContext.IASetPrimitiveTopology(
            D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglestrip);
        _deviceContext.IASetInputLayout(_cloudShader.InputLayout);
        _deviceContext.IASetVertexBuffers(
            0, 1, ref _cloudVertexBuffer, in vertexStride, in vertexOffset);
        _deviceContext.IASetIndexBuffer(_cloudIndexBuffer, Format.FormatR16Uint, 0);
        _deviceContext.VSSetShader(_cloudShader.VertexShader, ref nullClassInstance, 0);
        _deviceContext.PSSetShader(_cloudShader.PixelShader, ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _gradientConstantBuffer);
        _deviceContext.PSSetSamplers(0, 1, ref _samplers[0]);
        var activeResource = _cloudResources[_cloudGenerator.ActiveTextureIndex];
        _deviceContext.PSSetShaderResources(0, 1, ref activeResource);
        _deviceContext.RSSetState(SkyTwoSidedState);
        ApplyBlendMode(2);
        _deviceContext.DrawIndexed(Wrath335CloudMesh.StripIndexCount, 0, 0);
        drawCalls++;
        submittedIndices += Wrath335CloudMesh.StripIndexCount;

        ComPtr<ID3D11ShaderResourceView> nullResource = default;
        _deviceContext.PSSetShaderResources(0, 1, ref nullResource);
        _deviceContext.IASetPrimitiveTopology(
            D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
    }

    private unsafe void UploadCloudRows(int index, int firstRow, int rowCount)
    {
        var size = _cloudGenerator.Size;
        var rowPitch = checked((uint)(size * sizeof(uint)));
        var pixels = _cloudGenerator.GetPixels(index);
        var region = new Box
        {
            Left = 0,
            Top = checked((uint)firstRow),
            Front = 0,
            Right = checked((uint)size),
            Bottom = checked((uint)(firstRow + rowCount)),
            Back = 1
        };
        fixed (byte* source = pixels)
            _deviceContext.UpdateSubresource(
                _cloudTextures[index], 0, ref region,
                source + firstRow * size * sizeof(uint), rowPitch, 0);
    }

    private unsafe void UploadCloudMip(int index)
    {
        var pixels = _cloudGenerator.CreateFirstMip(index);
        var rowPitch = checked((uint)(_cloudGenerator.Size / 2 * sizeof(uint)));
        fixed (byte* source = pixels)
            _deviceContext.UpdateSubresource(
                _cloudTextures[index], 1, ref Unsafe.NullRef<Box>(),
                source, rowPitch, 0);
    }

    /// <summary>
    /// DNGlare draws after world geometry and particle effects, before the
    /// FFX glow composite. Occlusion results are read without flushing or
    /// stalling the GPU; the previous completed result drives this frame.
    /// </summary>
    public unsafe SkyRenderStats RenderWrathGlare(
        Camera camera, long lightTime, Vector3 lightDirection,
        uint viewportWidth, uint viewportHeight)
    {
        if (!_initialized || !_lighting.HasColorData ||
            viewportWidth == 0 || viewportHeight == 0)
            return default;
        var started = Stopwatch.GetTimestamp();
        ComPtr<ID3D11ClassInstance> nullGeometryClassInstance = default;
        ComPtr<ID3D11GeometryShader> nullGeometryShader = default;
        _deviceContext.GSSetShader(nullGeometryShader,
            ref nullGeometryClassInstance, 0);
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = _lastGlareTimestamp == 0
            ? 0f
            : (float)Stopwatch.GetElapsedTime(_lastGlareTimestamp, now).TotalSeconds;
        _lastGlareTimestamp = now;

        var celestial = Wrath335CelestialEvaluator.Evaluate((int)lightTime);
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix();
        Wrath335CelestialQuad.BuildCameraFacingBasis(
            camera.Front, out var horizontal, out var vertical);
        var constants = new SkyCelestialCB
        {
            BillboardHorizontal = new Vector4(horizontal, 0f),
            BillboardVertical = new Vector4(vertical, 0f),
            CameraRight = new Vector4(view.M11, view.M21, view.M31, 0f),
            CameraUp = new Vector4(view.M12, view.M22, view.M32, 0f),
            CameraFront = new Vector4(view.M13, view.M23, view.M33, 0f),
            ProjectionTerms = new Vector4(
                projection.M11, projection.M22,
                Wrath335SkyReference.MinimumSkyDepth, 0f)
        };
        var skyboxWeight = _lighting.Skyboxes.Count == 0
            ? 0f
            : _lighting.Skyboxes.Max(static skybox => skybox.Opacity);
        var sunAlignment = Vector3.Dot(
            Vector3.Normalize(celestial.Sun.CameraRelativeCenter),
            Vector3.Normalize(lightDirection));
        var moonAlignment = Vector3.Dot(
            Vector3.Normalize(celestial.Moon1.CameraRelativeCenter),
            Vector3.Normalize(lightDirection));
        uint drawCalls = 0;
        ulong submittedIndices = 0;

        RenderOneGlare(celestial.Sun, _activeGlareTextures.Sun,
            _sunOcclusion, _sunGlare, sunAlignment,
            camera, projection, viewportWidth, viewportHeight,
            elapsedSeconds, lightTime, skyboxWeight,
            ref constants, ref drawCalls, ref submittedIndices);
        RenderOneGlare(celestial.Moon1, _activeGlareTextures.Moon,
            _moonOcclusion, _moonGlare, moonAlignment,
            camera, projection, viewportWidth, viewportHeight,
            elapsedSeconds, lightTime, skyboxWeight,
            ref constants, ref drawCalls, ref submittedIndices);

        ComPtr<ID3D11BlendState> defaultBlend = default;
        var blendFactor = 1f;
        _deviceContext.OMSetBlendState(defaultBlend, ref blendFactor, uint.MaxValue);
        ComPtr<ID3D11DepthStencilState> defaultDepth = default;
        _deviceContext.OMSetDepthStencilState(defaultDepth, 0);
        ComPtr<ID3D11ShaderResourceView> nullResource = default;
        _deviceContext.PSSetShaderResources(0, 1, ref nullResource);
        return new SkyRenderStats(drawCalls, submittedIndices,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private unsafe void RenderOneGlare(
        Wrath335CelestialBody body, uint textureId,
        GlareOcclusion occlusion, Wrath335GlareEvaluator evaluator,
        float alignment, Camera camera, Matrix4x4 projection,
        uint viewportWidth, uint viewportHeight,
        float elapsedSeconds, long lightTime, float skyboxWeight,
        ref SkyCelestialCB constants,
        ref uint drawCalls, ref ulong submittedIndices)
    {
        if (textureId == 0 || !BLPCache.TryGetLoaded(textureId, out var texture))
            return;

        PollGlareOcclusion(occlusion);
        var bodyCenter = body.CameraRelativeCenter;
        constants.BodyCenter = new Vector4(bodyCenter, 0f);
        IssueGlareOcclusion(body, occlusion, camera, projection,
            viewportWidth, viewportHeight, ref constants,
            ref drawCalls, ref submittedIndices);

        var density = _cloudGenerator.SampleDensity(bodyCenter) /
            (float)byte.MaxValue;
        var glare = evaluator.Evaluate(
            lightTime, elapsedSeconds, occlusion.LastVisibility,
            density, skyboxWeight, alignment, 1f);
        if (glare.Opacity <= 0f)
            return;

        var halfSize = glare.Size * Wrath335CelestialQuad.HalfSizeScale;
        var tint = new Vector4(_lighting.SunColor, glare.Opacity);
        _celestialVertices[0] = new Wrath335CelestialVertex
        {
            LocalPosition = new Vector3(0f, -halfSize, halfSize),
            TextureCoordinate = new Vector2(0f, 0f),
            Color = tint
        };
        _celestialVertices[1] = new Wrath335CelestialVertex
        {
            LocalPosition = new Vector3(0f, halfSize, halfSize),
            TextureCoordinate = new Vector2(1f, 0f),
            Color = tint
        };
        _celestialVertices[2] = new Wrath335CelestialVertex
        {
            LocalPosition = new Vector3(0f, -halfSize, -halfSize),
            TextureCoordinate = new Vector2(0f, 1f),
            Color = tint
        };
        _celestialVertices[3] = new Wrath335CelestialVertex
        {
            LocalPosition = new Vector3(0f, halfSize, -halfSize),
            TextureCoordinate = new Vector2(1f, 1f),
            Color = tint
        };
        _deviceContext.UpdateSubresource(_celestialVertexBuffer, 0,
            ref Unsafe.NullRef<Box>(), ref _celestialVertices[0], 0, 0);
        constants.ProjectionTerms = new Vector4(
            projection.M11, projection.M22, projection.M33, projection.M43);
        _deviceContext.UpdateSubresource(_celestialConstantBuffer, 0,
            ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        _deviceContext.OMSetDepthStencilState(_depthDisabledState, 0);
        ApplyBlendMode(3);
        var vertexStride = (uint)Marshal.SizeOf<Wrath335CelestialVertex>();
        var vertexOffset = 0u;
        _deviceContext.IASetPrimitiveTopology(
            D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        _deviceContext.IASetInputLayout(_celestialShader.InputLayout);
        _deviceContext.IASetVertexBuffers(
            0, 1, ref _celestialVertexBuffer, in vertexStride, in vertexOffset);
        _deviceContext.IASetIndexBuffer(
            _celestialIndexBuffer, Format.FormatR16Uint, 0);
        _deviceContext.VSSetShader(_celestialShader.VertexShader,
            ref nullClassInstance, 0);
        _deviceContext.PSSetShader(_celestialShader.PixelShader,
            ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _celestialConstantBuffer);
        _deviceContext.PSSetSamplers(0, 1, ref _samplers[0]);
        _deviceContext.PSSetShaderResources(0, 1, ref texture);
        _deviceContext.DrawIndexed(
            (uint)Wrath335CelestialQuad.FourVertexIndices.Length, 0, 0);
        drawCalls++;
        submittedIndices += (uint)Wrath335CelestialQuad.FourVertexIndices.Length;
    }

    private unsafe void PollGlareOcclusion(GlareOcclusion occlusion)
    {
        for (var index = 0; index < occlusion.Queries.Length; index++)
        {
            if (!occlusion.Pending[index])
                continue;
            ulong visibleSamples = 0;
            var result = _deviceContext.GetData(
                occlusion.Queries[index], &visibleSamples, sizeof(ulong), 1);
            if (result != 0)
                continue;
            occlusion.Pending[index] = false;
            occlusion.LastVisibility = Math.Clamp(
                visibleSamples / Math.Max(occlusion.ProjectedAreas[index], 1f),
                0f, 1f);
        }
    }

    private unsafe void IssueGlareOcclusion(
        Wrath335CelestialBody body, GlareOcclusion occlusion,
        Camera camera, Matrix4x4 projection,
        uint viewportWidth, uint viewportHeight,
        ref SkyCelestialCB constants,
        ref uint drawCalls, ref ulong submittedIndices)
    {
        var freeIndex = Array.FindIndex(occlusion.Pending, static pending => !pending);
        if (freeIndex < 0)
            return;
        var vertexCount = Wrath335CelestialQuad.Build(
            _celestialVertices, body.CameraRelativeCenter.Z,
            body.Size, Vector4.One);
        if (vertexCount == 0)
        {
            occlusion.LastVisibility = 0f;
            return;
        }
        var area = CalculateProjectedGlareArea(
            _celestialVertices.AsSpan(0, vertexCount),
            body.CameraRelativeCenter, camera, projection,
            viewportWidth, viewportHeight);
        if (area <= 1f)
        {
            occlusion.LastVisibility = 0f;
            return;
        }

        _deviceContext.UpdateSubresource(_celestialVertexBuffer, 0,
            ref Unsafe.NullRef<Box>(), ref _celestialVertices[0], 0, 0);
        constants.ProjectionTerms = new Vector4(
            projection.M11, projection.M22,
            Wrath335SkyReference.MinimumSkyDepth, 0f);
        _deviceContext.UpdateSubresource(_celestialConstantBuffer, 0,
            ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        var vertexStride = (uint)Marshal.SizeOf<Wrath335CelestialVertex>();
        var vertexOffset = 0u;
        _deviceContext.IASetPrimitiveTopology(
            D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        _deviceContext.IASetInputLayout(_glareQueryShader.InputLayout);
        _deviceContext.IASetVertexBuffers(
            0, 1, ref _celestialVertexBuffer, in vertexStride, in vertexOffset);
        _deviceContext.IASetIndexBuffer(
            _celestialIndexBuffer, Format.FormatR16Uint, 0);
        _deviceContext.VSSetShader(_glareQueryShader.VertexShader,
            ref nullClassInstance, 0);
        _deviceContext.PSSetShader(_glareQueryShader.PixelShader,
            ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _celestialConstantBuffer);
        _deviceContext.RSSetState(_twoSidedState);
        _deviceContext.OMSetDepthStencilState(_glareQueryDepthState, 0);
        var blendFactor = 1f;
        _deviceContext.OMSetBlendState(
            _glareQueryBlendState, ref blendFactor, uint.MaxValue);
        _deviceContext.Begin(occlusion.Queries[freeIndex]);
        var indexCount = vertexCount == Wrath335CelestialQuad.MaxVertices
            ? (uint)Wrath335CelestialQuad.SixVertexIndices.Length
            : (uint)Wrath335CelestialQuad.FourVertexIndices.Length;
        _deviceContext.DrawIndexed(indexCount, 0, 0);
        _deviceContext.End(occlusion.Queries[freeIndex]);
        occlusion.ProjectedAreas[freeIndex] = area;
        occlusion.Pending[freeIndex] = true;
        drawCalls++;
        submittedIndices += indexCount;
    }

    internal static float CalculateProjectedGlareArea(
        ReadOnlySpan<Wrath335CelestialVertex> vertices,
        Vector3 center, Camera camera, Matrix4x4 projection,
        uint viewportWidth, uint viewportHeight)
    {
        Wrath335CelestialQuad.BuildCameraFacingBasis(
            camera.Front, out var horizontal, out var vertical);
        var view = camera.GetViewMatrix();
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        var front = new Vector3(view.M13, view.M23, view.M33);
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        foreach (var vertex in vertices)
        {
            var position = center +
                horizontal * vertex.LocalPosition.Y +
                vertical * vertex.LocalPosition.Z;
            var depth = Vector3.Dot(position, front);
            if (depth <= 0f)
                return 0f;
            var x = Vector3.Dot(position, right) * projection.M11 /
                depth * viewportWidth * 0.5f;
            var y = Vector3.Dot(position, up) * projection.M22 /
                depth * viewportHeight * 0.5f;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        return (maxX - minX) * (maxY - minY);
    }

    private unsafe void RenderWrathCelestials(
        Camera camera, long lightTime, ref uint drawCalls,
        ref ulong submittedIndices)
    {
        var textures = _activeCelestialTextures;
        if (textures.Sun == 0 && textures.Moon1 == 0)
            return;

        var frame = Wrath335CelestialEvaluator.Evaluate((int)lightTime);
        Wrath335CelestialQuad.BuildCameraFacingBasis(
            camera.Front, out var horizontal, out var vertical);
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix();
        var constants = new SkyCelestialCB
        {
            BillboardHorizontal = new Vector4(horizontal, 0f),
            BillboardVertical = new Vector4(vertical, 0f),
            CameraRight = new Vector4(view.M11, view.M21, view.M31, 0f),
            CameraUp = new Vector4(view.M12, view.M22, view.M32, 0f),
            CameraFront = new Vector4(view.M13, view.M23, view.M33, 0f),
            ProjectionTerms = new Vector4(
                projection.M11, projection.M22, projection.M33, projection.M43)
        };

        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        var vertexStride = (uint)Marshal.SizeOf<Wrath335CelestialVertex>();
        var vertexOffset = 0u;
        _deviceContext.IASetInputLayout(_celestialShader.InputLayout);
        _deviceContext.IASetVertexBuffers(
            0, 1, ref _celestialVertexBuffer, in vertexStride, in vertexOffset);
        _deviceContext.IASetIndexBuffer(_celestialIndexBuffer, Format.FormatR16Uint, 0);
        _deviceContext.VSSetShader(_celestialShader.VertexShader, ref nullClassInstance, 0);
        _deviceContext.PSSetShader(_celestialShader.PixelShader, ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _celestialConstantBuffer);
        _deviceContext.PSSetSamplers(0, 1, ref _samplers[0]);
        ApplyBlendMode(2);

        DrawWrathCelestialBody(frame.Sun, textures.Sun, ref constants,
            ref drawCalls, ref submittedIndices);
        DrawWrathCelestialBody(frame.Moon1, textures.Moon1, ref constants,
            ref drawCalls, ref submittedIndices);
        // In the clear-state SetColors path, moon02 has zero RGB tint. Its
        // weather/override tint is pending; drawing it white would be wrong.

        ComPtr<ID3D11ShaderResourceView> nullTexture = default;
        _deviceContext.PSSetShaderResources(0, 1, ref nullTexture);
    }

    private unsafe void DrawWrathCelestialBody(
        Wrath335CelestialBody body, uint textureId,
        ref SkyCelestialCB constants, ref uint drawCalls,
        ref ulong submittedIndices)
    {
        if (textureId == 0 || !BLPCache.TryGetLoaded(textureId, out var texture))
            return;

        var vertexCount = Wrath335CelestialQuad.Build(
            _celestialVertices, body.CameraRelativeCenter.Z, body.Size,
            new Vector4(_lighting.SunColor, 1f));
        if (vertexCount == 0)
            return;

        constants.BodyCenter = new Vector4(body.CameraRelativeCenter, 0f);
        _deviceContext.UpdateSubresource(
            _celestialVertexBuffer, 0, ref Unsafe.NullRef<Box>(),
            ref _celestialVertices[0], 0, 0);
        _deviceContext.UpdateSubresource(
            _celestialConstantBuffer, 0, ref Unsafe.NullRef<Box>(),
            ref constants, 0, 0);
        _deviceContext.PSSetShaderResources(0, 1, ref texture);
        var indexCount = vertexCount == Wrath335CelestialQuad.MaxVertices
            ? (uint)Wrath335CelestialQuad.SixVertexIndices.Length
            : (uint)Wrath335CelestialQuad.FourVertexIndices.Length;
        _deviceContext.DrawIndexed(indexCount, 0, 0);
        drawCalls++;
        submittedIndices += indexCount;
    }

    private void RenderSkyboxModel(
        Camera camera,
        ParsedDoodadBatch model,
        int flags,
        float opacity,
        bool animateModels,
        long sceneTimeMilliseconds,
        long lightTime,
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
            fogMode = (int)Wrath335M2FogMode.Disabled,
            globalOpacity = opacity
        };

        M2AnimationPose? pose = null;
        if (model.animation is { } animation &&
            (animation.HasAnimatedBones || animation.HasMaterialTracks))
        {
            if (!animateModels &&
                _lastSkyboxPoses.TryGetValue(model.fileDataID, out var last) &&
                ReferenceEquals(last.Animation, animation))
            {
                pose = last.Pose;
            }
            else
            {
                var time = animateModels
                    ? SkyboxAnimationClock.GetTimeMilliseconds(
                        animation, flags, sceneTimeMilliseconds, lightTime)
                    : 0;
                if (!_animationCaches.TryGetValue(model.fileDataID, out var cache))
                {
                    cache = new M2AnimationPoseCache();
                    _animationCaches.Add(model.fileDataID, cache);
                }
                cache.BeginFrame(animation, time, true);
                var frame = new M2AnimationFrameKey(animation.DefaultSequenceIndex, time);
                var key = animation.HasBillboardBones
                    ? new M2AnimationPoseKey(frame, 0, SkyboxTransform * view)
                    : M2AnimationPoseKey.Shared(frame);
                pose = cache.GetPose(animation, key, model.submeshes, true);
                if (pose is not null)
                    _lastSkyboxPoses[model.fileDataID] = (animation, pose);
            }
        }

        constants.hasSkinning = pose?.BonePalette is not null ? 1 : 0;
        if (pose?.BonePalette is { } palette &&
            (!ReferenceEquals(_uploadedPose, pose) || _uploadedPoseVersion != pose.Version))
        {
            _deviceContext.UpdateSubresource(_bonePaletteConstantBuffer, 0,
                ref Unsafe.NullRef<Box>(), ref palette[0], 0, 0);
            _uploadedPose = pose;
            _uploadedPoseVersion = pose.Version;
        }

        _deviceContext.IASetInputLayout(_m2Shader.InputLayout);
        _deviceContext.VSSetShader(_m2Shader.VertexShader, ref nullClassInstance, 0);
        _deviceContext.PSSetShader(_m2Shader.PixelShader, ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _m2ConstantBuffer);
        if (constants.hasSkinning != 0)
            _deviceContext.VSSetConstantBuffers(1, 1, ref _bonePaletteConstantBuffer);
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

        for (var batchIndex = 0; batchIndex < model.submeshes.Length; batchIndex++)
        {
            var batch = model.submeshes[batchIndex];
            constants.vertexShader = checked((int)batch.vertexShaderID);
            constants.pixelShader = checked((int)batch.pixelShaderID);
            constants.blendMode = batch.blendType;
            constants.alphaRef = batch.blendType == 1 ? 128f / 255f : -1f;
            if (pose is not null)
            {
                var material = pose.Materials[batchIndex];
                constants.materialColor = material.Color;
                constants.texMatrix1 = material.TextureMatrix1;
                constants.texMatrix2 = material.TextureMatrix2;
                constants.hasTexMatrix1 = material.HasTextureMatrix1 ? 1 : 0;
                constants.hasTexMatrix2 = material.HasTextureMatrix2 ? 1 : 0;
            }
            else
            {
                constants.materialColor = Vector4.One;
                constants.texMatrix1 = Matrix4x4.Identity;
                constants.texMatrix2 = Matrix4x4.Identity;
                constants.hasTexMatrix1 = 0;
                constants.hasTexMatrix2 = 0;
            }
            _deviceContext.UpdateSubresource(
                _m2ConstantBuffer,
                0,
                ref Unsafe.NullRef<Box>(),
                ref constants,
                0,
                0);

            _deviceContext.RSSetState(IsTwoSided(batch.renderFlags) ? SkyTwoSidedState : SkyCullState);
            ApplySkyboxBlendMode(checked((int)batch.blendType), opacity);

            for (var index = 0; index < batch.material.Length; index++)
            {
                var texture = batch.material[index] == 0
                    ? _missingTexture
                    : BLPCache.GetCurrent(batch.material[index], _missingTexture);
                _deviceContext.PSSetShaderResources((uint)index, 1, ref texture);
                var textureFlagsValue = batch.textureFlags is { } textureFlags && index < textureFlags.Length
                    ? textureFlags[index]
                    : 0;
                var sampler = _samplers[GetSamplerIndex(textureFlagsValue)];
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

    internal static bool SuppressesProceduralSky(
        WorldSkyboxLayer skybox, bool drawable) =>
        drawable && skybox.Opacity > Wrath335SkyReference.SkyboxSuppressionOpacity &&
        (skybox.Flags & Wrath335SkyReference.SkyboxCombineFlag) == 0;

    private static int GetSamplerIndex(uint textureFlags) =>
        ((textureFlags & 0x1) != 0 ? 2 : 0) |
        ((textureFlags & 0x2) != 0 ? 1 : 0);

    public void Dispose()
    {
        if (_activeStarModelId != 0)
            M2Cache.Release(_activeStarModelId, CacheOwnerId);
        _activeStarModelId = 0;
        foreach (var id in _trackedCelestialTextureIds)
            BLPCache.Release(id, CacheOwnerId);
        _trackedCelestialTextureIds.Clear();
        foreach (var id in _trackedGlareTextureIds)
            BLPCache.Release(id, CacheOwnerId);
        _trackedGlareTextureIds.Clear();
        _sunOcclusion.Dispose();
        _moonOcclusion.Dispose();
        foreach (var fileDataId in _trackedSkyboxFileDataIds)
            M2Cache.Release(fileDataId, CacheOwnerId);
        _trackedSkyboxFileDataIds.Clear();
        _animationCaches.Clear();
        _lastSkyboxPoses.Clear();
        foreach (var blendState in _blendStates)
            blendState.Dispose();
        foreach (var sampler in _samplers)
            sampler.Dispose();
        _missingTexture.Dispose();
        foreach (var resource in _cloudResources)
            resource.Dispose();
        foreach (var texture in _cloudTextures)
            texture.Dispose();
        _cloudIndexBuffer.Dispose();
        _cloudVertexBuffer.Dispose();
        _twoSidedState.Dispose();
        _cullState.Dispose();
        _skyScissorTwoSidedState.Dispose();
        _skyScissorCullState.Dispose();
        _glareQueryBlendState.Dispose();
        _glareQueryDepthState.Dispose();
        _depthDisabledState.Dispose();
        _instanceBuffer.Dispose();
        _bonePaletteConstantBuffer.Dispose();
        _m2ConstantBuffer.Dispose();
        _domeIndexBuffer.Dispose();
        _domeVertexBuffer.Dispose();
        _celestialIndexBuffer.Dispose();
        _celestialVertexBuffer.Dispose();
        _celestialConstantBuffer.Dispose();
        _gradientConstantBuffer.Dispose();
    }
}
