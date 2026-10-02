using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;
using M2MaterialFlags = WoWLib.Formats.M2.Root.Record.MaterialFlags;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct M2MeshSubmission(
    M2InstancePacket Packet,
    ParsedDoodadBatch Model,
    IReadOnlyList<M2AnimationDrawGroup> Groups,
    bool HasOpaque,
    bool HasTranslucent,
    Wrath335M2MeshWater[]? Water = null);

// Borrowed pipeline/cache resources. The renderer never disposes these handles.
internal readonly record struct M2MeshRenderBindings(
    CompiledShader Shader,
    ComPtr<ID3D11RasterizerState> OneSidedRasterizer,
    ComPtr<ID3D11RasterizerState> TwoSidedRasterizer,
    ComPtr<ID3D11BlendState>[] BlendStates,
    ComPtr<ID3D11SamplerState>[] Samplers,
    Func<uint, ComPtr<ID3D11ShaderResourceView>> ResolveTexture);

internal struct M2MeshRenderStats
{
    public uint M2DrawCalls;
    public uint M2SubmittedInstances;
    public ulong M2SubmittedIndices;
    public uint VertexBufferBindings;
    public uint IndexBufferBindings;
    public uint ConstantBufferUpdates;
    public uint InstanceBufferMapCalls;
    public uint TextureBindingCalls;
    public uint BlendStateBindings;
    public double SubmissionTimeMs;
}

internal static class M2MeshMaterialPolicy
{
    internal static bool IsTwoSided(ushort renderFlags) =>
        (renderFlags & (ushort)M2MaterialFlags.TwoSided) != 0;

    internal static int GetSamplerIndex(uint textureFlags) =>
        ((textureFlags & 0x1) != 0 ? 2 : 0) |
        ((textureFlags & 0x2) != 0 ? 1 : 0);
}

/// <summary>
/// Owns mesh constants, the bone palette, instance uploads and mesh depth states.
/// Device/context, mesh buffers, shaders, samplers, rasterizers and textures are borrowed.
/// </summary>
internal sealed class M2MeshRenderer : IDisposable
{
    private const int MaxInstancesPerBatch = 1024;
    private readonly ComPtr<ID3D11Device> device;
    private readonly ComPtr<ID3D11DeviceContext> _deviceContext;
    private readonly M2DepthStateController _m2DepthStates;
    private ComPtr<ID3D11Buffer> m2PerObjectConstantBuffer;
    private ComPtr<ID3D11Buffer> m2BonePaletteConstantBuffer;
    private ComPtr<ID3D11Buffer> instanceBuffer;
    private M2AnimationPose? _uploadedM2Pose;
    private long _uploadedM2PoseVersion;
    private readonly ComPtr<ID3D11ShaderResourceView>[] _srvScratch = new ComPtr<ID3D11ShaderResourceView>[16];
    private readonly ComPtr<ID3D11SamplerState>[] _samplerScratch = new ComPtr<ID3D11SamplerState>[4];
    private readonly int[] _instanceIndices = new int[MaxInstancesPerBatch];

    internal M2MeshRenderer(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context)
    {
        this.device = device;
        _deviceContext = context;
        _m2DepthStates = new(device, context);
    }

