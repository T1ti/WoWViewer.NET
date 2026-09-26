using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Structs;

/// <summary>
/// Render-thread-owned GPU resources for the MH2O payload of one ADT.
/// </summary>
public struct WorldLiquidResources
{
    public ComPtr<ID3D11Buffer> vertexBuffer;
    public ComPtr<ID3D11Buffer> indexBuffer;
    public ParsedWorldLiquidBatch[] batches;
    internal BoundingSphere[] batchSpheres;
    public WorldLiquidMaterialDescriptor[] materials;
    public uint[] textureFileDataIds;
    public BoundingBox bounds;
    public bool hasBounds;

    public bool HasGeometry
    {
        get
        {
            unsafe
            {
                return vertexBuffer.Handle != null &&
                    indexBuffer.Handle != null &&
                    batches is { Length: > 0 };
            }
        }
    }
}
