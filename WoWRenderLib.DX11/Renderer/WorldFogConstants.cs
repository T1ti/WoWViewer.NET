using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

[StructLayout(LayoutKind.Sequential)]
internal struct WrathFogCB
{
    public Vector4 Parameters; // scale, bias, rate, enabled
    public Vector4 Color;
}

internal static class WorldFogConstants
{
    // Both the staged outdoor bank and per-batch interior bank honor the
    // viewport override without altering client fog evaluation or visibility.
    public static WrathFogCB Create(Wrath335FogState fog, bool renderFog)
    {
        var width = MathF.Max(fog.EndDistance - fog.StartDistance,
            Wrath335OutdoorFogEvaluator.MinimumShaderFogWidth);
        return new WrathFogCB
        {
            Parameters = new Vector4(-1f / width,
                fog.EndDistance / width, fog.Rate, renderFog ? 1f : 0f),
            Color = new Vector4(Wrath335InteriorFog.UnpackColor(fog.Color), 1f)
        };
    }
}
