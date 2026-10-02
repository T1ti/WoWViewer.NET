namespace WoWRenderLib.DX11.Renderer;

/// <summary>12340 composed-alpha queue partition (0x821EB0..0x821F68) and
/// material state (0x81FE90). Default non-shadow DX9 alpha testing is byte-quantized.</summary>
internal readonly record struct Wrath335M2FadeMaterial(bool Draw, bool Translucent, int BlendState, float AlphaReference)
{
    internal static readonly float OpaqueThreshold = BitConverter.Int32BitsToSingle(0x3f7fff58);
    internal const float InvisibleThreshold = 0.0001f;

    public static Wrath335M2FadeMaterial Resolve(int blendMode, bool legacyDepth, ushort flags,
        float materialAlpha, bool nativeDoodad, float opacity, uint? baseBlendMode = null)
    {
        var depth = M2DepthPolicy.ForMaterial(legacyDepth, flags);
        var blend = blendMode switch { 1 => 1, 2 => 2, 3 => 10, 4 => 3, 5 => 4, 6 => 5, _ => 0 };
        if (!nativeDoodad)
            return new(true, blendMode > 1 || depth != M2DepthMode.Default, blend,
                blendMode == 1 ? 128f / 255f : -1f);

        var alpha = (double)materialAlpha * opacity;
        // 0x821F32..0x821F68 partitions using the base material, but 0x81FF08
        // selects the actual layer's blend family from that native pass row.
        var transparent = (baseBlendMode ?? (uint)blendMode) > 1 || alpha < OpaqueThreshold;
        // 0xA453B0 rows 1/2 map opaque/key/alpha to Gx Alpha; other families retain their blend.
        if (transparent && blendMode is >= 0 and <= 2) blend = 2;
        var reference = -1f;
        if (blendMode == 1)
        {
            // 0x81FF96 float product -> 0x873BBD float *255 -> truncate byte.
            var shaderReference = (float)((float)alpha * (double)(224f / 255f));
            var referenceByte = (byte)(int)(shaderReference * 255f);
            if (referenceByte != 0) reference = referenceByte / 255f;
        }
        else if (blendMode > 1) reference = 1f / 255f;
        return new(alpha >= InvisibleThreshold, transparent, blend, reference);
    }
}
