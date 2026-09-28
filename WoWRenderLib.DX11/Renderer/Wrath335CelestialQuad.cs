using System.Numerics;
using System.Runtime.InteropServices;

namespace WoWRenderLib.DX11.Renderer;

[StructLayout(LayoutKind.Sequential)]
internal struct Wrath335CelestialVertex
{
    public Vector3 LocalPosition;
    public Vector2 TextureCoordinate;
    public Vector4 Color;
}

/// <summary>
/// Client celestial billboard quad before its camera-facing transform.
/// The client clips the lower edge to the eye-height horizon and inserts
/// a vertex row at the end of the 0.4-unit alpha fade when needed.
/// </summary>
internal static class Wrath335CelestialQuad
{
    internal const int MaxVertices = 6;
    internal const float HalfSizeScale = 0.5f;
    internal const float HorizonFadeHeight = 0.40000001f;
    internal const float HorizonFadeSlope = 2.5f;
    internal const float FadeSplitTolerance = 0.001f;
    internal const float BasisAxisTolerance = 0.0000099999997f;

    private static readonly ushort[] FourVertexIndexValues =
        [0, 1, 2, 1, 3, 2];
    private static readonly ushort[] SixVertexIndexValues =
        [0, 1, 2, 1, 3, 2, 2, 3, 4, 3, 5, 4];

    internal static ReadOnlySpan<ushort> FourVertexIndices => FourVertexIndexValues;
    internal static ReadOnlySpan<ushort> SixVertexIndices => SixVertexIndexValues;

    /// <summary>
    /// BuildOrthonormalBasisFromDirection (0x9ABB60) uses the view forward
    /// column for the billboard's local X axis. Its local Y remains horizontal
    /// and local Z is their cross product.
    /// </summary>
    internal static void BuildCameraFacingBasis(
        Vector3 viewForward, out Vector3 horizontal, out Vector3 vertical)
    {
        var forward = Vector3.Normalize(viewForward);
        horizontal = new Vector3(-forward.Y, forward.X, 0f);
        if (MathF.Abs(forward.X * horizontal.X) <= BasisAxisTolerance)
            horizontal = Vector3.UnitY;
        else
            horizontal = Vector3.Normalize(horizontal);
        vertical = Vector3.Cross(forward, horizontal);
    }

    internal static int Build(
        Span<Wrath335CelestialVertex> vertices,
        float centerHeightAboveEye,
        float fullSize,
        Vector4 tint)
    {
        if (vertices.Length < MaxVertices)
            throw new ArgumentException($"Expected {MaxVertices} vertices.", nameof(vertices));
        if (fullSize <= 0f)
            return 0;

        var halfSize = fullSize * HalfSizeScale;
        var topHeight = centerHeightAboveEye + halfSize;
        var bottomHeight = centerHeightAboveEye - halfSize;
        if (topHeight < 0f && bottomHeight < 0f)
            return 0;

        var bottomLocalHeight = bottomHeight <= 0f
            ? -centerHeightAboveEye
            : -halfSize;
        var bottomV = bottomHeight <= 0f ? topHeight / fullSize : 1f;
        AddRow(vertices, 0, halfSize, halfSize, 0f, centerHeightAboveEye, tint);

        var bottomVisibleHeight = centerHeightAboveEye + bottomLocalHeight;
        if (topHeight - HorizonFadeHeight > FadeSplitTolerance &&
            bottomVisibleHeight - HorizonFadeHeight < FadeSplitTolerance)
        {
            var fadeLocalHeight = HorizonFadeHeight - centerHeightAboveEye;
            var fadeV = (halfSize - fadeLocalHeight) / fullSize;
            AddRow(vertices, 2, halfSize, fadeLocalHeight, fadeV, centerHeightAboveEye, tint);
            AddRow(vertices, 4, halfSize, bottomLocalHeight, bottomV, centerHeightAboveEye, tint);
            return 6;
        }

        AddRow(vertices, 2, halfSize, bottomLocalHeight, bottomV, centerHeightAboveEye, tint);
        return 4;
    }

    private static void AddRow(
        Span<Wrath335CelestialVertex> vertices,
        int index,
        float halfSize,
        float localHeight,
        float v,
        float centerHeightAboveEye,
        Vector4 tint)
    {
        var height = centerHeightAboveEye + localHeight;
        var alpha = height - HorizonFadeHeight < FadeSplitTolerance
            ? (byte)(int)(Math.Clamp(height * HorizonFadeSlope, 0f, 1f) * 255f) / 255f
            : tint.W;
        var color = new Vector4(tint.X, tint.Y, tint.Z, alpha);
        // Retain local X=0 as in DayNight::BuildCelestialQuad.
        vertices[index] = new Wrath335CelestialVertex
        {
            LocalPosition = new Vector3(0f, -halfSize, localHeight),
            TextureCoordinate = new Vector2(0f, v),
            Color = color
        };
        vertices[index + 1] = new Wrath335CelestialVertex
        {
            LocalPosition = new Vector3(0f, halfSize, localHeight),
            TextureCoordinate = new Vector2(1f, v),
            Color = color
        };
    }
}
