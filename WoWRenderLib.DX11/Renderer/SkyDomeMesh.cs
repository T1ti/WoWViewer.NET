using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

[StructLayout(LayoutKind.Sequential)]
internal struct SkyDomeVertex
{
    public Vector3 Position;
    public Vector4 Color;
}

/// <summary>
/// CPU-side 3.3.5 DNSky geometry and vertex colors. Positions and indices are
/// built once; only colors change when the sampled environment changes.
/// </summary>
internal static class SkyDomeMesh
{
    internal const int AzimuthSegments = Wrath335SkyReference.AzimuthSegments;
    internal const int VertexCount = 2 + Wrath335SkyReference.InteriorRings * AzimuthSegments;
    internal const int TriangleIndexCount =
        AzimuthSegments * (2 + 2 * (Wrath335SkyReference.InteriorRings - 1)) * 3;

    private const float Tau = 2f * MathF.PI;

    internal static SkyDomeVertex[] CreateVertices()
    {
        var vertices = new SkyDomeVertex[VertexCount];
        vertices[0].Position = new Vector3(0f, 0f,
            (1f - Wrath335SkyReference.VerticalOffset) * Wrath335SkyReference.RenderScale);

        for (var ring = 1; ring <= Wrath335SkyReference.InteriorRings; ring++)
        {
            // DNSky::Build (0x7F2470) uses the client's cubic sine/cosine
            // approximation for ring elevation, then scales the shifted dome.
            var phase = Wrath335SkyReference.RingElevations[ring] * MathF.PI *
                Wrath335SkyReference.InversePi;
            var radius = Wrath335SkyReference.CubicCosine(
                phase - Wrath335SkyReference.SinePhaseShift) *
                Wrath335SkyReference.RenderScale;
            var z = (Wrath335SkyReference.CubicCosine(phase) - Wrath335SkyReference.VerticalOffset) *
                Wrath335SkyReference.RenderScale;
            for (var segment = 0; segment < AzimuthSegments; segment++)
            {
                var azimuth = segment * (Tau / AzimuthSegments);
                vertices[RingVertex(ring, segment)].Position = new Vector3(
                    MathF.Sin(azimuth) * radius,
                    MathF.Cos(azimuth) * radius,
                    z);
            }
        }

        vertices[^1].Position = new Vector3(0f, 0f,
            (-1f - Wrath335SkyReference.VerticalOffset) * Wrath335SkyReference.RenderScale);
        return vertices;
    }

    internal static ushort[] CreateTriangleIndices()
    {
        // The client stores six 50-index triangle strips. Discard their
        // degenerate pole triangles and submit the same 240 visible triangles
        // in one DX11 indexed draw.
        var indices = new ushort[TriangleIndexCount];
        var cursor = 0;
        for (var segment = 0; segment < AzimuthSegments; segment++)
        {
            var next = (segment + 1) % AzimuthSegments;
            Add(0, RingVertex(1, segment), RingVertex(1, next));
        }

        for (var ring = 1; ring < Wrath335SkyReference.InteriorRings; ring++)
        {
            for (var segment = 0; segment < AzimuthSegments; segment++)
            {
                var next = (segment + 1) % AzimuthSegments;
                Add(RingVertex(ring, segment), RingVertex(ring + 1, segment),
                    RingVertex(ring, next));
                Add(RingVertex(ring, next), RingVertex(ring + 1, segment),
                    RingVertex(ring + 1, next));
            }
        }

        for (var segment = 0; segment < AzimuthSegments; segment++)
        {
            var next = (segment + 1) % AzimuthSegments;
            Add(RingVertex(Wrath335SkyReference.InteriorRings, segment),
                VertexCount - 1, RingVertex(Wrath335SkyReference.InteriorRings, next));
        }

        return indices;

        void Add(int a, int b, int c)
        {
            indices[cursor++] = checked((ushort)a);
            indices[cursor++] = checked((ushort)b);
            indices[cursor++] = checked((ushort)c);
        }
    }

