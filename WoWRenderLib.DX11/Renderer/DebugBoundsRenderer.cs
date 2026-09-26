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
/// Draws all scene bounds with shared line geometry and one instance stream per frame.
/// The scene manager supplies ordering and a stable object snapshot/lock boundary.
/// </summary>
internal sealed class DebugBoundsRenderer(
    ComPtr<ID3D11Device> device,
    ComPtr<ID3D11DeviceContext> deviceContext) : IDisposable
{
    private const int BoxVertexCount = 24;
    private const int SphereSegments = 32;
    private const int SphereVertexCount = 3 * SphereSegments * 2;

    private readonly ComPtr<ID3D11Device> _device = device;
    private readonly ComPtr<ID3D11DeviceContext> _deviceContext = deviceContext;
    private readonly List<BoundsInstance> _boxInstances = [];
    private readonly List<BoundsInstance> _sphereInstances = [];
    private CompiledShader _shader;
    private ComPtr<ID3D11Buffer> _constantBuffer;
    private ComPtr<ID3D11Buffer> _boxVertexBuffer;
    private ComPtr<ID3D11Buffer> _sphereVertexBuffer;
    private ComPtr<ID3D11Buffer> _instanceBuffer;
    private ComPtr<ID3D11RasterizerState> _wireframeRasterizerState;
    private ComPtr<ID3D11DepthStencilState> _depthStencilState;
    private ComPtr<ID3D11ClassInstance> _nullClassInstance;
    private int _instanceCapacity;
    private bool _initialized;

    [StructLayout(LayoutKind.Sequential)]
    private struct BoundsConstantBuffer
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BoundsInstance
    {
        public Matrix4x4 Model;
        public Vector4 Color;

        public BoundsInstance(Matrix4x4 model, Vector4 color)
        {
            Model = model;
            Color = color;
        }
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
            in constantBufferDesc, null, ref _constantBuffer));

        CreateUnitBoxBuffer();
        CreateUnitSphereBuffer();

        var wireframeDesc = new RasterizerDesc
        {
            FillMode = FillMode.Wireframe,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthClipEnable = true
        };
        SilkMarshal.ThrowHResult(_device.CreateRasterizerState(
            in wireframeDesc, ref _wireframeRasterizerState));

        // Bounds are drawn after opaque meshes and before liquids. Writing depth
        // at line pixels lets nearer lines stay in front of translucent water,
        // while water can blend over lines that are behind its surface.
        var depthDesc = new DepthStencilDesc
        {
            DepthEnable = true,
            DepthWriteMask = DepthWriteMask.All,
            DepthFunc = ComparisonFunc.LessEqual,
            StencilEnable = false
        };
        SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(
            in depthDesc, ref _depthStencilState));

        _initialized = true;
    }

    public void RefreshShader(CompiledShader shader) => _shader = shader;

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

        _boxInstances.Clear();
        _sphereInstances.Clear();
        foreach (var sceneObject in sceneObjects)
        {
            if (sceneObject is ADTContainer)
                continue;

            var renderSelection = showSelection && sceneObject.IsSelected;
            if (!showBoundingBoxes && !showBoundingSpheres && !renderSelection)
                continue;

            if (showBoundingBoxes || renderSelection)
            {
                var worldBounds = sceneObject.GetBoundingBox();
                if (worldBounds.HasValue && IsFinite(worldBounds.Value) &&
                    TryGetLocalBounds(sceneObject, out var localBounds, out var objectMatrix) &&
                    IsFinite(localBounds))
                {
                    // Unit box -> local bounds -> object transform. Preserve the
                    // object's rotation, rather than drawing its world AABB.
                    var size = localBounds.Max - localBounds.Min;
                    var center = (localBounds.Min + localBounds.Max) * 0.5f;
                    var model = Matrix4x4.CreateScale(size) *
                        Matrix4x4.CreateTranslation(center) * objectMatrix;
                    _boxInstances.Add(new BoundsInstance(model,
                        renderSelection ? Vector4.One : new Vector4(1, 1, 0, 1)));
                }
            }

            if (showBoundingSpheres && !renderSelection)
            {
                var sphere = sceneObject.GetBoundingSphere();
                if (sphere.HasValue && IsFinite(sphere.Value))
                {
                    var model = Matrix4x4.CreateScale(sphere.Value.Radius) *
                        Matrix4x4.CreateTranslation(sphere.Value.Center);
                    _sphereInstances.Add(new BoundsInstance(model,
                        new Vector4(0, 0.5f, 1, 1)));
                }
            }
        }

        if (_boxInstances.Count + _sphereInstances.Count == 0)
            return 0;

        UploadInstances();
        UploadCamera(projection, view);

        _deviceContext.RSSetState(_wireframeRasterizerState);
        _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
        _deviceContext.IASetInputLayout(_shader.InputLayout);
        _deviceContext.VSSetShader(_shader.VertexShader, ref _nullClassInstance, 0);
        _deviceContext.PSSetShader(_shader.PixelShader, ref _nullClassInstance, 0);
        _deviceContext.OMSetDepthStencilState(_depthStencilState, 0);
        _deviceContext.VSSetConstantBuffers(0, 1, ref _constantBuffer);

        uint instanceStride = (uint)Marshal.SizeOf<BoundsInstance>();
        uint offset = 0;
        _deviceContext.IASetVertexBuffers(1, 1, ref _instanceBuffer,
            in instanceStride, in offset);

        uint drawCalls = 0;
        if (_boxInstances.Count > 0)
        {
            DrawInstances(_boxVertexBuffer, BoxVertexCount,
                (uint)_boxInstances.Count, 0);
            drawCalls++;
        }
        if (_sphereInstances.Count > 0)
        {
            DrawInstances(_sphereVertexBuffer, SphereVertexCount,
                (uint)_sphereInstances.Count, (uint)_boxInstances.Count);
            drawCalls++;
        }

        RestorePipelineState(defaultRasterizerState);
        return drawCalls;
    }

    private static bool IsFinite(BoundingBox box) =>
        float.IsFinite(box.Min.X) && float.IsFinite(box.Min.Y) &&
        float.IsFinite(box.Min.Z) && float.IsFinite(box.Max.X) &&
        float.IsFinite(box.Max.Y) && float.IsFinite(box.Max.Z);

    private static bool IsFinite(BoundingSphere sphere) =>
        float.IsFinite(sphere.Center.X) && float.IsFinite(sphere.Center.Y) &&
        float.IsFinite(sphere.Center.Z) && float.IsFinite(sphere.Radius) &&
        sphere.Radius >= 0;

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

    private unsafe void UploadCamera(Matrix4x4 projection, Matrix4x4 view)
    {
        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(_deviceContext.Map(
            _constantBuffer, 0, Map.WriteDiscard, 0, ref mapped));
        *(BoundsConstantBuffer*)mapped.PData = new BoundsConstantBuffer
        {
            Projection = projection,
            View = view
        };
        _deviceContext.Unmap(_constantBuffer, 0);
    }

    private unsafe void UploadInstances()
    {
        var count = checked(_boxInstances.Count + _sphereInstances.Count);
        if (count > _instanceCapacity)
        {
            var capacity = 1;
            while (capacity < count)
                capacity = checked(capacity * 2);

            _instanceBuffer.Dispose();
            var description = new BufferDesc
            {
                ByteWidth = checked((uint)(capacity * sizeof(BoundsInstance))),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write
            };
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in description, null, ref _instanceBuffer));
            _instanceCapacity = capacity;
        }

        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(_deviceContext.Map(
            _instanceBuffer, 0, Map.WriteDiscard, 0, ref mapped));
        var destination = new Span<BoundsInstance>(mapped.PData, count);
        CollectionsMarshal.AsSpan(_boxInstances).CopyTo(destination);
        CollectionsMarshal.AsSpan(_sphereInstances).CopyTo(destination[_boxInstances.Count..]);
        _deviceContext.Unmap(_instanceBuffer, 0);
    }

    private void DrawInstances(
        ComPtr<ID3D11Buffer> vertexBuffer,
        int vertexCount,
        uint instanceCount,
        uint firstInstance)
    {
        uint vertexStride = (uint)Marshal.SizeOf<Vector3>();
        uint offset = 0;
        _deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer,
            in vertexStride, in offset);
        _deviceContext.DrawInstanced((uint)vertexCount, instanceCount, 0, firstInstance);
    }

    private unsafe void CreateUnitBoxBuffer()
    {
        Span<Vector3> vertices = stackalloc Vector3[BoxVertexCount];
        var min = new Vector3(-0.5f);
        var max = new Vector3(0.5f);
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

        var description = new BufferDesc
        {
            ByteWidth = (uint)(BoxVertexCount * sizeof(Vector3)),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.VertexBuffer
        };
        fixed (Vector3* data = vertices)
        {
            var initialData = new SubresourceData { PSysMem = data };
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in description, in initialData, ref _boxVertexBuffer));
        }
    }

    private unsafe void CreateUnitSphereBuffer()
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

        var description = new BufferDesc
        {
            ByteWidth = (uint)(SphereVertexCount * sizeof(Vector3)),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.VertexBuffer
        };
        fixed (Vector3* data = vertices)
        {
            var initialData = new SubresourceData { PSysMem = data };
            SilkMarshal.ThrowHResult(_device.CreateBuffer(
                in description, in initialData, ref _sphereVertexBuffer));
        }
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
        ComPtr<ID3D11Buffer> nullBuffer = default;
        uint zero = 0;
        _deviceContext.IASetVertexBuffers(1, 1, ref nullBuffer, in zero, in zero);
    }

    public void Dispose()
    {
        _depthStencilState.Dispose();
        _wireframeRasterizerState.Dispose();
        _instanceBuffer.Dispose();
        _sphereVertexBuffer.Dispose();
        _boxVertexBuffer.Dispose();
        _constantBuffer.Dispose();
    }
}
