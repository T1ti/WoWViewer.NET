using Silk.NET.Direct3D11;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Shared scene blend descriptions used by the renderer and GPU fixtures.</summary>
internal static class SceneBlendPolicy
{
    private static readonly (bool Enabled, Blend Src, Blend Dst, Blend SrcAlpha, Blend DstAlpha)[] Configurations =
    [
        (false, Blend.One, Blend.Zero, Blend.One, Blend.Zero),
        (false, Blend.One, Blend.Zero, Blend.One, Blend.Zero),
        (true, Blend.SrcAlpha, Blend.InvSrcAlpha, Blend.SrcAlpha, Blend.InvSrcAlpha),
        (true, Blend.SrcAlpha, Blend.One, Blend.Zero, Blend.One),
        (true, Blend.DestColor, Blend.Zero, Blend.DestAlpha, Blend.Zero),
        (true, Blend.DestColor, Blend.SrcColor, Blend.DestAlpha, Blend.SrcAlpha),
        (true, Blend.DestColor, Blend.One, Blend.DestAlpha, Blend.One),
        (true, Blend.InvSrcAlpha, Blend.One, Blend.InvSrcAlpha, Blend.One),
        (true, Blend.InvSrcAlpha, Blend.Zero, Blend.InvSrcAlpha, Blend.Zero),
        (true, Blend.SrcAlpha, Blend.Zero, Blend.SrcAlpha, Blend.Zero),
        (true, Blend.One, Blend.One, Blend.Zero, Blend.One),
        (true, Blend.BlendFactor, Blend.InvBlendFactor, Blend.BlendFactor, Blend.InvBlendFactor),
        (true, Blend.InvDestColor, Blend.One, Blend.One, Blend.Zero),
        (true, Blend.One, Blend.InvSrcAlpha, Blend.One, Blend.InvSrcAlpha)
    ];

    public static BlendDesc Description(int index)
    {
        var (enabled, src, dst, srcAlpha, dstAlpha) = Configurations[index];
        var result = new BlendDesc { AlphaToCoverageEnable = 0, IndependentBlendEnable = 0 };
        result.RenderTarget[0] = new RenderTargetBlendDesc
        {
            BlendEnable = enabled,
            SrcBlend = src, DestBlend = dst, BlendOp = BlendOp.Add,
            SrcBlendAlpha = srcAlpha, DestBlendAlpha = dstAlpha, BlendOpAlpha = BlendOp.Add,
            RenderTargetWriteMask = (byte)ColorWriteEnable.All
        };
        return result;
    }
}
