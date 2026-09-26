using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Owns the GPU resources and transient pipeline state used to draw scene bounds.
/// The scene manager supplies ordering and a stable object snapshot/lock boundary.
/// </summary>
internal sealed class DebugBoundsRenderer(
    ComPtr<ID3D11Device> device,
    ComPtr<ID3D11DeviceContext> deviceContext) : IDisposable
{
    private const int SphereSegments = 32;
    private const int SphereVertexCount = 3 * SphereSegments * 2;

    private readonly ComPtr<ID3D11Device> _device = device;
    private readonly ComPtr<ID3D11DeviceContext> _deviceContext = deviceContext;
    private CompiledShader _shader;
    private ComPtr<ID3D11Buffer> _constantBuffer;
    private ComPtr<ID3D11Buffer> _boxVertexBuffer;
    private ComPtr<ID3D11Buffer> _sphereVertexBuffer;
    private ComPtr<ID3D11RasterizerState> _wireframeRasterizerState;
    private ComPtr<ID3D11DepthStencilState> _depthStencilState;
    private ComPtr<ID3D11ClassInstance> _nullClassInstance;
    private bool _initialized;

    [StructLayout(LayoutKind.Sequential)]
    private struct BoundsConstantBuffer
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
        public Matrix4x4 Model;
        public Vector4 Color;
    }

    public unsafe void Initialize(CompiledShader shader)
    {
        if (_initialized)
            return;

        _shader = shader;

        var constantBufferDesc = new BufferDesc
        {
            ByteWidth = (uint)sizeof(BoundsConstantBuffer),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write
        };
        SilkMarshal.ThrowHResult(_device.CreateBuffer(
            in constantBufferDesc,
            null,
            ref _constantBuffer));

        var boxBufferDesc = new BufferDesc
        {
            ByteWidth = (uint)(24 * sizeof(Vector3)),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.VertexBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write
        };
        SilkMarshal.ThrowHResult(_device.CreateBuffer(
            in boxBufferDesc,
            null,
            ref _boxVertexBuffer));

        var sphereBufferDesc = new BufferDesc
        {
            ByteWidth = (uint)(SphereVertexCount * sizeof(Vector3)),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.VertexBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write
        };
        SilkMarshal.ThrowHResult(_device.CreateBuffer(
            in sphereBufferDesc,
            null,
            ref _sphereVertexBuffer));
        UploadUnitSphere();

        var wireframeDesc = new RasterizerDesc
        {
            FillMode = FillMode.Wireframe,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthClipEnable = true
        };
        SilkMarshal.ThrowHResult(_device.CreateRasterizerState(
            in wireframeDesc,
            ref _wireframeRasterizerState));

        var depthDesc = new DepthStencilDesc
        {
            DepthEnable = false,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunc.Always,
            StencilEnable = false
        };
        SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(
            in depthDesc,
            ref _depthStencilState));

        _initialized = true;
    }

    public uint Render(
        IReadOnlyList<Container3D> sceneObjects,
        bool showBoundingBoxes,
        bool showBoundingSpheres,
        bool showSelection,
        Matrix4x4 projection,
        Matrix4x4 view,
        ComPtr<ID3D11RasterizerState> defaultRasterizerState)
    {
        if (!_initialized)
            throw new InvalidOperationException("The debug bounds renderer has not been initialized.");

        if (!showBoundingBoxes && !showBoundingSpheres && !showSelection)
            return 0;

        _deviceContext.RSSetState(_wireframeRasterizerState);
        _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
        _deviceContext.IASetInputLayout(_shader.InputLayout);
        _deviceContext.VSSetShader(_shader.VertexShader, ref _nullClassInstance, 0);
        _deviceContext.PSSetShader(_shader.PixelShader, ref _nullClassInstance, 0);
        _deviceContext.OMSetDepthStencilState(_depthStencilState, 0);

        uint drawCalls = 0;
        foreach (var sceneObject in sceneObjects)
        {
            if (sceneObject is ADTContainer)
                continue;

            var renderSelection = showSelection && sceneObject.IsSelected;
            if (!showBoundingBoxes && !showBoundingSpheres && !renderSelection)
                continue;

            var color = renderSelection
                ? Vector4.One
                : new Vector4(1, 1, 0, 1);

            if (showBoundingBoxes || renderSelection)
            {
                var bounds = sceneObject.GetBoundingBox();
                if (bounds.HasValue &&
                    float.IsFinite(bounds.Value.Min.X) &&
                    float.IsFinite(bounds.Value.Max.X) &&
                    TryGetLocalBounds(sceneObject, out var localBounds, out var modelMatrix))
                {
                    DrawBoundingBox(localBounds, modelMatrix, color, projection, view);
                    drawCalls++;
                }
            }

            if (showBoundingSpheres && !renderSelection)
            {
                var sphere = sceneObject.GetBoundingSphere();
                if (sphere.HasValue)
                {
                    DrawBoundingSphere(
                        sphere.Value,
                        new Vector4(0, 0.5f, 1, 1),
                        projection,
                        view);
                    drawCalls++;
                }
            }
        }

        RestorePipelineState(defaultRasterizerState);
        return drawCalls;
    }

    private static bool TryGetLocalBounds(
        Container3D sceneObject,
        out BoundingBox bounds,
        out Matrix4x4 modelMatrix)
    {
        switch (sceneObject)
        {
            case WMOContainer wmo:
                bounds = wmo.GetLocalBoundingBox();
                modelMatrix = wmo.GetModelMatrix();
                return true;
            case M2Container m2:
                bounds = m2.GetLocalBoundingBox();
                modelMatrix = m2.GetModelMatrix();
                return true;
            default:
                bounds = default;
                modelMatrix = default;
                return false;
        }
    }

    private unsafe void DrawBoundingBox(
        BoundingBox bounds,
        Matrix4x4 modelMatrix,
        Vector4 color,
        Matrix4x4 projection,
        Matrix4x4 view)
    {
        var min = bounds.Min;
        var max = bounds.Max;
        Span<Vector3> vertices = stackalloc Vector3[24];
        var index = 0;

        vertices[index++] = new(min.X, min.Y, min.Z); vertices[index++] = new(max.X, min.Y, min.Z);
        vertices[index++] = new(max.X, min.Y, min.Z); vertices[index++] = new(max.X, min.Y, max.Z);
        vertices[index++] = new(max.X, min.Y, max.Z); vertices[index++] = new(min.X, min.Y, max.Z);
        vertices[index++] = new(min.X, min.Y, max.Z); vertices[index++] = new(min.X, min.Y, min.Z);
        vertices[index++] = new(min.X, max.Y, min.Z); vertices[index++] = new(max.X, max.Y, min.Z);
        vertices[index++] = new(max.X, max.Y, min.Z); vertices[index++] = new(max.X, max.Y, max.Z);
        vertices[index++] = new(max.X, max.Y, max.Z); vertices[index++] = new(min.X, max.Y, max.Z);
        vertices[index++] = new(min.X, max.Y, max.Z); vertices[index++] = new(min.X, max.Y, min.Z);
        vertices[index++] = new(min.X, min.Y, min.Z); vertices[index++] = new(min.X, max.Y, min.Z);
        vertices[index++] = new(max.X, min.Y, min.Z); vertices[index++] = new(max.X, max.Y, min.Z);
        vertices[index++] = new(max.X, min.Y, max.Z); vertices[index++] = new(max.X, max.Y, max.Z);
        vertices[index++] = new(min.X, min.Y, max.Z); vertices[index++] = new(min.X, max.Y, max.Z);

        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(_deviceContext.Map(
            _boxVertexBuffer,
            0,
            Map.WriteDiscard,
            0,
            ref mapped));
        vertices.CopyTo(new Span<Vector3>(mapped.PData, vertices.Length));
        _deviceContext.Unmap(_boxVertexBuffer, 0);

        DrawVertices(_boxVertexBuffer, 24, modelMatrix, color, projection, view);
    }

    private void DrawBoundingSphere(
        BoundingSphere sphere,
        Vector4 color,
        Matrix4x4 projection,
        Matrix4x4 view)
    {
        var modelMatrix = Matrix4x4.CreateScale(sphere.Radius) *
            Matrix4x4.CreateTranslation(sphere.Center);
        DrawVertices(
            _sphereVertexBuffer,
            SphereVertexCount,
            modelMatrix,
            color,
            projection,
            view);
    }

    private unsafe void DrawVertices(
        ComPtr<ID3D11Buffer> vertexBuffer,
        int vertexCount,
        Matrix4x4 modelMatrix,
        Vector4 color,
        Matrix4x4 projection,
        Matrix4x4 view)
    {
        var constants = new BoundsConstantBuffer
        {
            Projection = projection,
            View = view,
            Model = modelMatrix,
            Color = color
        };

        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(_deviceContext.Map(
            _constantBuffer,
            0,
            Map.WriteDiscard,
            0,
            ref mapped));
        *(BoundsConstantBuffer*)mapped.PData = constants;
        _deviceContext.Unmap(_constantBuffer, 0);

        uint stride = (uint)sizeof(Vector3);
        uint offset = 0;
        _deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in stride, in offset);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _constantBuffer);
        _deviceContext.PSSetConstantBuffers(0, 1, ref _constantBuffer);

        ComPtr<ID3D11Buffer> nullBuffer = default;
        uint nullStride = 0;
        uint nullOffset = 0;
        _deviceContext.IASetVertexBuffers(1, 1, ref nullBuffer, in nullStride, in nullOffset);
        _deviceContext.Draw((uint)vertexCount, 0);
    }

    private unsafe void UploadUnitSphere()
    {
        Span<Vector3> vertices = stackalloc Vector3[SphereVertexCount];
        var index = 0;
        for (var plane = 0; plane < 3; plane++)
        {
            for (var segment = 0; segment < SphereSegments; segment++)
            {
                var angle0 = MathF.Tau / SphereSegments * segment;
                var angle1 = MathF.Tau / SphereSegments * (segment + 1);
                vertices[index++] = GetCirclePoint(plane, angle0);
                vertices[index++] = GetCirclePoint(plane, angle1);
            }
        }

        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(_deviceContext.Map(
            _sphereVertexBuffer,
            0,
            Map.WriteDiscard,
            0,
            ref mapped));
        vertices.CopyTo(new Span<Vector3>(mapped.PData, vertices.Length));
        _deviceContext.Unmap(_sphereVertexBuffer, 0);
    }

    private static Vector3 GetCirclePoint(int plane, float angle) => plane switch
    {
        0 => new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0),
        1 => new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)),
        _ => new Vector3(0, MathF.Cos(angle), MathF.Sin(angle))
    };

    private void RestorePipelineState(ComPtr<ID3D11RasterizerState> defaultRasterizerState)
    {
        _deviceContext.RSSetState(defaultRasterizerState);
        _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        ComPtr<ID3D11DepthStencilState> nullDepthStencilState = default;
        _deviceContext.OMSetDepthStencilState(nullDepthStencilState, 0);
    }

    public void Dispose()
    {
        _depthStencilState.Dispose();
        _wireframeRasterizerState.Dispose();
        _sphereVertexBuffer.Dispose();
        _boxVertexBuffer.Dispose();
        _constantBuffer.Dispose();
    }
}
