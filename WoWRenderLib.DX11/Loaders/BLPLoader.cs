using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace WoWRenderLib.DX11.Loaders;

public static class BLPLoader
{
    private static readonly byte[] PlaceholderPixels = [255, 0, 255, 255];
    private static readonly byte[] WhitePixels = [255, 255, 255, 255];
    private static readonly byte[] TransparentPixels = [0, 0, 0, 0];

    public static ComPtr<ID3D11ShaderResourceView> CreatePlaceholderTexture(ComPtr<ID3D11Device> device) =>
        CreateSolidTexture(device, PlaceholderPixels);

    /// <summary>
    /// Creates the neutral diffuse texture used when terrain has no assigned texture.
    /// This is intentionally distinct from the magenta missing-asset placeholder.
    /// </summary>
    public static ComPtr<ID3D11ShaderResourceView> CreateWhiteTexture(ComPtr<ID3D11Device> device) =>
        CreateSolidTexture(device, WhitePixels);

    /// <summary>
    /// Creates the neutral texture used for optional WMO material slots.
    /// </summary>
    public static ComPtr<ID3D11ShaderResourceView> CreateTransparentTexture(ComPtr<ID3D11Device> device) =>
        CreateSolidTexture(device, TransparentPixels);

    private static unsafe ComPtr<ID3D11ShaderResourceView> CreateSolidTexture(
        ComPtr<ID3D11Device> device,
        byte[] pixels)
    {
        var texDesc = new Texture2DDesc
        {
            Width = 1,
            Height = 1,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
            CPUAccessFlags = 0,
            MiscFlags = 0
        };

        fixed (byte* p = pixels)
        {
            SubresourceData initData = default;
            initData.PSysMem = p;
            initData.SysMemPitch = 4;

            ComPtr<ID3D11Texture2D> texture = default;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in texDesc, ref initData, ref texture));

            ComPtr<ID3D11ShaderResourceView> srv = default;
            var srvDesc = new ShaderResourceViewDesc
            {
                Format = texDesc.Format,
                ViewDimension = D3DSrvDimension.D3D101SrvDimensionTexture2D,
                Texture2D = new Tex2DSrv { MipLevels = 1, MostDetailedMip = 0 }
            };

            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(texture, in srvDesc, ref srv));
            texture.Dispose();
            return srv;
        }
    }

    public static unsafe ComPtr<ID3D11ShaderResourceView> GenerateAlphaTexture(
        ComPtr<ID3D11Device> device,
        byte[] values)
    {
        var texDesc = new Texture2DDesc
        {
            Width = 64,
            Height = 64,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
            CPUAccessFlags = 0,
            MiscFlags = 0
        };

        fixed (byte* p = values)
        {
            SubresourceData initData = default;
            initData.PSysMem = p;
            initData.SysMemPitch = 4 * texDesc.Width;

            ComPtr<ID3D11Texture2D> texture = default;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in texDesc, ref initData, ref texture));

            ComPtr<ID3D11ShaderResourceView> srv = default;
            var srvDesc = new ShaderResourceViewDesc
            {
                Format = texDesc.Format,
                ViewDimension = D3DSrvDimension.D3D101SrvDimensionTexture2D,
                Texture2D = new Tex2DSrv { MipLevels = 1, MostDetailedMip = 0 }
            };

            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(texture, in srvDesc, ref srv));
            texture.Dispose();
            return srv;
        }
    }
}
