using System.Numerics;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Static MODD lighting in build 12340; entity floor/MOCV queries are separate.</summary>
internal readonly record struct Wrath335WmoDoodadLighting(
    bool Referenced, bool Interior, Vector3 Ambient, Vector3 Diffuse)
{
    // 0xAEEDF0 stores the incoming direction. Diffuse_T1 dots against -c12.
    internal static readonly Vector3 DirectionToLight = new(0.30822f, 0.30822f, 0.9f);

    internal static Wrath335WmoDoodadLighting[] Build(
        ReadOnlySpan<WMODoodad> doodads, ReadOnlySpan<WorldModelGroupBatches> groups)
    {
        var result = new Wrath335WmoDoodadLighting[doodads.Length];
        foreach (var group in groups)
        {
            if (group.doodadReferences == null)
                continue;
            foreach (var index in group.doodadReferences)
            {
                if (index >= result.Length)
                    continue;
                var previous = result[index];
                // 0x7BDFBD and 0x7BF859: MOGI, not MOGP/MOCV or raw MODD flags.
                // Once any group sets runtime bit 4, subsequent indoor links retain it.
                var interior = (group.mogiFlags & 0x48) == 0 &&
                    (!previous.Referenced || previous.Interior);
                var (ambient, diffuse) = AdjustLightmap(doodads[index].color);
                // 0x835335–0x8353B7: collapse the single directional contribution.
                var magnitude = DirectionToLight.Length();
                ambient += diffuse * (0.25f * (1f - magnitude));
                diffuse = Vector3.Min(Vector3.One, diffuse * (1.25f * magnitude - 0.25f));
                result[index] = new(true, interior, ambient, diffuse);
            }
        }
        return result;
    }

    internal static bool IsMaterialLit(ushort flags, int blendMode) =>
        (flags & 1) == 0 && blendMode is >= 0 and <= 4; // 0x81FB22, table 0xA45374.

    internal static (Vector3 Ambient, Vector3 Diffuse) AdjustLightmap(Vector4 color)
    {
        // 0x7BF092 passes diffuse floor 112 and ambient ceiling 96 to 0x7C1AD0.
        var rgb = new Vector3(Byte(color.X), Byte(color.Y), Byte(color.Z));
        var maximum = Math.Max(1f, Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z)));
        var diffuse = rgb;
        if (maximum < 112f)
        {
            // The native floor path round-trips through HSV, then packs with FISTP.
            diffuse = RaiseValue(rgb / 255f, 112.0 / maximum);
            diffuse = new Vector3(RoundByte(diffuse.X * 255f),
                RoundByte(diffuse.Y * 255f), RoundByte(diffuse.Z * 255f));
        }
        var ambient = rgb;
        if (maximum > 96f)
        {
            var multiplier = (int)MathF.Round(96f * 255f / maximum - 0.5f, MidpointRounding.ToEven);
            ambient = new Vector3(((int)rgb.X * multiplier + 255) >> 8,
                ((int)rgb.Y * multiplier + 255) >> 8,
                ((int)rgb.Z * multiplier + 255) >> 8);
        }
        return (ambient / 255f, diffuse / 255f);
    }

    private static int Byte(float value) => RoundByte(value * 255f);
    private static int RoundByte(float value) => Math.Clamp(
        (int)MathF.Round(value, MidpointRounding.ToEven), 0, 255);

    private static Vector3 RaiseValue(Vector3 rgb, double scale)
    {
        // 0x984F60 / 0x985030. Retain the float stores between the HSV stages.
        var maximum = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
        var minimum = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
        var delta = maximum - minimum;
        var saturation = maximum == 0 ? 0 : (float)((double)delta / maximum);
        var value = (float)((double)maximum * scale);
        if (saturation == 0)
            return new(value);
        var hue = maximum == rgb.X ? (float)((double)(rgb.Y - rgb.Z) / delta) :
            maximum == rgb.Y ? (float)((double)(rgb.Z - rgb.X) / delta + 2) :
            (float)((double)(rgb.X - rgb.Y) / delta + 4);
        hue *= 60f;
        if (hue < 0)
            hue += 360f;
        var sectorPosition = (hue >= 360f ? hue - 360f : hue) * 0.016666668f;
        var sector = Math.Min(5, (int)MathF.Round(sectorPosition - 0.5f, MidpointRounding.ToEven));
        var fraction = (double)sectorPosition - sector;
        var s = Math.Min(1f, saturation);
        var p = (float)((1.0 - s) * value);
        var q = (float)((1.0 - s * fraction) * value);
        var t = (float)((1.0 - s * (1.0 - fraction)) * value);
        return sector switch
        {
            0 => new(value, t, p), 1 => new(q, value, p),
            2 => new(p, value, t), 3 => new(p, q, value),
            4 => new(t, p, value), _ => new(value, p, q)
        };
    }
}
