using System.Numerics;
using System.Runtime.CompilerServices;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal static class WmoMaterialPolicy
{
    private const uint SidnFlag = 0x10;

    // Wisp's load-time fix-up: legacy two-texture shaders without their
    // second stage use the one-texture Opaque program.
    internal static int ResolveShader(bool legacyClient, int shader, bool hasTexture2) =>
        legacyClient && !hasTexture2 && shader is 3 or 5 or 6 ? 4 : shader;

    // TODO(WMO): Wisp also promotes shader 0 + opaque blend to shader 4 when
    // the base BLP has alpha. The streaming BLP metadata currently does not
    // expose alpha presence to WMO material setup.

    // Wisp's decoded WotLK MapObj lighting banks. Modern clients retain the
    // established DX11 lighting path because these selectors were decoded for
    // the older WMO renderer.
    internal static int ResolveLightingMode(bool legacyClient, ushort rootFlags,
        uint groupFlags, bool hasMocv, byte category, uint materialFlags)
    {
        if (!legacyClient)
            return (materialFlags & 0x1) != 0 ? 0 : -1;

        var unified = (rootFlags & 0x2) != 0;
        var unlit = (materialFlags & 0x1) != 0;
        var window = (materialFlags & 0x20) != 0;
        if (category == 0) // Transition first pass.
            return unified && unlit ? 0 : window ? 2 : 1;

        if (unified)
            return (groupFlags & 0x48) != 0 ? (unlit ? 0 : 1) : (window ? 2 : 3);

        if (!hasMocv)
            return unlit ? 0 : 1;
        return category == 1 ? 0 : window ? 2 : 1;
    }

    internal static (Vector3 Ambient, Vector3 Diffuse) WindowLighting(Vector3 ambient, Vector3 diffuse)
    {
        static int ToByte(float value) => (int)MathF.Floor(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
        static (float, float) Channel(float a, float d)
        {
            var midpoint = (ToByte(a) + ToByte(d)) >> 1;
            return (Math.Min(midpoint + 16, 255) / 255f, midpoint / 255f);
        }

        var x = Channel(ambient.X, diffuse.X);
        var y = Channel(ambient.Y, diffuse.Y);
        var z = Channel(ambient.Z, diffuse.Z);
        return (new Vector3(x.Item1, y.Item1, z.Item1),
            new Vector3(x.Item2, y.Item2, z.Item2));
    }

    internal static Vector3 PackedRgb(uint color) => new(
        ((color >> 16) & 0xff) / 255f,
        ((color >> 8) & 0xff) / 255f,
        (color & 0xff) / 255f);

    // World lighting and the WMO night-glow curve share the 0..2880 clock.
    internal static float SidnPulse(long worldTime) =>
        WorldLightingCatalog.CalculateWmoSidnPulse(worldTime);

    internal static Vector3 SidnColor(in PreppedWMOMaterial material, float pulse)
    {
        if ((material.Flags & SidnFlag) == 0 || pulse <= 0f)
            return Vector3.Zero;

        var packed = material.Color1;
        return new Vector3(
            ((packed >> 16) & 0xff) / 255f,
            ((packed >> 8) & 0xff) / 255f,
            (packed & 0xff) / 255f) * pulse;
    }

    // Wisp's c29 is byte-scaled and then halved before the vertex combine.
    internal static Vector3 WispSidnColor(in PreppedWMOMaterial material, float pulse)
    {
        if ((material.Flags & SidnFlag) == 0 || pulse <= 0f)
            return Vector3.Zero;

        static float Channel(uint packed, int shift, float scale)
        {
            var value = (int)((packed >> shift) & 0xff);
            return ((int)(value * Math.Clamp(scale, 0f, 1f) + 0.5f) >> 1) / 255f;
        }
        return new Vector3(
            Channel(material.Color1, 16, pulse),
            Channel(material.Color1, 8, pulse),
            Channel(material.Color1, 0, pulse));
    }

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

/// <summary>Retains frame SIDN colors for each loaded material array until world time changes.</summary>
internal sealed class WmoSidnColorCache
{
    private readonly ConditionalWeakTable<PreppedWMOMaterial[], CacheEntry> _entries = new();
    private long _lastTime;
    private bool _hasTime;
    private float _pulse;

    internal ReadOnlySpan<Vector3> GetColors(PreppedWMOMaterial[] materials, long worldTime,
        bool wispLegacy = false)
    {
        if (!_hasTime || worldTime != _lastTime)
        {
            _pulse = WmoMaterialPolicy.SidnPulse(worldTime);
            _lastTime = worldTime;
            _hasTime = true;
        }

        var entry = _entries.GetValue(materials, static items => new CacheEntry(items.Length));
        if (!entry.HasTime || entry.Time != worldTime)
        {
            for (var index = 0; index < materials.Length; index++)
            {
                entry.Colors[index] = WmoMaterialPolicy.SidnColor(materials[index], _pulse);
                entry.WispColors[index] = WmoMaterialPolicy.WispSidnColor(materials[index], _pulse);
            }
            entry.Time = worldTime;
            entry.HasTime = true;
        }

        return wispLegacy ? entry.WispColors : entry.Colors;
    }

    private sealed class CacheEntry(int materialCount)
    {
        internal long Time;
        internal bool HasTime;
        internal readonly Vector3[] Colors = new Vector3[materialCount];
        internal readonly Vector3[] WispColors = new Vector3[materialCount];
    }
}
