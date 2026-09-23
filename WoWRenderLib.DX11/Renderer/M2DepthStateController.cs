using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace WoWRenderLib.DX11.Renderer;

internal enum M2DepthMode
{
    Default,
    ReadOnly,
    Disabled
}

internal static class M2DepthPolicy
{
    // Wisp's 3.3.5 material translation treats bits 0x8/0x10 as disabling
    // depth test/write, despite the names on WowLib's flag enum. Modern
    // rendering keeps its existing state until verified independently.
    internal static M2DepthMode ForMaterial(bool usesLegacyDepthFlags, ushort renderFlags)
    {
        if (!usesLegacyDepthFlags)
            return M2DepthMode.Default;
        if ((renderFlags & 0x8) != 0)
            return M2DepthMode.Disabled;
        return (renderFlags & 0x10) != 0
            ? M2DepthMode.ReadOnly
            : M2DepthMode.Default;
    }
}

/// <summary>Owns the depth states used by the legacy M2 pass.</summary>
internal sealed class M2DepthStateController : IDisposable
{
    private readonly ComPtr<ID3D11DeviceContext> context;
    private ComPtr<ID3D11DepthStencilState> readOnlyState;
    private ComPtr<ID3D11DepthStencilState> disabledState;
    private M2DepthMode? currentMode;

    public M2DepthStateController(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context)
    {
        this.context = context;
        var description = new DepthStencilDesc
        {
            DepthEnable = true,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunc.LessEqual,
            StencilEnable = false
        };
        try
        {
            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(
                in description, ref readOnlyState));

            description.DepthEnable = false;
            description.DepthFunc = ComparisonFunc.Always;
            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(
                in description, ref disabledState));
        }
        catch
        {
            readOnlyState.Dispose();
            throw;
        }
    }

    public void BeginPass() => currentMode = null;

    public void Apply(bool usesLegacyDepthFlags, ushort renderFlags)
    {
        var mode = M2DepthPolicy.ForMaterial(usesLegacyDepthFlags, renderFlags);
        if (currentMode == mode)
            return;

        var state = mode switch
        {
            M2DepthMode.ReadOnly => readOnlyState,
            M2DepthMode.Disabled => disabledState,
            _ => default
        };
        context.OMSetDepthStencilState(state, 0);
        currentMode = mode;
    }

    public void EndPass()
    {
        if (currentMode is M2DepthMode.ReadOnly or M2DepthMode.Disabled)
        {
            ComPtr<ID3D11DepthStencilState> defaultState = default;
            context.OMSetDepthStencilState(defaultState, 0);
        }
        currentMode = null;
    }

    public void Dispose()
    {
        disabledState.Dispose();
        readOnlyState.Dispose();
    }
}
