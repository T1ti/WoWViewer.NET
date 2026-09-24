using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.Controls;

public sealed class FrameTimelineGraph : Control
{
    private readonly record struct StackSegment(
        FrameTimingStep Step,
        double Top,
        double Bottom)
    {
        public double Height => Math.Max(0d, Bottom - Top);
    }

    public static readonly StyledProperty<IReadOnlyList<FrameProfileSnapshot>?> SamplesProperty =
        AvaloniaProperty.Register<FrameTimelineGraph, IReadOnlyList<FrameProfileSnapshot>?>(nameof(Samples));

    private static readonly Pen GridPen = new(Brush("#35404C"), 1);
    private string? _activeTooltip;

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
            DomainTotal(sample, FrameTimingDomain.Cpu),
            DomainTotal(sample, FrameTimingDomain.Gpu)));
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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateTooltip(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        UpdateTooltip(null);
    }

    private void UpdateTooltip(Point? pointerPosition)
    {
        var tooltip = pointerPosition is { } point
            ? FindTooltip(point)
            : null;
        if (string.Equals(_activeTooltip, tooltip, StringComparison.Ordinal))
            return;

        _activeTooltip = tooltip;
        ToolTip.SetTip(this, tooltip);
        ToolTip.SetIsOpen(this, tooltip != null);
    }

    private string? FindTooltip(Point point)
    {
        var bounds = Bounds;
        var samples = Samples;
        if (samples == null || samples.Count == 0 || bounds.Width < 2 || bounds.Height < 2 ||
            point.X < 0 || point.X > bounds.Width || point.Y < 0 || point.Y > bounds.Height)
            return null;

        var sampleIndex = Math.Clamp((int)(point.X / (bounds.Width / samples.Count)), 0, samples.Count - 1);
        var sample = samples[sampleIndex];
        var columnWidth = bounds.Width / samples.Count;
        var x = sampleIndex * columnWidth;
        var barWidth = Math.Max(0.75, columnWidth * 0.4);
        var domain = point.X >= x && point.X <= x + barWidth
            ? FrameTimingDomain.Cpu
            : point.X >= x + Math.Max(barWidth, columnWidth * 0.5) &&
              point.X <= x + Math.Max(barWidth, columnWidth * 0.5) + barWidth
                ? FrameTimingDomain.Gpu
                : (FrameTimingDomain?)null;
        if (domain is null)
            return null;

        var maximumObserved = samples.Max(currentSample => Math.Max(
            DomainTotal(currentSample, FrameTimingDomain.Cpu),
            DomainTotal(currentSample, FrameTimingDomain.Gpu)));
        var scaleMilliseconds = ChooseScale(maximumObserved);
        foreach (var segment in BuildStackSegments(sample, domain.Value, bounds.Height, scaleMilliseconds))
        {
            if (point.Y >= segment.Top && point.Y <= segment.Bottom)
            {
                return $"{segment.Step.Name}\n{FrameTimingCategoryCatalog.Describe(segment.Step.Name)}\nFrame {sample.FrameNumber}: {segment.Step.DurationMilliseconds:F3} ms";
            }
        }

        return null;
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
        foreach (var segment in BuildStackSegments(sample, domain, bounds.Height, scaleMilliseconds))
        {
            if (segment.Height <= 0)
                continue;

            context.FillRectangle(
                FrameStepBrushConverter.BrushFor(segment.Step.Name),
                new Rect(x, segment.Top, width, segment.Height));
        }
    }

    private static IReadOnlyList<StackSegment> BuildStackSegments(
        FrameProfileSnapshot sample,
        FrameTimingDomain domain,
        double graphHeight,
        double scaleMilliseconds)
    {
        var steps = OrderedSteps(sample, domain).ToArray();
        if (steps.Length == 0 || graphHeight <= 0 || !double.IsFinite(scaleMilliseconds) || scaleMilliseconds <= 0)
            return Array.Empty<StackSegment>();

        var rawHeights = steps
            .Select(step => step.DurationMilliseconds / scaleMilliseconds * graphHeight)
            .ToArray();
        var rawTotalHeight = rawHeights.Sum();
        var minimumVisibleHeight = Math.Min(1d, graphHeight / steps.Length);
        var minimumHeightExpansion = rawHeights.Sum(height => Math.Max(0d, minimumVisibleHeight - height));
        var showMinimumHeights =
            rawTotalHeight + minimumHeightExpansion <= graphHeight + 0.0001d;

        var segments = new StackSegment[steps.Length];
        var bottom = graphHeight;
        for (var index = 0; index < steps.Length; index++)
        {
            var height = rawHeights[index];
            if (showMinimumHeights)
                height = Math.Max(height, minimumVisibleHeight);

            var top = bottom - height;
            segments[index] = new StackSegment(steps[index], top, bottom);
            bottom = top;
        }

        return segments;
    }

    private static IEnumerable<FrameTimingStep> OrderedSteps(
        FrameProfileSnapshot sample,
        FrameTimingDomain domain) =>
        FrameTimingCategoryCatalog.OrderSteps(sample.Steps, domain);

    private static double DomainTotal(FrameProfileSnapshot sample, FrameTimingDomain domain) =>
        FrameTimingCategoryCatalog.OrderSteps(sample.Steps, domain)
            .Sum(step => step.DurationMilliseconds);

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
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BrushFor(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static IBrush BrushFor(string? name) =>
        Brush(FrameTimingCategoryCatalog.ColorFor(name));

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
