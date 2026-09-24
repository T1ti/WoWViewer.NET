using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.Controls;

/// <summary>
/// Shows total CPU and GPU frame durations against the same sample index.
/// The two series are intentionally independent lines rather than stacked work.
/// </summary>
public sealed class FrameDurationGraph : Control
{
    private static readonly IBrush BackgroundBrush = Brush("#12171D");
    private static readonly IBrush CpuBrush = Brush("#6CB6FF");
    private static readonly IBrush GpuBrush = Brush("#FF5DA2");
    private static readonly Pen CpuPen = new(CpuBrush, 2);
    private static readonly Pen GpuPen = new(GpuBrush, 2);
    private static readonly Pen GridPen = new(Brush("#35404C"), 1);

    public static readonly StyledProperty<IReadOnlyList<FrameProfileSnapshot>?> SamplesProperty =
        AvaloniaProperty.Register<FrameDurationGraph, IReadOnlyList<FrameProfileSnapshot>?>(nameof(Samples));

    private string? _activeTooltip;

    public IReadOnlyList<FrameProfileSnapshot>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    static FrameDurationGraph() =>
        AffectsRender<FrameDurationGraph>(SamplesProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        var samples = Samples;
        if (samples == null || samples.Count == 0 || bounds.Width < 2 || bounds.Height < 2)
            return;

        var maximumObserved = samples.Max(MaximumDuration);
        var scaleMilliseconds = ChooseScale(maximumObserved);

        DrawBudgetLine(context, bounds, 8.333d, scaleMilliseconds);
        DrawBudgetLine(context, bounds, 16.667d, scaleMilliseconds);
        DrawBudgetLine(context, bounds, 33.333d, scaleMilliseconds);

        DrawSeries(context, bounds, samples, FrameTimingDomain.Cpu, CpuPen, scaleMilliseconds);
        DrawSeries(context, bounds, samples, FrameTimingDomain.Gpu, GpuPen, scaleMilliseconds);
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
        var gpu = sample.GpuFrameMilliseconds is { } gpuMilliseconds
            ? $"{gpuMilliseconds:F3} ms"
            : "pending";
        return $"Frame {sample.FrameNumber}\nCPU total: {sample.CpuFrameMilliseconds:F3} ms\nGPU total: {gpu}";
    }

    private static void DrawSeries(
        DrawingContext context,
        Rect bounds,
        IReadOnlyList<FrameProfileSnapshot> samples,
        FrameTimingDomain domain,
        Pen pen,
        double scaleMilliseconds)
    {
        Point? previous = null;
        var columnWidth = bounds.Width / samples.Count;
        for (var sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
        {
            var duration = DurationFor(samples[sampleIndex], domain);
            if (duration is not > 0 || !double.IsFinite(duration.Value))
            {
                previous = null;
                continue;
            }

            var point = new Point(
                (sampleIndex + 0.5d) * columnWidth,
                bounds.Height - Math.Clamp(duration.Value / scaleMilliseconds, 0d, 1d) * bounds.Height);
            if (previous is { } previousPoint)
                context.DrawLine(pen, previousPoint, point);

            context.DrawEllipse(pen.Brush, null, point, 2.5, 2.5);
            previous = point;
        }
    }

    private static double MaximumDuration(FrameProfileSnapshot sample) =>
        Math.Max(sample.CpuFrameMilliseconds, sample.GpuFrameMilliseconds.GetValueOrDefault());

    private static double? DurationFor(FrameProfileSnapshot sample, FrameTimingDomain domain) =>
        domain == FrameTimingDomain.Cpu
            ? sample.CpuFrameMilliseconds
            : sample.GpuFrameMilliseconds;

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
