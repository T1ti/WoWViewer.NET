using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

[StructLayout(LayoutKind.Sequential)]
internal struct Wrath335CloudVertex
{
    public Vector3 Position;
    public Vector2 TextureCoordinate;
    public Vector4 Color;
}

/// <summary>DNClouds::BuildMesh (0x7F20E0) fixed cloud-cap geometry.</summary>
internal static class Wrath335CloudMesh
{
    internal const int AzimuthSegments = 16;
    internal const int RingCount = 12;
    internal const int VertexCount = 1 + (RingCount - 1) * AzimuthSegments;
    internal const int StripIndexCount =
        (RingCount - 1) * (AzimuthSegments + 1) * 2;
    private const float TextureCenter = 0.5f;
    private const float TextureRadius = 0.5f;
    private const float Tau = 2f * MathF.PI;

    private static readonly float[] RingElevations =
    [
        0f, 0.025f, 0.05f, 0.075f, 0.1f, 0.125f,
        0.15f, 0.175f, 0.205f, 0.23f, 0.245f, 0.25f
    ];

    private static readonly byte[] RingAlphas =
    [
        255, 255, 255, 255, 255, 255,
        255, 255, 255, 128, 0, 0
    ];

    internal static Wrath335CloudVertex[] CreateVertices()
    {
        var vertices = new Wrath335CloudVertex[VertexCount];
        vertices[0] = CreateVertex(0, 0);
        for (var ring = 1; ring < RingCount; ring++)
        {
            for (var segment = 0; segment < AzimuthSegments; segment++)
                vertices[1 + (ring - 1) * AzimuthSegments + segment] =
                    CreateVertex(ring, segment);
        }
        return vertices;
    }

    internal static ushort[] CreateStripIndices()
    {
        var indices = new ushort[StripIndexCount];
        var cursor = 0;
        for (var ring = 1; ring < RingCount; ring++)
        {
            var previousStart = ring == 1
                ? 0
                : 1 + (ring - 2) * AzimuthSegments;
            var currentStart = 1 + (ring - 1) * AzimuthSegments;
            for (var segment = 0; segment <= AzimuthSegments; segment++)
            {
                var wrappedSegment = segment % AzimuthSegments;
                indices[cursor++] = checked((ushort)(ring == 1
                    ? 0 : previousStart + wrappedSegment));
                indices[cursor++] = checked((ushort)(currentStart + wrappedSegment));
            }
        }
        return indices;
    }

    private static Wrath335CloudVertex CreateVertex(int ring, int segment)
    {
        var elevation = RingElevations[ring] * MathF.PI;
        var azimuth = segment * Tau / AzimuthSegments;
        var radialDistance = MathF.Sin(elevation);
        var sine = MathF.Sin(azimuth);
        var cosine = MathF.Cos(azimuth);
        var textureRadius = TextureRadius * ring / (RingCount - 1);
        return new Wrath335CloudVertex
        {
            Position = new Vector3(
                sine * radialDistance,
                cosine * radialDistance,
                MathF.Cos(elevation) - Wrath335SkyReference.VerticalOffset) *
                Wrath335SkyReference.RenderScale,
            TextureCoordinate = new Vector2(
                TextureCenter + sine * textureRadius,
                TextureCenter + cosine * textureRadius),
            Color = new Vector4(1f, 1f, 1f,
                RingAlphas[ring] / (float)byte.MaxValue)
        };
    }
}
