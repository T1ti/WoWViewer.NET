namespace WoWRenderLib.Structs;

/// <summary>
/// Build 12340 DNClouds::InitNoiseTables / GenerateTexture value noise.
/// Coordinates are unsigned 8.8 fixed point; each axis wraps through the
/// client's 256-entry permutation.
/// </summary>
public sealed class Wrath335CloudNoise
{
    public const int GradientCount = 256;
    public const int OctaveCount = 4;
    public const int FractionBits = 8;
    public const int DefaultRowsPerFrame = 8;
    public const int MinimumTextureSize = 128;
    public const int MaximumCloudLod = 3;
    public const float ScrollPhasePerSecond = 2f;
    private const int BaseFrequency = 16;
    private const int CoordinateMask = GradientCount - 1;
    private const float HeightScale = 64f;
    private const float HeightOffset = 128f;
    private const float GradientDenominator = short.MaxValue;

    // g_dnNoisePermutation @0xAF4A70, build 12340. This is a permutation,
    // unlike the random gradient values made at client startup.
    private static readonly byte[] Permutation = Convert.FromHexString(
        "e19bd26cafc7dd90cb7446d5459e21fc0552ad85de8bae1b09475af64b825bbf" +
        "a98a0297c2eb51071971e49fcdfd868ef841e0d91679e53f596760689c11c981" +
        "2408a56eed75e73884d39814b56fefdaaaa333ac9d2f50d4b0fa573163f288bd" +
        "a2732c2b7c5e96108df7200ac6dfff4835835439dcc53a32d00bf11c03c03eca" +
        "12d799184c290fb3272e370680a717bc6a22bb8ca44970b6f4c3e30d234dc4b9" +
        "1ac8e2771f7ba87df944b7e6b187a0b40c01f39466a626eefb25f07e404aa128" +
        "b895abb265421d3b923dfe6b2a569a04ece87815e9d12d62c1724e13ce0e767f" +
        "304f93551ecfdb3658eabe7a5f438f6d89d6915d5c64f500d8ba3c536961cc34");

    private static readonly float[] Fade = CreateFadeTable();
    private readonly float[] _gradients;

    public Wrath335CloudNoise(ReadOnlySpan<float> gradients)
    {
        if (gradients.Length != GradientCount)
            throw new ArgumentException("The client uses 256 gradient values.", nameof(gradients));
        _gradients = gradients.ToArray();
    }

    public static Wrath335CloudNoise CreateProcessRandom()
    {
        var gradients = new float[GradientCount];
        for (var index = 0; index < gradients.Length; index++)
        {
            var clientRandValue = Random.Shared.Next(0, short.MaxValue + 1);
            gradients[index] = (float)(1.0 -
                2.0 * clientRandValue / GradientDenominator);
        }
        return new Wrath335CloudNoise(gradients);
    }

    public (float Height, float NormalHeight) Sample(
        int column, int row, ushort phase, int lod)
    {
        if ((uint)lod > MaximumCloudLod)
            throw new ArgumentOutOfRangeException(nameof(lod));

        var total = 0f;
        var normalHeight = 0f;
        var baseFrequency = BaseFrequency >> lod;
        for (var octave = 0; octave < OctaveCount; octave++)
        {
            var frequency = baseFrequency << octave;
            var x = unchecked((ushort)(phase + column * frequency));
            var y = unchecked((ushort)(row * frequency));
            var value = SampleLattice(x, y, phase) / (1 << octave);
            total += value;
            if (octave < OctaveCount - 1)
                normalHeight += value;
        }
        return (total, normalHeight);
    }

    public static byte ToHeightByte(float noiseHeight) =>
        unchecked((byte)(int)MathF.Round(
            noiseHeight * HeightScale + HeightOffset,
            MidpointRounding.ToEven));

    public static byte ToCoverageByte(float density) =>
        unchecked((byte)(int)((1f - density) * byte.MaxValue));

    public static byte ToAlpha(byte height, byte coverage, ReadOnlySpan<byte> densityLut)
    {
        if (densityLut.Length != Wrath335CloudDensityLut.EntryCount)
            throw new ArgumentException("The client density lookup has 256 values.", nameof(densityLut));
        var index = height - coverage;
        return index < 0 ? (byte)0 : densityLut[index];
    }

    private float SampleLattice(ushort x, ushort y, ushort z)
    {
        var x0 = x >> FractionBits;
        var y0 = y >> FractionBits;
        var z0 = z >> FractionBits;
        var xFade = Fade[x & CoordinateMask];
        var yFade = Fade[y & CoordinateMask];
        var zFade = Fade[z & CoordinateMask];

        var near = InterpolatePlane(x0, y0, z0, xFade, yFade);
        var far = InterpolatePlane(x0, y0, (z0 + 1) & CoordinateMask, xFade, yFade);
        return near + (far - near) * zFade;
    }

    private float InterpolatePlane(
        int x, int y, int z, float xFade, float yFade)
    {
        var lower = InterpolateX(x, y, z, xFade);
        var upper = InterpolateX(x, (y + 1) & CoordinateMask, z, xFade);
        return lower + (upper - lower) * yFade;
    }

    private float InterpolateX(int x, int y, int z, float fade)
    {
        var first = Gradient(x, y, z);
        var second = Gradient((x + 1) & CoordinateMask, y, z);
        return first + (second - first) * fade;
    }

    private float Gradient(int x, int y, int z)
    {
        var zHash = Permutation[z];
        var yHash = Permutation[(y + zHash) & CoordinateMask];
        var xHash = Permutation[(x + yHash) & CoordinateMask];
        return _gradients[xHash];
    }

    private static float[] CreateFadeTable()
    {
        var values = new float[GradientCount];
        for (var index = 0; index < values.Length; index++)
            values[index] = (float)((1.0 -
                Math.Cos(index * Math.PI / GradientCount)) * 0.5);
        return values;
    }
}
