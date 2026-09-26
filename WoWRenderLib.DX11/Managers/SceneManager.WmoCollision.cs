using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Managers;

public partial class SceneManager
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WmoCollisionCB
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
    }

    private unsafe void DrawWmoCollisionMesh(
        WorldModel wmo,
        List<WMOContainer> instances,
        Matrix4x4 projection,
        Matrix4x4 view,
        uint instanceStride,
        uint instanceOffset,
        bool restoreTwoSidedRasterizer,
        ref int currentBlendType,
        ref uint drawCalls,
        ref ulong submittedIndexCount)
    {
        var hasCollision = false;
        foreach (var group in wmo.groupBatches)
        {
            if (group.collisionVertexCount == 0)
                continue;
            hasCollision = true;
            break;
        }
        if (!hasCollision)
            return;

        if (wmoCollisionConstantBuffer.Handle is null)
        {
            var description = new BufferDesc
            {
                ByteWidth = (uint)sizeof(WmoCollisionCB),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.ConstantBuffer
            };
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in description, null, ref wmoCollisionConstantBuffer));
        }

        var shader = _shaderManager.GetOrCompileShader("wmo_collision");
        var constants = new WmoCollisionCB { Projection = projection, View = view };
        _deviceContext.UpdateSubresource(wmoCollisionConstantBuffer, 0,
            ref Unsafe.NullRef<Box>(), ref constants, 0, 0);
        ConstantBufferUpdates++;
        _deviceContext.IASetInputLayout(shader.InputLayout);
        _deviceContext.VSSetShader(shader.VertexShader, ref nullClassInstance, 0);
        _deviceContext.PSSetShader(shader.PixelShader, ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref wmoCollisionConstantBuffer);
        _deviceContext.RSSetState(m2TwoSidedRasterizerState);
        ApplyBlendMode(0, ref currentBlendType);

        var collisionStride = (uint)sizeof(WMOCollisionVertex);
        uint collisionOffset = 0;
        for (var visibilityBatchIndex = 0;
             visibilityBatchIndex < _activeWmoVisibilityBatchCount;
             visibilityBatchIndex++)
        {
            var visibilityBatch = _wmoVisibilityBatches[visibilityBatchIndex];
            var visibleInstances = visibilityBatch.InstanceIndices;
            var enabledGroups = visibilityBatch.GroupMask;
            if (visibleInstances.Count == 0)
                continue;

            var hasCollisionGroup = false;
            for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
            {
                if (enabledGroups[groupIndex] && wmo.groupBatches[groupIndex].collisionVertexCount != 0)
                {
                    hasCollisionGroup = true;
                    break;
                }
            }
            if (!hasCollisionGroup)
                continue;

            for (var batchStart = 0; batchStart < visibleInstances.Count; batchStart += MaxInstancesPerBatch)
            {
                var batchSize = Math.Min(MaxInstancesPerBatch, visibleInstances.Count - batchStart);
                MappedSubresource mapped = default;
                SilkMarshal.ThrowHResult(_deviceContext.Map(
                    instanceMatrixBuffer, 0, Map.WriteDiscard, 0, ref mapped));
                var destination = new Span<Matrix4x4>(mapped.PData, batchSize);
                for (var instanceIndex = 0; instanceIndex < batchSize; instanceIndex++)
                    destination[instanceIndex] = instances[
                        visibleInstances[batchStart + instanceIndex]].GetModelMatrix();
                _deviceContext.Unmap(instanceMatrixBuffer, 0);
                InstanceBufferMapCalls++;
                _deviceContext.IASetVertexBuffers(1, 1, ref instanceMatrixBuffer,
                    in instanceStride, in instanceOffset);
                VertexBufferBindings++;

                for (var groupIndex = 0; groupIndex < wmo.groupBatches.Length; groupIndex++)
                {
                    if (!enabledGroups[groupIndex])
                        continue;
                    var group = wmo.groupBatches[groupIndex];
                    if (group.collisionVertexCount == 0)
                        continue;

                    var collisionVertexBuffer = group.collisionVertexBuffer;
                    _deviceContext.IASetVertexBuffers(0, 1, ref collisionVertexBuffer,
                        in collisionStride, in collisionOffset);
                    VertexBufferBindings++;
                    _deviceContext.DrawInstanced(group.collisionVertexCount,
                        (uint)batchSize, 0, 0);
                    drawCalls++;
                    WmoDrawCalls++;
                    WmoSubmittedInstances += (uint)batchSize;
                    var submittedVertices = (ulong)group.collisionVertexCount * (uint)batchSize;
                    submittedIndexCount += submittedVertices;
                    WmoSubmittedIndices += submittedVertices;
                }
            }
        }

        // The next WMO still uses the ordinary material program and its own
        // cbuffer; restore the bindings changed by this diagnostic pass.
        _deviceContext.RSSetState(restoreTwoSidedRasterizer
            ? m2TwoSidedRasterizerState : wmoRasterizerState);
        _deviceContext.IASetInputLayout(wmoShaderProgram.InputLayout);
        _deviceContext.VSSetShader(wmoShaderProgram.VertexShader, ref nullClassInstance, 0);
        _deviceContext.PSSetShader(wmoShaderProgram.PixelShader, ref nullClassInstance, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref wmoPerObjectConstantBuffer);
    }
}
