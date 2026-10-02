using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Numerics;
using WoWRenderLib.Structs;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Owns the three generated depth textures and their update storage.</summary>
internal sealed class Wrath335LiquidTextures(ComPtr<ID3D11Device> device,
    ComPtr<ID3D11DeviceContext> context) : IDisposable
{
    private readonly ComPtr<ID3D11Texture2D>[] _textures = new ComPtr<ID3D11Texture2D>[3];
    private readonly ComPtr<ID3D11ShaderResourceView>[] _views = new ComPtr<ID3D11ShaderResourceView>[3];
    private readonly uint[] _pixels = new uint[8 * 64];
    private Palette? _previous;
    private readonly record struct Palette(Vector4 OceanClose, Vector4 OceanFar, Vector4 RiverClose, Vector4 RiverFar);

    internal unsafe void Update(WorldLightingSettings lighting)
    {
        var palette = new Palette(
            new(lighting.HasLiquidColorData ? lighting.OceanCloseColor : WorldLiquidColorDefaults.OceanClose,
                lighting.HasLiquidAlphaData ? lighting.OceanShallowAlpha : 1),
            new(lighting.HasLiquidColorData ? lighting.OceanFarColor : WorldLiquidColorDefaults.OceanFar,
                lighting.HasLiquidAlphaData ? lighting.OceanDeepAlpha : 1),
            new(lighting.HasLiquidColorData ? lighting.RiverCloseColor : WorldLiquidColorDefaults.RiverClose,
                lighting.HasLiquidAlphaData ? lighting.WaterShallowAlpha : 1),
            new(lighting.HasLiquidColorData ? lighting.RiverFarColor : WorldLiquidColorDefaults.RiverFar,
                lighting.HasLiquidAlphaData ? lighting.WaterDeepAlpha : 1));
        if (_previous == palette) return;
        for (var index = 0; index < 3; index++)
        {
            Wrath335Liquid.FillGradient(_pixels, (WorldLiquidWaterType)index,
                index == 0 ? palette.OceanClose : palette.RiverClose,
                index == 0 ? palette.OceanFar : palette.RiverFar);
            fixed (uint* pixels = _pixels)
            {
                if (_textures[index].Handle == null)
                {
                    var description = new Texture2DDesc
                    {
                        Width = 8, Height = 64, MipLevels = 1, ArraySize = 1,
                        Format = Format.FormatB8G8R8A8Unorm, SampleDesc = new(1, 0),
                        Usage = Usage.Default, BindFlags = (uint)BindFlag.ShaderResource
                    };
                    var data = new SubresourceData { PSysMem = pixels, SysMemPitch = 32 };
                    SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, in data, ref _textures[index]));
                    SilkMarshal.ThrowHResult(device.CreateShaderResourceView(_textures[index], null, ref _views[index]));
                }
                else
                    context.UpdateSubresource((ID3D11Resource*)_textures[index].Handle, 0, (Box*)null, pixels, 32, 0);
            }
        }
        _previous = palette;
    }

    internal ComPtr<ID3D11ShaderResourceView> Get(WorldLiquidWaterType type) =>
        type is >= WorldLiquidWaterType.Ocean and <= WorldLiquidWaterType.Wmo ? _views[(int)type] : default;

    public void Dispose()
    {
        for (var index = 0; index < 3; index++) { _views[index].Dispose(); _textures[index].Dispose(); }
        _previous = null;
    }
}