    internal static void SetColors(
        Span<SkyDomeVertex> vertices,
        WorldSkyLighting sky,
        float glowStrength,
        float clientSunAzimuth)
    {
        if (vertices.Length != VertexCount)
            throw new ArgumentException($"Expected {VertexCount} sky vertices.", nameof(vertices));

        var fog = PackInputColor(sky.FogColor);
        // The 3.3.5 DNSky::SetColors reads the sampled sky bands directly.
        // Its skybox setup uses flags 1 and 2, but not the later-client
        // flag-4 fog-color override.
        var top = PackInputColor(sky.TopColor);
        var middle = PackInputColor(sky.MiddleColor);
        var band1 = PackInputColor(sky.Band1Color);
        var band2 = PackInputColor(sky.Band2Color);
        var smog = PackInputColor(sky.SmogColor);
        vertices[0].Color = new Vector4(top / 255f, 1f);

        Span<float> azimuthGlow = stackalloc float[AzimuthSegments];
        for (var segment = 0; segment < AzimuthSegments; segment++)
        {
            var phase = clientSunAzimuth / Tau + Wrath335SkyReference.SunwardAzimuthPhase -
                segment / (float)AzimuthSegments;
            phase -= MathF.Floor(phase);
            azimuthGlow[segment] = SkyAzimuthGlow(phase);
        }

        for (var ring = 1; ring < Wrath335SkyReference.InteriorRings; ring++)
        {
            var band = ring switch
            {
                1 => middle,
                2 => band1,
                3 => band2,
                _ => smog
            };
            var washed = LerpPackedColor(band, middle, glowStrength);
            for (var segment = 0; segment < AzimuthSegments; segment++)
            {
                // DNSky::SetColors (0x7F0530) evaluates the azimuth table
                // at each of the 24 vertices. DayNight::LerpCImVector
                // (0x7ED2D0) truncates every intermediate RGB to bytes.
                var glow = azimuthGlow[segment];
                Vector3 color;
                if (glow < 0f)
                {
                    var towardZenith = LerpPackedColor(washed, top,
                        Wrath335SkyReference.AntisolarZenithBlend * glowStrength);
                    color = LerpPackedColor(washed, towardZenith,
                        -glow * glowStrength);
                }
                else
                {
                    color = LerpPackedColor(band, washed,
                        (1f - glow) * glowStrength);
                }
                vertices[RingVertex(ring, segment)].Color = new Vector4(color / 255f, 1f);
            }
        }

        for (var segment = 0; segment < AzimuthSegments; segment++)
            vertices[RingVertex(Wrath335SkyReference.InteriorRings, segment)].Color =
                new Vector4(fog / 255f, 1f);
        vertices[^1].Color = new Vector4(fog / 255f, 1f);
    }

    private static int RingVertex(int ring, int segment) =>
        1 + (ring - 1) * AzimuthSegments + segment;

    private static Vector3 PackInputColor(Vector3 color) => new(
        Math.Clamp(MathF.Round(color.X * 255f), 0f, 255f),
        Math.Clamp(MathF.Round(color.Y * 255f), 0f, 255f),
        Math.Clamp(MathF.Round(color.Z * 255f), 0f, 255f));

    private static Vector3 LerpPackedColor(Vector3 from, Vector3 to, float amount) => new(
        PackLerpChannel(from.X, to.X, amount),
        PackLerpChannel(from.Y, to.Y, amount),
        PackLerpChannel(from.Z, to.Z, amount));

    private static float PackLerpChannel(float from, float to, float amount)
    {
        // g_dnTimeFloorBias = 0.5. The x86 float-to-int conversion truncates
        // toward zero before CImVector stores the low byte.
        var interpolated = from + (to - from) * amount;
        return (byte)(int)(interpolated - Wrath335SkyReference.PackedColorFloorBias);
    }

    private static float SkyAzimuthGlow(float phase)
    {
        // g_dnSkyAzimuthGlowCurve at 0xAF4BAC, linearly wrapped by
        // DayNight__InterpTable (0x7ED3B0).
        var curve = Wrath335SkyReference.AzimuthGlowCurve;
        var next = 0;
        while (next < curve.Length && phase > curve[next].X)
            next++;
        if (next == curve.Length)
            next = 0;
        var previous = next == 0 ? curve.Length - 1 : next - 1;
        var start = curve[previous];
        var end = curve[next];
        var endPhase = next == 0 ? end.X + 1f : end.X;
        var samplePhase = phase < start.X ? phase + 1f : phase;
        var blend = (samplePhase - start.X) / (endPhase - start.X);
        return start.Y + (end.Y - start.Y) * blend;
    }
}
