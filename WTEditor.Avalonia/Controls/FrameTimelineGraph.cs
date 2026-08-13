using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.Controls;

public sealed class FrameTimelineGraph : Control
{
    public static readonly StyledProperty<IReadOnlyList<FrameProfileSnapshot>?> SamplesProperty =
        AvaloniaProperty.Register<FrameTimelineGraph, IReadOnlyList<FrameProfileSnapshot>?>(nameof(Samples));

    private static readonly Pen GridPen = new(Brush("#35404C"), 1);

    public IReadOnlyList<FrameProfileSnapshot>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    static FrameTimelineGraph() =>
        AffectsRender<FrameTimelineGraph>(SamplesProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        context.FillRectangle(Brush("#12171D"), bounds);

        var samples = Samples;
        if (samples == null || samples.Count == 0 || bounds.Width < 2 || bounds.Height < 2)
            return;

        var maximumObserved = samples.Max(sample => Math.Max(
            sample.Steps.Where(step => step.Domain == FrameTimingDomain.Cpu)
                .Sum(step => step.DurationMilliseconds),
            sample.Steps.Where(step => step.Domain == FrameTimingDomain.Gpu)
                .Sum(step => step.DurationMilliseconds)));
        var scaleMilliseconds = ChooseScale(maximumObserved);

        DrawBudgetLine(context, bounds, 8.333d, scaleMilliseconds);
        DrawBudgetLine(context, bounds, 16.667d, scaleMilliseconds);
        DrawBudgetLine(context, bounds, 33.333d, scaleMilliseconds);

        var columnWidth = bounds.Width / samples.Count;
        for (var sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
        {
            var sample = samples[sampleIndex];
            var x = sampleIndex * columnWidth;
            var barWidth = Math.Max(0.75, columnWidth * 0.4);
            DrawStack(context, bounds, sample, FrameTimingDomain.Cpu, x, barWidth, scaleMilliseconds);
            DrawStack(context, bounds, sample, FrameTimingDomain.Gpu,
                x + Math.Max(barWidth, columnWidth * 0.5), barWidth, scaleMilliseconds);
        }
    }

    private static void DrawStack(
        DrawingContext context,
        Rect bounds,
        FrameProfileSnapshot sample,
        FrameTimingDomain domain,
        double x,
        double width,
        double scaleMilliseconds)
    {
        var bottom = bounds.Height;
        foreach (var step in sample.Steps.Where(step => step.Domain == domain))
        {
            var height = Math.Min(bounds.Height, step.DurationMilliseconds / scaleMilliseconds * bounds.Height);
            bottom -= height;
            context.FillRectangle(
                FrameStepBrushConverter.BrushFor(step.Name),
                new Rect(x, Math.Max(0, bottom), width, height));
        }
    }

    private static double ChooseScale(double maximumObserved)
    {
        var padded = Math.Max(8.333d, maximumObserved * 1.15d);
        if (padded <= 8.333d)
            return 8.333d;
        if (padded <= 16.667d)
            return 16.667d;
        if (padded <= 33.333d)
            return 33.333d;

        return Math.Ceiling(padded / 10d) * 10d;
    }

    private static void DrawBudgetLine(
        DrawingContext context,
        Rect bounds,
        double milliseconds,
        double scaleMilliseconds)
    {
        if (milliseconds > scaleMilliseconds)
            return;

        var y = bounds.Height - milliseconds / scaleMilliseconds * bounds.Height;
        context.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}

public sealed class FrameStepBrushConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, IBrush> Brushes =
        new Dictionary<string, IBrush>(StringComparer.Ordinal)
        {
            ["World streaming (CPU)"] = Brush("#FFD166"),
            ["Resource upload submission (CPU)"] = Brush("#F78C6B"),
            ["WMO culling (CPU)"] = Brush("#65D6C1"),
            ["WMO command submission (CPU)"] = Brush("#A88BFA"),
            ["M2 culling (CPU)"] = Brush("#78D5E3"),
            ["M2 command submission (CPU)"] = Brush("#C792EA"),
            ["Terrain culling (CPU)"] = Brush("#A7D46F"),
            ["Terrain command submission (CPU)"] = Brush("#D8A0DF"),
            ["Scene setup / debug (CPU)"] = Brush("#8494A7"),
            ["Other frame work (CPU)"] = Brush("#5EA1FF"),
            ["Resource uploads (GPU timeline)"] = Brush("#FF9F43"),
            ["World span (GPU timeline)"] = Brush("#FF5DA2"),
            ["WMO span (GPU timeline)"] = Brush("#FF5DA2"),
            ["M2 span (GPU timeline)"] = Brush("#FF7A90"),
            ["Terrain span (GPU timeline)"] = Brush("#EF6FFF"),
            ["Debug span (GPU timeline)"] = Brush("#B89CFF"),
            ["Other GPU timeline"] = Brush("#B455D4"),
            ["Wait for viewport texture"] = Brush("#607080")
        };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BrushFor(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static IBrush BrushFor(string? name) =>
        name != null && Brushes.TryGetValue(name, out var brush) ? brush : Brush("#718090");

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
