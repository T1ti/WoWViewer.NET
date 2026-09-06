using System.Numerics;

namespace WoWRenderLib.DX11;

public enum TextureBrushMode
{
    Paint,
    Colour
}

public readonly record struct TextureBrushInput(
    TextureBrushMode ToolMode,
    byte Opacity,
    float Strength);

/// <summary>Presentation metadata for texture operations, independent of brush geometry.</summary>
public static class TextureBrushModes
{
    public static Vector4 GetPreviewColor(TextureBrushMode mode) => mode switch
    {
        TextureBrushMode.Paint => new Vector4(1f, 0.58f, 0.18f, 1f),
        TextureBrushMode.Colour => new Vector4(1f, 0.32f, 0.72f, 1f),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported texture brush mode.")
    };
}
