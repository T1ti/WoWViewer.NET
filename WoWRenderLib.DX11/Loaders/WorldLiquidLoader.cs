using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Loaders;

/// <summary>
/// Uploads managed MH2O geometry on the render thread and owns its symmetric teardown.
/// </summary>
internal static class WorldLiquidLoader
{
    public static WorldLiquidResources Upload(
        ComPtr<ID3D11Device> device,
        ParsedWorldLiquid parsed,
        uint cacheOwner)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (parsed.IsEmpty)
            return default;

        WorldLiquidResources result = default;
        var acquiredTextureIds = new List<uint>();
        try
        {
            unsafe
            {
                var vertexDesc = new BufferDesc
                {
                    ByteWidth = checked((uint)(parsed.Vertices.Length * Marshal.SizeOf<WorldLiquidVertex>())),
                    Usage = Usage.Immutable,
                    BindFlags = (uint)BindFlag.VertexBuffer
                };
                fixed (WorldLiquidVertex* vertexData = parsed.Vertices)
                {
                    var initialData = new SubresourceData
                    {
                        PSysMem = vertexData
                    };
                    SilkMarshal.ThrowHResult(device.CreateBuffer(
                        in vertexDesc,
                        in initialData,
                        ref result.vertexBuffer));
                }

                var indexDesc = new BufferDesc
                {
                    ByteWidth = checked((uint)(parsed.Indices.Length * sizeof(uint))),
                    Usage = Usage.Immutable,
                    BindFlags = (uint)BindFlag.IndexBuffer
                };
                fixed (uint* indexData = parsed.Indices)
                {
                    var initialData = new SubresourceData
                    {
                        PSysMem = indexData
                    };
                    SilkMarshal.ThrowHResult(device.CreateBuffer(
                        in indexDesc,
                        in initialData,
                        ref result.indexBuffer));
                }
            }

            result.batches = parsed.Batches;
            result.materials = parsed.Materials;
            result.textureFileDataIds = parsed.TextureFileDataIds;
            result.bounds = parsed.Bounds;
            result.hasBounds = parsed.HasBounds;
            foreach (var textureId in parsed.TextureFileDataIds)
            {
                if (textureId == 0 || acquiredTextureIds.Contains(textureId))
                    continue;

                BLPCache.GetOrLoad(device, textureId, cacheOwner);
                acquiredTextureIds.Add(textureId);
            }

            return result;
        }
        catch
        {
            result.indexBuffer.Dispose();
            result.vertexBuffer.Dispose();
            foreach (var textureId in acquiredTextureIds)
                BLPCache.Release(textureId, cacheOwner);
            throw;
        }
    }

    public static void Unload(ref WorldLiquidResources liquid, uint cacheOwner)
    {
        if (liquid.textureFileDataIds != null)
        {
            foreach (var textureId in liquid.textureFileDataIds)
            {
                if (textureId != 0)
                    BLPCache.Release(textureId, cacheOwner);
            }
        }

        liquid.indexBuffer.Dispose();
        liquid.vertexBuffer.Dispose();
        liquid = default;
    }
}
