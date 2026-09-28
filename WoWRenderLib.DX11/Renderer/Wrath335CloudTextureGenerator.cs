using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct Wrath335CloudUpdate(
    int TextureIndex, int FirstRow, int RowCount, bool InitializeBoth);

/// <summary>
/// CPU part of the build 12340 DNClouds procedural texture. The inactive
/// texture receives eight rows per frame; a completed texture becomes visible
/// only when the client's quantized scroll phase changes.
/// </summary>
internal sealed class Wrath335CloudTextureGenerator
{
    internal const int DefaultLod = 0;
    internal const int ClientMipCount = 2;
    private const int MinimumTextureSize = Wrath335CloudNoise.MinimumTextureSize;
    private const int ChannelsPerPixel = 4;
    private const float NormalReferenceSize = 128f;
    private const float CloudLightHeight = 64f;
    private const float CloudWeatherHeightRange = 192f;
    private const float WeatherEmissiveReduction = 0.75f;
    private const float TexelShadeOffset = 64f;
    private const int FastInverseSquareRootMagic = 0x5F3997BB;
    private const float MoonHandoffMorning = 0.2013889f;
    private const float MoonHandoffEvening = 0.9236111f;
    private const float CloudElevationLimit = MathF.PI / 4f;
    private const float SmallHorizontalVector = 0.00001f;

    private readonly Wrath335CloudNoise _noise;
    private readonly byte[] _densityLookup = Wrath335CloudDensityLut.Create();
    private readonly byte[][] _pixels;
    private readonly byte[] _density;
    private readonly float[] _previousRowHeight;
    private ushort _phase;
    private float _elapsedSeconds;
    private int _nextRow;
    private bool _initialized;

    public Wrath335CloudTextureGenerator(int lod, Wrath335CloudNoise? noise = null)
    {
        if ((uint)lod > Wrath335CloudNoise.MaximumCloudLod)
            throw new ArgumentOutOfRangeException(nameof(lod));
        Lod = lod;
        Size = MinimumTextureSize << lod;
        _noise = noise ?? Wrath335CloudNoise.CreateProcessRandom();
        _pixels =
        [
            new byte[Size * Size * ChannelsPerPixel],
            new byte[Size * Size * ChannelsPerPixel]
        ];
        Array.Fill(_pixels[0], byte.MaxValue);
        Array.Fill(_pixels[1], byte.MaxValue);
        _density = new byte[Size * Size];
        _previousRowHeight = new float[Size];
    }

    public int Lod { get; }
    public int Size { get; }
    public int ActiveTextureIndex { get; private set; }
    public ReadOnlySpan<byte> GetPixels(int index) => _pixels[index];
    public ReadOnlySpan<byte> Density => _density;

    public byte[] CreateFirstMip(int index)
    {
        var source = _pixels[index];
        var halfSize = Size / 2;
        var mip = new byte[halfSize * halfSize * ChannelsPerPixel];
        for (var row = 0; row < halfSize; row++)
        {
            for (var column = 0; column < halfSize; column++)
            {
                var sourceIndex = (row * 2 * Size + column * 2) * ChannelsPerPixel;
                var destinationIndex = (row * halfSize + column) * ChannelsPerPixel;
                for (var channel = 0; channel < ChannelsPerPixel; channel++)
                {
                    var sum = source[sourceIndex + channel] +
                        source[sourceIndex + ChannelsPerPixel + channel] +
                        source[sourceIndex + Size * ChannelsPerPixel + channel] +
                        source[sourceIndex + (Size + 1) * ChannelsPerPixel + channel];
                    mip[destinationIndex + channel] = (byte)(sum / 4);
                }
            }
        }
        return mip;
    }

    public Wrath335CloudUpdate Update(
        float elapsedSeconds, float cloudDensity,
        Vector3 bodyColor, Vector3 emissiveColor, Vector3 ambientColor,
        Vector3 lightDirection, float weatherBlend)
    {
        _elapsedSeconds += Math.Max(0f, elapsedSeconds);
        var coverage = Wrath335CloudNoise.ToCoverageByte(cloudDensity);
        weatherBlend = Math.Clamp(weatherBlend, 0f, 1f);
        var light = ProjectLightToTexture(lightDirection, Size, weatherBlend);
        var emissiveScale = 1f - weatherBlend * WeatherEmissiveReduction;

        if (!_initialized)
        {
            GenerateRows(0, Size, 0, coverage, bodyColor, emissiveColor,
                ambientColor, light, emissiveScale);
            _pixels[0].CopyTo(_pixels[1], 0);
            _initialized = true;
            return new Wrath335CloudUpdate(0, 0, Size, true);
        }

        var target = 1 - ActiveTextureIndex;
        var firstRow = _nextRow;
        var count = Math.Min(Wrath335CloudNoise.DefaultRowsPerFrame,
            Size - firstRow);
        GenerateRows(firstRow, count, target, coverage, bodyColor,
            emissiveColor, ambientColor, light, emissiveScale);
        _nextRow += count;
        if (_nextRow == Size)
        {
            var newPhase = unchecked((ushort)(int)(
                _elapsedSeconds * Wrath335CloudNoise.ScrollPhasePerSecond));
            if (newPhase != _phase)
            {
                _phase = newPhase;
                ActiveTextureIndex = target;
            }
            _nextRow = 0;
        }
        return new Wrath335CloudUpdate(target, firstRow, count, false);
    }

