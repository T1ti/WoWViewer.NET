namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct Wrath335M2ShaderEnvironment(uint LightingFlags,
    uint PointLightCount, uint ShadowMode, bool AlphaTestCapability, uint PcfFiltering);

internal readonly record struct Wrath335M2ShaderSelection(uint VertexIndex, uint PixelIndex,
    uint ShadowMode);

/// <summary>
/// Borrowed resolved shader identities, matching native effect tables +0x2C and
/// +0x194. Entries are resource identities, not permutation numbers or asset IDs.
/// A present effect can contain zero shader entries (native fixed-function path).
/// </summary>
internal readonly record struct Wrath335M2EffectSortTables(ReadOnlyMemory<uint> Vertex,
    ReadOnlyMemory<uint> Pixel)
{
    internal bool TryResolve(Wrath335M2ShaderSelection selection, out Wrath335M2ShaderSortKey key)
    {
        key = default;
        if (selection.VertexIndex >= Vertex.Length || selection.PixelIndex >= Pixel.Length) return false;
        key = new(Vertex.Span[(int)selection.VertexIndex], Pixel.Span[(int)selection.PixelIndex]);
        return true;
    }
}

/// <summary>Build 12340 mesh variant selector at 0x81F1D0..0x81F324.
/// These indices select effect-table entries; the comparator reads those entries.</summary>
internal static class Wrath335M2ShaderSelectors
{
    internal static Wrath335M2ShaderSelection Select(ushort materialBlend, ushort materialFlags,
        ushort boneInfluences, float composedAlpha, bool projected,
        in Wrath335M2ShaderEnvironment environment)
    {
        var unlit = (materialFlags & 1) != 0;
        // 0xA45374: modes 0..4 lit, 5/6 unlit. Native malformed indices are unclaimed.
        var lit = !unlit && materialBlend <= 4 ? 1u : 0u;
        // Blend-family unlit is distinct from material UNLIT: only the latter
        // suppresses lighting +0xA4's point-light count.
        var lights = unlit ? 0u : environment.PointLightCount;
        uint shadow = 0;
        if (!unlit && (environment.LightingFlags & 0x10) != 0 && (materialFlags & 0x100) == 0)
        {
            shadow = environment.ShadowMode;
            if (shadow != 0)
            {
                if ((environment.LightingFlags & 8) != 0) shadow = 1;
                if (projected) shadow = 0;
            }
        }
        var alphaClass = materialBlend == 0 ? 0u : materialBlend == 1
            ? AlphaClass(composedAlpha) : 1u;
        var shaderAlphaTest = alphaClass != 0 && (!environment.AlphaTestCapability || shadow != 0);
        var vertex = unchecked(lit + 2 * (lights + 5 * (Math.Min((uint)boneInfluences, 2) +
            3 * Math.Min(shadow, 2))));
        var pixel = unchecked(shadow + 4 * (environment.PcfFiltering + (shaderAlphaTest ? 2u : 0u)));
        return new(vertex, pixel, shadow);
    }

    // FISTP qword under truncation, followed by the low dword (0x81F256..0x81F27A).
    // Invalid conversions produce integer-indefinite 0x8000000000000000 -> low dword 0.
    private static uint AlphaClass(float alpha)
    {
        var product = alpha * 224.0;
        return product >= long.MinValue && product < 9223372036854775808.0
            ? unchecked((uint)(long)product) : 0;
    }
}
