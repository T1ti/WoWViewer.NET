namespace WTEditor.Avalonia.Rendering;

internal static class StreamingFrameBudget
{
    private const double DefaultTargetFrameMilliseconds = 10d;
    private const double MaximumSynchronousWorkMilliseconds = 10d;
    private const double MinimumProgressMilliseconds = 1d;

    public static double CalculateMilliseconds(
        double? frameIntervalSeconds,
        double elapsedBeforeRenderMilliseconds,
        double estimatedRenderMilliseconds,
        bool hasPendingWork = false)
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

        var budget = Math.Clamp(available, 0d, maximumStreamingShare);
        if (!hasPendingWork)
            return budget;

        // A consistently over-budget renderer must not reduce streaming to zero
        // forever. One small progress slice keeps bounded result channels draining
        // and prevents expensive M2 submission from deadlocking world loading.
        return Math.Max(budget, Math.Min(MinimumProgressMilliseconds, maximumStreamingShare));
    }
}