    internal unsafe void Initialize()
    {
        if (instanceBuffer.Handle != null)
            return;
        try
        {
            var description = new BufferDesc
            {
                ByteWidth = (uint)sizeof(M2PerObjectCB),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.ConstantBuffer
            };
            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref m2PerObjectConstantBuffer));
            description.ByteWidth = (uint)(M2Animation.MaxGpuBones * sizeof(Matrix4x4));
            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref m2BonePaletteConstantBuffer));
            description = new BufferDesc
            {
                ByteWidth = (uint)(MaxInstancesPerBatch * sizeof(M2InstanceData)),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write
            };
            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref instanceBuffer));
        }
        catch
        {
            ReleaseBuffers();
            throw;
        }
    }

    /// <summary>
    /// Submits the selected mesh phase in caller order. Restores default depth and
    /// the one-sided rasterizer even on failure. Leaves the M2 layout, triangle topology,
    /// shaders, buffers, textures, samplers and last blend state bound; callers must
    /// rebind these before a different pass. Fog buffer slot 4 and render targets are borrowed.
    /// </summary>
    internal unsafe M2MeshRenderStats Draw(
        IReadOnlyList<M2MeshSubmission> m2MeshSubmissions, bool translucent,
        M2PerObjectCB m2ConstantBuffer, long frameNumber, in WrathFogCB fogCB,
        WrathFogCB? doodadCurrentFog, in M2MeshRenderBindings bindings,
        Wrath335M2QueueMask waterSide = Wrath335M2QueueMask.None)
    {
        if (instanceBuffer.Handle == null)
            throw new InvalidOperationException("Initialize the M2 renderer before drawing.");
        var started = Stopwatch.GetTimestamp();
        var stats = new M2MeshRenderStats();
        uint m2VertexStride = (uint)sizeof(M2Vertex), m2VertexOffset = 0, instanceOffset = 0;
        var currentBlendType = -1;
        ComPtr<ID3D11ClassInstance> nullClassInstance = default;
        var shader = bindings.Shader;
        _m2DepthStates.BeginPass();
        _m2DepthStates.Apply(false, 0);
        _deviceContext.RSSetState(bindings.OneSidedRasterizer);
        _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        _deviceContext.IASetInputLayout(shader.InputLayout);
        _deviceContext.VSSetShader(shader.VertexShader, ref nullClassInstance, 0);
        _deviceContext.PSSetShader(shader.PixelShader, ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref m2PerObjectConstantBuffer);
        _deviceContext.PSSetConstantBuffers(0, 1, ref m2PerObjectConstantBuffer);
        _deviceContext.VSSetConstantBuffers(1, 1, ref m2BonePaletteConstantBuffer);
        var lastM2BlendMode = float.NaN;
        var lastM2VertexShader = int.MinValue;
        var lastM2PixelShader = int.MinValue;
        var lastM2AlphaRef = float.NaN;
        var lastM2HasSkinning = -1;
        var lastDoodadMaterialLit = -1;
        var lastM2FogMode = -1;
        var lastM2HasAnimation = false;
        var lastM2MaterialColor = Vector4.Zero;
        var lastM2TexMatrix1 = Matrix4x4.Identity;
        var lastM2TexMatrix2 = Matrix4x4.Identity;
        var lastM2HasTexMatrix1 = -1;
        var lastM2HasTexMatrix2 = -1;
        bool? lastM2TwoSided = null;
        try
        {
            foreach (var submission in m2MeshSubmissions)
            {
                if (translucent ? !submission.HasTranslucent : !submission.HasOpaque)
                    continue;
                // Non-12340 submissions retain one transparent draw in the above
                // partition. The default API draws the original unsplit phase.
                if (translucent && waterSide == Wrath335M2QueueMask.BelowWater && submission.Water == null)
                    continue;
                var packet = submission.Packet;
                var m2 = submission.Model;
                var animationGroups = submission.Groups;
                var vertexBuffer = m2.vertexBuffer;
                var indiceBuffer = m2.indiceBuffer;

                _deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in m2VertexStride, in m2VertexOffset);
                _deviceContext.IASetIndexBuffer(indiceBuffer, Format.FormatR16Uint, 0);
                stats.VertexBufferBindings++;
                stats.IndexBufferBindings++;

                foreach (var animationGroup in animationGroups)
                {
                    var pose = animationGroup.Pose;
                    m2ConstantBuffer.hasSkinning = pose?.BonePalette is not null ? 1 : 0;
                    if (pose?.BonePalette is { } palette &&
                        (!ReferenceEquals(_uploadedM2Pose, pose) ||
                         _uploadedM2PoseVersion != pose.Version))
                    {
                        _deviceContext.UpdateSubresource(m2BonePaletteConstantBuffer, 0,
                            ref Unsafe.NullRef<Box>(), ref palette[0], 0, 0);
                        stats.ConstantBufferUpdates++;
                        _uploadedM2Pose = pose;
                        _uploadedM2PoseVersion = pose.Version;
                    }

                    var cursor = 0;
                    while (cursor < animationGroup.Indices.Count)
                    {
                        var batchCount = 0;
                        while (cursor < animationGroup.Indices.Count && batchCount < MaxInstancesPerBatch)
                        {
                            var index = animationGroup.Indices[cursor++];
                            if (translucent && waterSide != Wrath335M2QueueMask.None &&
                                submission.Water is { } water && !water[index].Includes(waterSide)) continue;
                            _instanceIndices[batchCount++] = index;
                        }
                        if (batchCount == 0) continue;

                        unsafe
                        {
                            MappedSubresource mapped = default;
                            SilkMarshal.ThrowHResult(_deviceContext.Map(instanceBuffer, 0, Map.WriteDiscard, 0, ref mapped));

                            try
                            {
                                var dest = new Span<M2InstanceData>(mapped.PData, batchCount);
                                for (int i = 0; i < batchCount; i++)
                                {
                                    var index = _instanceIndices[i];
                                    dest[i] = M2InstanceData.ForScene(packet.Instances[index], packet.WorldMatrices[index],
                                        frameNumber, fogCB, doodadCurrentFog);
                                    if (translucent && waterSide != Wrath335M2QueueMask.None && submission.Water is { } water)
                                    {
                                        dest[i].WaterPlane = water[index].ClipPlane(waterSide);
                                        dest[i].RenderParameters.Z = water[index].Selection.Above && water[index].Selection.Below ? 1 : 0;
                                    }
                                }
                            }
                            finally { _deviceContext.Unmap(instanceBuffer, 0); }
                            stats.InstanceBufferMapCalls++;
                        }

                        var m2InstanceStride = (uint)Marshal.SizeOf<M2InstanceData>();
                        _deviceContext.IASetVertexBuffers(1, 1, ref instanceBuffer, in m2InstanceStride, in instanceOffset);
                        stats.VertexBufferBindings++;

                        for (int j = 0; j < m2.submeshes.Length; j++)
                        {
                            var batch = m2.submeshes[j];
                            var materialState = Wrath335M2FadeMaterial.Resolve((int)batch.blendType,
                                m2.usesLegacyDepthFlags, batch.renderFlags, pose?.Materials[j].Color.W ?? 1f,
                                animationGroup.NativeDoodadFade, animationGroup.DoodadOpacity, batch.baseBlendType);
                            if (!materialState.Draw || materialState.Translucent != translucent)
                                continue;

                            _m2DepthStates.Apply(m2.usesLegacyDepthFlags, batch.renderFlags);

                            var isTwoSided = M2MeshMaterialPolicy.IsTwoSided(batch.renderFlags);
                            if (lastM2TwoSided != isTwoSided)
                            {
                                _deviceContext.RSSetState(
                                    isTwoSided ? bindings.TwoSidedRasterizer : bindings.OneSidedRasterizer);
                                lastM2TwoSided = isTwoSided;
                            }

                            m2ConstantBuffer.blendMode = batch.blendType;
                            ApplyBlendMode(materialState.BlendState, bindings.BlendStates, ref currentBlendType, ref stats);
                            m2ConstantBuffer.alphaRef = materialState.AlphaReference;
                            m2ConstantBuffer.vertexShader = (int)batch.vertexShaderID;
                            m2ConstantBuffer.pixelShader = (int)batch.pixelShaderID;
                            m2ConstantBuffer.doodadMaterialLit = Wrath335WmoDoodadLighting.IsMaterialLit(
                                batch.renderFlags, (int)batch.blendType) ? 1 : 0;
                            m2ConstantBuffer.fogMode = (int)Wrath335M2FogPolicy.ForMaterial(
                                m2.usesLegacyDepthFlags, (int)batch.blendType,
                                (batch.renderFlags & (ushort)M2MaterialFlags.Unfogged) != 0);
                            if (pose is not null)
                            {
                                var material = pose.Materials[j];
                                m2ConstantBuffer.materialColor = material.Color;
                                m2ConstantBuffer.texMatrix1 = material.TextureMatrix1;
                                m2ConstantBuffer.texMatrix2 = material.TextureMatrix2;
                                m2ConstantBuffer.hasTexMatrix1 = material.HasTextureMatrix1 ? 1 : 0;
                                m2ConstantBuffer.hasTexMatrix2 = material.HasTextureMatrix2 ? 1 : 0;
                            }
                            else
                            {
                                m2ConstantBuffer.materialColor = Vector4.One;
                                m2ConstantBuffer.texMatrix1 = Matrix4x4.Identity;
                                m2ConstantBuffer.texMatrix2 = Matrix4x4.Identity;
                                m2ConstantBuffer.hasTexMatrix1 = 0;
                                m2ConstantBuffer.hasTexMatrix2 = 0;
                            }

                            if (m2ConstantBuffer.blendMode != lastM2BlendMode ||
                                m2ConstantBuffer.vertexShader != lastM2VertexShader ||
                                m2ConstantBuffer.pixelShader != lastM2PixelShader ||
                                m2ConstantBuffer.alphaRef != lastM2AlphaRef ||
                                m2ConstantBuffer.fogMode != lastM2FogMode ||
                                m2ConstantBuffer.hasSkinning != lastM2HasSkinning ||
                                m2ConstantBuffer.doodadMaterialLit != lastDoodadMaterialLit ||
                                lastM2HasAnimation != (pose is not null) ||
                                m2ConstantBuffer.materialColor != lastM2MaterialColor ||
                                !m2ConstantBuffer.texMatrix1.Equals(lastM2TexMatrix1) ||
                                !m2ConstantBuffer.texMatrix2.Equals(lastM2TexMatrix2) ||
                                m2ConstantBuffer.hasTexMatrix1 != lastM2HasTexMatrix1 ||
                                m2ConstantBuffer.hasTexMatrix2 != lastM2HasTexMatrix2)
                            {
                                _deviceContext.UpdateSubresource(m2PerObjectConstantBuffer, 0, ref Unsafe.NullRef<Box>(), ref m2ConstantBuffer, 0, 0);
                                stats.ConstantBufferUpdates++;
                                lastM2BlendMode = m2ConstantBuffer.blendMode;
                                lastM2VertexShader = m2ConstantBuffer.vertexShader;
                                lastM2PixelShader = m2ConstantBuffer.pixelShader;
                                lastM2AlphaRef = m2ConstantBuffer.alphaRef;
                                lastM2FogMode = m2ConstantBuffer.fogMode;
                                lastM2HasSkinning = m2ConstantBuffer.hasSkinning;
                                lastDoodadMaterialLit = m2ConstantBuffer.doodadMaterialLit;
                                lastM2HasAnimation = pose is not null;
                                lastM2MaterialColor = m2ConstantBuffer.materialColor;
                                lastM2TexMatrix1 = m2ConstantBuffer.texMatrix1;
                                lastM2TexMatrix2 = m2ConstantBuffer.texMatrix2;
                                lastM2HasTexMatrix1 = m2ConstantBuffer.hasTexMatrix1;
                                lastM2HasTexMatrix2 = m2ConstantBuffer.hasTexMatrix2;
                            }

                            for (int s = 0; s < batch.material.Length; s++)
                                _srvScratch[s] = bindings.ResolveTexture(batch.material[s]);
                            if (batch.material.Length > 0)
                            {
                                _deviceContext.PSSetShaderResources(0, (uint)batch.material.Length, ref _srvScratch[0]);
                                var samplerCount = Math.Min(batch.material.Length, _samplerScratch.Length);
                                for (var s = 0; s < samplerCount; s++)
                                {
                                    var flags = batch.textureFlags is { } textureFlags && s < textureFlags.Length
                                        ? textureFlags[s]
                                        : 0;
                                    _samplerScratch[s] = bindings.Samplers[M2MeshMaterialPolicy.GetSamplerIndex(flags)];
                                }
                                _deviceContext.PSSetSamplers(0, (uint)samplerCount, ref _samplerScratch[0]);
                                stats.TextureBindingCalls++;
                            }

                            _deviceContext.DrawIndexedInstanced(batch.numFaces, (uint)batchCount, batch.firstFace, 0, 0);
                            stats.M2DrawCalls++;
                            stats.M2SubmittedInstances += (uint)batchCount;
                            var submittedIndices = (ulong)batch.numFaces * (uint)batchCount;
                            stats.M2SubmittedIndices += submittedIndices;
                        }
                    }
                }
            }
        }
        finally
        {
            _m2DepthStates.EndPass();
            _deviceContext.RSSetState(bindings.OneSidedRasterizer);
        }
        stats.SubmissionTimeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return stats;
    }

    private void ApplyBlendMode(int index, ComPtr<ID3D11BlendState>[] states,
        ref int current, ref M2MeshRenderStats stats)
    {
        if (index < 0 || index >= states.Length)
            index = 0;
        if (current == index)
            return;
        float factor = 1;
        _deviceContext.OMSetBlendState(states[index], ref factor, uint.MaxValue);
        current = index;
        stats.BlendStateBindings++;
    }

    private void ReleaseBuffers()
    {
        instanceBuffer.Dispose();
        m2BonePaletteConstantBuffer.Dispose();
        m2PerObjectConstantBuffer.Dispose();
        instanceBuffer = default;
        m2BonePaletteConstantBuffer = default;
        m2PerObjectConstantBuffer = default;
        _uploadedM2Pose = null;
    }

    public void Dispose()
    {
        ReleaseBuffers();
        _m2DepthStates.Dispose();
    }
}
