using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WTEditor.Avalonia.ViewModels;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.Controls;

/// <summary>Draws a preset's exact sampled strength mask as a square grayscale image.</summary>
public sealed class BrushMaskPreview : Control
{
    private const int SampleCount = 32;
    private const float PreviewFalloff = 0.35f;
    private static readonly IBrush[] StrengthBrushes = Enumerable.Range(0, 256)
        .Select(value => (IBrush)new SolidColorBrush(Color.FromRgb((byte)value, (byte)value, (byte)value)))
        .ToArray();

    public static readonly StyledProperty<BrushPresetViewModel?> PresetProperty =
        AvaloniaProperty.Register<BrushMaskPreview, BrushPresetViewModel?>(nameof(Preset));

    static BrushMaskPreview() => AffectsRender<BrushMaskPreview>(PresetProperty);

    public BrushPresetViewModel? Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (Preset is not { } preset || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var pixelWidth = Bounds.Width / SampleCount;
        var pixelHeight = Bounds.Height / SampleCount;
        var brush = new BrushInput(
            1f,
            PreviewFalloff,
            preset.SupportsFalloff,
            preset.Shape,
            preset.FalloffProfile);

        for (var y = 0; y < SampleCount; y++)
        {
            var sampleY = ((y + 0.5f) / SampleCount * 2f) - 1f;
            for (var x = 0; x < SampleCount; x++)
            {
                var sampleX = ((x + 0.5f) / SampleCount * 2f) - 1f;
                var strength = BrushMath.CalculateInfluence(sampleX, sampleY, brush);
                var shade = (int)MathF.Round(Math.Clamp(strength, 0f, 1f) * 255f);
                context.FillRectangle(
                    StrengthBrushes[shade],
                    new Rect(x * pixelWidth, y * pixelHeight, pixelWidth + 0.1, pixelHeight + 0.1));
            }
        }
    }
}
