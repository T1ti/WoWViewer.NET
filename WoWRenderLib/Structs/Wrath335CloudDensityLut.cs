namespace WoWRenderLib.Structs;

/// <summary>
/// DNClouds::BuildDensityLUT (0x7EDB50) creates this once at map startup.
/// The changing LightFloatBand cloud density moves the lookup index later;
/// it does not rebuild the curve.
/// </summary>
public static class Wrath335CloudDensityLut
{
    public const int EntryCount = 256;
    public const float DefaultCoverage = 0.60000002f;
    public const float CurveBase = 0.95999998f;
    public const byte DefaultCoverageByte =
        (byte)(int)((1f - DefaultCoverage) * byte.MaxValue);

    public static byte[] Create(byte initialCoverageByte = DefaultCoverageByte)
    {
        var values = new byte[EntryCount];
        var step = (byte.MaxValue - initialCoverageByte) / (double)EntryCount;
        for (var index = 0; index < values.Length; index++)
        {
            var exponent = index * step;
            values[index] = (byte)(int)(
                byte.MaxValue - Math.Pow(CurveBase, exponent) * byte.MaxValue);
        }
        return values;
    }
}
