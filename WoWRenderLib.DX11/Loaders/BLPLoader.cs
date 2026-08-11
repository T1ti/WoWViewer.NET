using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WoWFormatLib.Structs.TEX;

namespace WoWRenderLib.DX11.Loaders
{
    public static class BLPLoader
    {
        private static readonly byte[] PlaceholderPixels = new byte[] { 255, 0, 255, 255 };
        private static readonly byte[] TransparentPixels = new byte[] { 0, 0, 0, 0 };

        public static unsafe ComPtr<ID3D11ShaderResourceView> CreatePlaceholderTexture(ComPtr<ID3D11Device> device)
            => CreateSolidTexture(device, PlaceholderPixels);

        /// <summary>
        /// Creates the neutral texture used for optional WMO material slots.
        /// WMO shaders treat absent auxiliary textures as zero contribution; an
        /// opaque diagnostic (magenta) texture would incorrectly affect alpha
        /// weighting and layer selection.
        /// </summary>
        public static unsafe ComPtr<ID3D11ShaderResourceView> CreateTransparentTexture(ComPtr<ID3D11Device> device)
            => CreateSolidTexture(device, TransparentPixels);

        private static unsafe ComPtr<ID3D11ShaderResourceView> CreateSolidTexture(ComPtr<ID3D11Device> device, byte[] pixels)
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
                BindFlags = (uint)(BindFlag.ShaderResource),
                CPUAccessFlags = 0,
                MiscFlags = 0
            };

            SubresourceData initData = default;
            fixed (byte* p = pixels)
            {
                initData.PSysMem = p;
                initData.SysMemPitch = (uint)(4 * texDesc.Width);

                ComPtr<ID3D11Texture2D> tex = default;
                SilkMarshal.ThrowHResult(device.CreateTexture2D(in texDesc, ref initData, ref tex));

                ComPtr<ID3D11ShaderResourceView> srv = default;
                var srvDesc = new ShaderResourceViewDesc
                {
                    Format = texDesc.Format,
                    ViewDimension = D3DSrvDimension.D3D101SrvDimensionTexture2D,
                    Texture2D = new Tex2DSrv { MipLevels = 1, MostDetailedMip = 0 }
                };

                SilkMarshal.ThrowHResult(device.CreateShaderResourceView(tex, in srvDesc, ref srv));

                tex.Dispose();
                return srv;
            }
        }

        public unsafe static ComPtr<ID3D11ShaderResourceView> GenerateAlphaTexture(ComPtr<ID3D11Device> device, byte[] values)
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
                initData.SysMemPitch = (uint)(4 * texDesc.Width);

                ComPtr<ID3D11Texture2D> tex = default;
                SilkMarshal.ThrowHResult(device.CreateTexture2D(in texDesc, ref initData, ref tex));

                ComPtr<ID3D11ShaderResourceView> srv = default;
                var srvDesc = new ShaderResourceViewDesc
                {
                    Format = texDesc.Format,
                    ViewDimension = D3DSrvDimension.D3D101SrvDimensionTexture2D,
                    Texture2D = new Tex2DSrv { MipLevels = 1, MostDetailedMip = 0 }
                };

                SilkMarshal.ThrowHResult(device.CreateShaderResourceView(tex, in srvDesc, ref srv));
                tex.Dispose();
                return srv;
            }
        }

        public unsafe static ComPtr<ID3D11ShaderResourceView> CreateTextureFromBlob(ComPtr<ID3D11Device> device, BlobTexture blobTex, byte[] bytes)
        {
            var dxgiFormat = blobTex.dxtFormat switch
            {
                0 => Format.FormatBC1Unorm,
                1 => Format.FormatBC2Unorm,
                2 => Format.FormatBC3Unorm,
                _ => throw new NotImplementedException(),
            };

            var sizePerBlock = blobTex.dxtFormat switch { 0 => 8, 1 => 16, 2 => 16, _ => throw new NotImplementedException() };
            var expectedBytes = (blobTex.sizeX / 4) * (blobTex.sizeY / 4) * sizePerBlock;
            if (bytes.Length > expectedBytes)
                bytes = bytes[..expectedBytes];

            var texDesc = new Texture2DDesc
            {
                Width = blobTex.sizeX,
                Height = blobTex.sizeY,
                MipLevels = 1,
                ArraySize = 1,
                Format = dxgiFormat,
                SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
                Usage = Usage.Default,
                BindFlags = (uint)(BindFlag.ShaderResource),
                CPUAccessFlags = 0,
                MiscFlags = 0
            };

            fixed (byte* p = bytes)
            {
                SubresourceData initData = default;
                initData.PSysMem = p;
                initData.SysMemPitch = (uint)((blobTex.sizeX / 4) * sizePerBlock);

                ComPtr<ID3D11Texture2D> tex = default;
                SilkMarshal.ThrowHResult(device.CreateTexture2D(in texDesc, ref initData, ref tex));

                ComPtr<ID3D11ShaderResourceView> srv = default;
                var srvDesc = new ShaderResourceViewDesc
                {
                    Format = texDesc.Format,
                    ViewDimension = D3DSrvDimension.D3D101SrvDimensionTexture2D,
                    Texture2D = new Tex2DSrv { MipLevels = 1, MostDetailedMip = 0 }
                };

                SilkMarshal.ThrowHResult(device.CreateShaderResourceView(tex, in srvDesc, ref srv));
                tex.Dispose();
                return srv;
            }
        }
    }
}
