using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.Direct3D;
using Silk.NET.DXGI;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal sealed class M2EffectRenderer(
    ComPtr<ID3D11Device> device,
    ComPtr<ID3D11DeviceContext> context) : IDisposable
{
    private const int MaxEdges = 4096;
    private const int StreamVertexCapacity = MaxEdges * 16;
    private const int StreamIndexCapacity = StreamVertexCapacity * 3 / 2;
    private ShaderManager? _shaderManager;
    private ComPtr<ID3D11Buffer> _vertexBuffer;
    private ComPtr<ID3D11Buffer> _indexBuffer;
    private ComPtr<ID3D11Buffer> _constantBuffer;
    private int _vertexCursor;
    private int _indexCursor;
    private readonly ConditionalWeakTable<M2Container, RibbonCacheEntry> _ribbonCache = new();
    private readonly ConditionalWeakTable<M2Container, ParticleCacheEntry> _particleCache = new();

    private sealed class RibbonCacheEntry
    {
        public M2Animation? Animation;
        public M2AnimationFrameKey Frame;
        public M2RibbonMesh[] Meshes = [];
    }

    private sealed class ParticleCacheEntry
    {
        public M2Animation? Animation;
        public M2AnimationFrameKey Frame;
        public Matrix4x4 ModelToView;
        public uint Seed;
        public M2RibbonMesh[] Meshes = [];
        public bool[] Built = [];
    }

    private struct RibbonConstants
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
        public Matrix4x4 Model;
        public float AlphaReference;
        public Vector3 Padding;
    }

    public unsafe void Initialize(ShaderManager shaderManager)
    {
        _shaderManager = shaderManager;
        shaderManager.GetOrCompileShader("m2_effect");
        var vertexDesc = new BufferDesc
        {
            ByteWidth = (uint)(StreamVertexCapacity * sizeof(M2RibbonVertex)),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.VertexBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write
        };
        SilkMarshal.ThrowHResult(device.CreateBuffer(in vertexDesc, null, ref _vertexBuffer));
        var indexDesc = vertexDesc;
        indexDesc.ByteWidth = (uint)(StreamIndexCapacity * sizeof(ushort));
        indexDesc.BindFlags = (uint)BindFlag.IndexBuffer;
        SilkMarshal.ThrowHResult(device.CreateBuffer(in indexDesc, null, ref _indexBuffer));
        var constantsDesc = new BufferDesc
        {
            ByteWidth = (uint)sizeof(RibbonConstants),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ConstantBuffer
        };
        SilkMarshal.ThrowHResult(device.CreateBuffer(in constantsDesc, null, ref _constantBuffer));
    }

    public M2RibbonMesh GetRibbonMesh(
        M2Container instance,
        M2Animation animation,
        int ribbonIndex,
        M2AnimationFrameKey frame)
    {
        var entry = _ribbonCache.GetValue(instance, static _ => new RibbonCacheEntry());
        if (!ReferenceEquals(entry.Animation, animation) ||
            entry.Frame != frame || entry.Meshes.Length != animation.Ribbons.Length)
        {
            entry.Animation = animation;
            entry.Frame = frame;
            if (entry.Meshes.Length != animation.Ribbons.Length)
                entry.Meshes = new M2RibbonMesh[animation.Ribbons.Length];
            for (var i = 0; i < entry.Meshes.Length; i++)
                entry.Meshes[i] = M2RibbonMeshBuilder.Build(
                    animation, animation.Ribbons[i], frame.SequenceIndex,
                    frame.TimeMilliseconds);
        }
        return entry.Meshes[ribbonIndex];
    }

    public M2RibbonMesh GetParticleMesh(
        M2Container instance,
        M2Animation animation,
        int particleIndex,
        M2AnimationFrameKey frame,
        Matrix4x4 modelToView)
    {
        var entry = _particleCache.GetValue(instance, static _ => new ParticleCacheEntry());
        var seed = instance.UniqueID ^ (uint)instance.WmoDoodadIndex * 0x9E3779B9u;
        if (!ReferenceEquals(entry.Animation, animation) ||
            entry.Frame != frame || entry.ModelToView != modelToView ||
            entry.Seed != seed || entry.Meshes.Length != animation.Particles.Length)
        {
            entry.Animation = animation;
            entry.Frame = frame;
            entry.ModelToView = modelToView;
            entry.Seed = seed;
            if (entry.Meshes.Length != animation.Particles.Length)
            {
                entry.Meshes = new M2RibbonMesh[animation.Particles.Length];
                entry.Built = new bool[animation.Particles.Length];
            }
            else
                Array.Clear(entry.Built);
        }
        if (!entry.Built[particleIndex])
        {
            var previous = entry.Meshes[particleIndex];
            entry.Meshes[particleIndex] = M2ParticleMeshBuilder.BuildSupported(
                animation, animation.Particles[particleIndex], frame.SequenceIndex,
                frame.TimeMilliseconds, seed, modelToView,
                previous.Vertices, previous.Indices);
            entry.Built[particleIndex] = true;
        }
        return entry.Meshes[particleIndex];
    }

    /// <summary>
    /// Return the last rendered mesh without sampling particle tracks or the new
    /// camera. A newly visible emitter is sampled once at the paused scene time.
    /// </summary>
    public M2RibbonMesh GetFrozenParticleMesh(
        M2Container instance,
        M2Animation animation,
        int particleIndex,
        M2AnimationFrameKey fallbackFrame,
        Matrix4x4 modelToView)
    {
        var entry = _particleCache.GetValue(instance, static _ => new ParticleCacheEntry());
        var seed = instance.UniqueID ^ (uint)instance.WmoDoodadIndex * 0x9E3779B9u;
        if (!ReferenceEquals(entry.Animation, animation) || entry.Seed != seed ||
            entry.Meshes.Length != animation.Particles.Length)
        {
            entry.Animation = animation;
            entry.Seed = seed;
            entry.Meshes = new M2RibbonMesh[animation.Particles.Length];
            entry.Built = new bool[animation.Particles.Length];
            entry.Frame = new M2AnimationFrameKey(-1, -1);
        }

        if (!entry.Built[particleIndex])
        {
            entry.Meshes[particleIndex] = M2ParticleMeshBuilder.BuildSupported(
                animation, animation.Particles[particleIndex],
                fallbackFrame.SequenceIndex, fallbackFrame.TimeMilliseconds,
                seed, modelToView);
            entry.Built[particleIndex] = true;
            // The fallback mesh must be refreshed if live playback resumes at
            // the same frame and camera that preceded the pause.
            entry.Frame = new M2AnimationFrameKey(-1, -1);
        }
        return entry.Meshes[particleIndex];
    }

    public unsafe void Begin()
    {
        if (_shaderManager is null)
            return;
        var shader = _shaderManager.GetOrCompileShader("m2_effect");
        context.IASetInputLayout(shader.InputLayout);
        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        context.VSSetShader(shader.VertexShader, ref nullClassInstance, 0);
        context.PSSetShader(shader.PixelShader, ref nullClassInstance, 0);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.VSSetConstantBuffers(0, 1, ref _constantBuffer);
        context.PSSetConstantBuffers(0, 1, ref _constantBuffer);
        var vertexBuffer = _vertexBuffer;
        uint stride = (uint)sizeof(M2RibbonVertex);
        uint offset = 0;
        context.IASetVertexBuffers(0, 1, ref vertexBuffer, in stride, in offset);
        context.IASetIndexBuffer(_indexBuffer, Format.FormatR16Uint, 0);
    }

    public void BeginFrame()
    {
        _vertexCursor = 0;
        _indexCursor = 0;
    }

    public unsafe void Draw(
        M2RibbonMesh mesh,
        Matrix4x4 model,
        Matrix4x4 view,
        Matrix4x4 projection,
        float alphaReference,
        ComPtr<ID3D11ShaderResourceView> texture,
        ComPtr<ID3D11SamplerState> sampler)
    {
        if (mesh.Indices.Length == 0 || _shaderManager is null)
            return;
        if (mesh.Vertices.Length > StreamVertexCapacity ||
            mesh.Indices.Length > StreamIndexCapacity)
            throw new InvalidOperationException("M2 effect mesh exceeds the streaming buffer capacity.");

        // DISCARD once for a new stream segment, then append without renaming
        // the buffers for every emitter in the frame.
        var discard = _vertexCursor == 0 ||
            _vertexCursor + mesh.Vertices.Length > StreamVertexCapacity ||
            _indexCursor + mesh.Indices.Length > StreamIndexCapacity;
        if (discard)
            _vertexCursor = _indexCursor = 0;
        var mapMode = discard ? Map.WriteDiscard : Map.WriteNoOverwrite;
        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(context.Map(_vertexBuffer, 0, mapMode, 0, ref mapped));
        mesh.Vertices.AsSpan().CopyTo(
            new Span<M2RibbonVertex>(mapped.PData, StreamVertexCapacity)
                .Slice(_vertexCursor, mesh.Vertices.Length));
        context.Unmap(_vertexBuffer, 0);
        SilkMarshal.ThrowHResult(context.Map(_indexBuffer, 0, mapMode, 0, ref mapped));
        mesh.Indices.AsSpan().CopyTo(
            new Span<ushort>(mapped.PData, StreamIndexCapacity)
                .Slice(_indexCursor, mesh.Indices.Length));
        context.Unmap(_indexBuffer, 0);

        var constants = new RibbonConstants
        {
            Projection = projection,
            View = view,
            Model = model,
            AlphaReference = alphaReference,
            Padding = Vector3.Zero
        };
        context.UpdateSubresource(_constantBuffer, 0, ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
        context.PSSetShaderResources(0, 1, ref texture);
        context.PSSetSamplers(0, 1, ref sampler);
        context.DrawIndexed((uint)mesh.Indices.Length, (uint)_indexCursor, _vertexCursor);
        _vertexCursor += mesh.Vertices.Length;
        _indexCursor += mesh.Indices.Length;
    }

    public void Dispose()
    {
        _constantBuffer.Dispose();
        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
    }
}
