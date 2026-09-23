namespace WoWRenderLib.DX11.Renderer;

internal static class WmoMaterialPolicy
{
    // WotLK's WMO blend modes use a different alpha-reference table from M2.
    internal static float AlphaReference(uint blendMode, bool legacyClient)
    {
        if (!legacyClient)
            return blendMode == 1 ? 128f / 255f : -1f;

        return blendMode switch
        {
            1 => 224f / 255f,
            >= 2 and <= 6 => 1f / 255f,
            _ => 0f
        };
    }

    // WMO material flags 0x40 and 0x80 clamp the U and V axes respectively.
    // The shared sampler array is indexed by (wrapU << 1) | wrapV.
    internal static int SamplerIndex(uint materialFlags) =>
        ((materialFlags & 0x40) == 0 ? 2 : 0) |
        ((materialFlags & 0x80) == 0 ? 1 : 0);
}
