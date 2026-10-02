using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>12340 material selection, unsigned clocks and 8x64 BGRA gradients.</summary>
internal static class Wrath335Liquid
{
    internal const int GradientWidth = 8;
    internal const int GradientHeight = 64;

    // 0 remains the other-client fallback. Material 3 has a separate six-texture program.
    internal static int Program(int materialId, bool specular) => materialId switch
    {
        1 => specular ? 2 : 1,
        2 => 3,
        _ => 0
    };

    internal static int Frame(long milliseconds, uint period, int count)
    {
        if (count <= 1) return 0;
        if (period == 0) period = 1;
        var phase = (float)((double)(unchecked((uint)milliseconds) % period) / period * count);
        return Math.Min((int)phase, count - 1);
    }

    internal static float Scroll(long milliseconds, float speed)
    {
        if (speed == 0 || !float.IsFinite(speed)) return 0;
        var period = unchecked((uint)(long)(1000.0 / speed));
        return period == 0 ? 0 : (float)((double)(unchecked((uint)milliseconds) % period) / period);
    }

    // DayNight +0xF8 -> sun CM2Light +0x48 -> CM2Lighting +0x6C -> VS c36.
    // The fixed indoor light never writes specular; its constructor leaves zero.
    internal static Vector3 Specular(Vector3 sunColor, bool interior) => interior ? Vector3.Zero :
        new Vector3(Byte(sunColor.X), Byte(sunColor.Y), Byte(sunColor.Z)) / 255f;

    internal static void FillGradient(Span<uint> pixels, WorldLiquidWaterType type,
        Vector4 shallow, Vector4 deep)
    {
        if (pixels.Length != GradientWidth * GradientHeight)
            throw new ArgumentException("An 8x64 gradient is required.", nameof(pixels));
        var start = Pack(shallow);
        var end = Pack(deep);
        for (var row = 0; row < GradientHeight; row++)
        {
            var alpha = Ramp(start >> 24, end >> 24, row);
            var red = Ramp((start >> 16) & 255, (end >> 16) & 255, row);
            var green = Ramp((start >> 8) & 255, (end >> 8) & 255, row);
            var blue = Ramp(start & 255, end & 255, row);
            if (type == WorldLiquidWaterType.Wmo)
            {
                red = (end >> 16) & 255; green = (end >> 8) & 255; blue = end & 255;
            }
            else if (type == WorldLiquidWaterType.Ocean && row == GradientHeight - 1)
            {
                // HSV value *= .9 preserves hue/saturation, hence scales RGB.
                // CImVector::operator=(C3Vector) also overwrites alpha with 255.
                red = (uint)MathF.Round(red * .89999998f, MidpointRounding.ToEven);
                green = (uint)MathF.Round(green * .89999998f, MidpointRounding.ToEven);
                blue = (uint)MathF.Round(blue * .89999998f, MidpointRounding.ToEven);
                alpha = 255;
            }
            for (var column = 0; column < GradientWidth; column++)
                pixels[row * GradientWidth + column] = type == WorldLiquidWaterType.Wmo && column >= 4
                    ? (alpha << 24) | 0x00ffffff
                    : (alpha << 24) | (red << 16) | (green << 8) | blue;
        }
    }

    private static uint Ramp(uint start, uint end, int row) =>
        (uint)(((int)start * 256 + ((int)end - (int)start) * 4 * row) >> 8);

    private static uint Pack(Vector4 value) =>
        (Byte(value.W) << 24) | (Byte(value.X) << 16) | (Byte(value.Y) << 8) | Byte(value.Z);

    private static uint Byte(float value) => (uint)MathF.Round(
        Math.Clamp(float.IsFinite(value) ? value : 0, 0, 1) * 255, MidpointRounding.ToEven);
}
