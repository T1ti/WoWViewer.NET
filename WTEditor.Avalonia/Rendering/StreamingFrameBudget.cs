namespace WTEditor.Avalonia.Rendering;

internal static class StreamingFrameBudget
{
    private const double DefaultTargetFrameMilliseconds = 10d;
    private const double MaximumSynchronousWorkMilliseconds = 10d;

    public static double CalculateMilliseconds(
        double? frameIntervalSeconds,
        double elapsedBeforeRenderMilliseconds,
        double estimatedRenderMilliseconds)
    {
        var targetMilliseconds = frameIntervalSeconds is > 0d &&
                                 double.IsFinite(frameIntervalSeconds.Value)
            ? frameIntervalSeconds.Value * 1000d
            : DefaultTargetFrameMilliseconds;
        targetMilliseconds = Math.Max(1d, targetMilliseconds);

        elapsedBeforeRenderMilliseconds = double.IsFinite(elapsedBeforeRenderMilliseconds)
            ? Math.Max(0d, elapsedBeforeRenderMilliseconds)
            : 0d;
        estimatedRenderMilliseconds = double.IsFinite(estimatedRenderMilliseconds)
            ? Math.Max(0d, estimatedRenderMilliseconds)
            : 0d;

        var safetyReserve = Math.Max(0.75d, targetMilliseconds * 0.1d);
        var maximumStreamingShare = Math.Min(
            MaximumSynchronousWorkMilliseconds,
            Math.Max(0d, targetMilliseconds - safetyReserve));
        var available = targetMilliseconds -
                        elapsedBeforeRenderMilliseconds -
                        estimatedRenderMilliseconds -
                        safetyReserve;

        // If the rest of the frame has already spent the deadline, leave GPU
        // publication queued for the next frame instead of forcing a spike.
        return Math.Clamp(available, 0d, maximumStreamingShare);
    }
}