    internal byte SampleDensity(Vector3 rayDirection)
    {
        if (!_initialized)
            return 0;
        var texturePoint = ProjectLightToTexture(rayDirection, Size, 0f);
        var column = Math.Clamp((int)texturePoint.X, 0, Size - 1);
        var row = Math.Clamp((int)texturePoint.Y, 0, Size - 1);
        return _density[row * Size + column];
    }

    private void GenerateRows(
        int firstRow, int count, int target, byte coverage,
        Vector3 bodyColor, Vector3 emissiveColor, Vector3 ambientColor,
        Vector3 light, float emissiveScale)
    {
        var pixels = _pixels[target];
        var normalScale = Size / NormalReferenceSize;
        for (var row = firstRow; row < firstRow + count; row++)
        {
            var previousColumnHeight = 0f;
            for (var column = 0; column < Size; column++)
            {
                var (height, normalHeight) = _noise.Sample(
                    column, row, _phase, Lod);
                var derivativeX = (previousColumnHeight - normalHeight) * normalScale;
                var derivativeY =
                    (_previousRowHeight[column] - normalHeight) * normalScale;
                previousColumnHeight = normalHeight;
                _previousRowHeight[column] = normalHeight;

                var alpha = Wrath335CloudNoise.ToAlpha(
                    Wrath335CloudNoise.ToHeightByte(height),
                    coverage, _densityLookup);
                var pixelIndex = row * Size + column;
                var byteIndex = pixelIndex * ChannelsPerPixel;
                _density[pixelIndex] = alpha;
                if (alpha == 0)
                {
                    // DNClouds copies the left RGB texel to avoid dark bilinear
                    // fringes. Column zero retains the allocation's white byte.
                    if (column > 0)
                    {
                        pixels[byteIndex] = pixels[byteIndex - ChannelsPerPixel];
                        pixels[byteIndex + 1] = pixels[byteIndex - 3];
                        pixels[byteIndex + 2] = pixels[byteIndex - 2];
                        pixels[byteIndex + 3] = 0;
                    }
                    continue;
                }

                var shadeByte = ((byte.MaxValue - alpha) >> 1) + (int)TexelShadeOffset;
                var shade = shadeByte / (float)byte.MaxValue;
                var lightVector = new Vector3(
                    light.X - column, light.Y - row, light.Z);
                var normalLengthSquared =
                    derivativeX * derivativeX + derivativeY * derivativeY + 1f;
                var lightLengthSquared = lightVector.LengthSquared();
                var lightDot = (light.Z +
                    derivativeX * lightVector.X +
                    derivativeY * lightVector.Y) *
                    FastInverseSquareRoot(normalLengthSquared) *
                    FastInverseSquareRoot(lightLengthSquared);
                var highlight = Math.Max(0f, lightDot) * emissiveScale;
                var color = ambientColor + bodyColor * shade +
                    emissiveColor * highlight;
                pixels[byteIndex] = ToColorByte(color.Z);
                pixels[byteIndex + 1] = ToColorByte(color.Y);
                pixels[byteIndex + 2] = ToColorByte(color.X);
                pixels[byteIndex + 3] = alpha;
            }
        }
    }

    private static byte ToColorByte(float component) =>
        unchecked((byte)(int)(Math.Min(component, 1f) * byte.MaxValue));

    private static float FastInverseSquareRoot(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        return BitConverter.Int32BitsToSingle(
            FastInverseSquareRootMagic - ((bits >> 1) & 0x3FFF_FFFF));
    }

    internal static Vector3 ProjectLightToTexture(
        Vector3 direction, int textureSize, float weatherBlend)
    {
        // DNClouds::Collide chooses the forward exit of the unit sphere
        // centered 0.70710678 below the camera.
        var offset = Wrath335SkyReference.VerticalOffset;
        var a = direction.LengthSquared();
        if (a <= float.Epsilon)
            return new Vector3(textureSize * 0.5f, textureSize * 0.5f,
                CloudLightHeight);
        var b = 2f * offset * direction.Z;
        var c = offset * offset - 1f;
        var discriminant = b * b - 4f * a * c;
        var rayLength = (-b + MathF.Sqrt(Math.Max(0f, discriminant))) / (2f * a);
        var hit = direction * rayLength;
        var sphereVector = new Vector3(hit.X, hit.Y, hit.Z + offset);
        var polarAngle = MathF.Acos(Math.Clamp(
            sphereVector.Z / sphereVector.Length(), -1f, 1f));
        var textureRadius = Math.Min(polarAngle, CloudElevationLimit) /
            CloudElevationLimit * 0.5f;
        var horizontalLength = new Vector2(hit.X, hit.Y).Length();
        var horizontalDirection = horizontalLength <= SmallHorizontalVector
            ? Vector2.Zero
            : new Vector2(hit.X, hit.Y) / horizontalLength;
        return new Vector3(
            (0.5f + horizontalDirection.X * textureRadius) * textureSize,
            (0.5f + horizontalDirection.Y * textureRadius) * textureSize,
            CloudLightHeight + weatherBlend * CloudWeatherHeightRange);
    }

    internal static bool UseMoon(long lightTime)
    {
        var phase = DayNight.NormalizeTime(
            (int)(lightTime % DayNight.GameDayLength)) /
            (float)DayNight.GameDayLength;
        return phase < MoonHandoffMorning || phase > MoonHandoffEvening;
    }
}
