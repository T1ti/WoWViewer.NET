namespace WTEditor.Avalonia.Rendering;

/// <summary>
/// Selects the renderer cadence for the viewport's current presentation state.
/// The foreground limit is an upper bound: frames are still rendered only from
/// Avalonia composition callbacks, so the renderer cannot get ahead of presentation.
/// </summary>
public enum ViewportRenderActivity
{
    Foreground,
    Background,
    Suspended
}

internal static class ViewportFrameRatePolicy
{
    public const int DefaultForegroundFramesPerSecond = 60;
    public const int MinimumForegroundFramesPerSecond = 30;
    public const int MaximumForegroundFramesPerSecond = 360;

    public static int NormalizeForegroundFramesPerSecond(int framesPerSecond) =>
        Math.Clamp(
            framesPerSecond,
            MinimumForegroundFramesPerSecond,
            MaximumForegroundFramesPerSecond);

    public static double? GetFrameIntervalSeconds(
        int foregroundFramesPerSecond,
        ViewportRenderActivity activity,
        bool isForegroundFrameRateLimitEnabled)
    {
        if (activity != ViewportRenderActivity.Suspended &&
            !isForegroundFrameRateLimitEnabled)
        {
            return null;
        }

        var framesPerSecond = activity == ViewportRenderActivity.Suspended
            ? 1
            : NormalizeForegroundFramesPerSecond(foregroundFramesPerSecond);

        return 1d / framesPerSecond;
    }
}

/// <summary>
/// Tracks frame deadlines separately from presentation-buffer availability.
/// A due frame only consumes its deadline after it has actually been presented.
/// </summary>
internal sealed class ViewportFrameClock
{
    private readonly double _dueToleranceSeconds;
    private double _nextFrameDueSeconds;

    public ViewportFrameClock(double dueToleranceSeconds)
    {
        if (!double.IsFinite(dueToleranceSeconds) || dueToleranceSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(dueToleranceSeconds));

        _dueToleranceSeconds = dueToleranceSeconds;
    }

    public void Reset() => _nextFrameDueSeconds = 0;

    public bool IsFrameDue(double nowSeconds, double? frameIntervalSeconds)
    {
        if (frameIntervalSeconds == null)
        {
            // Disabling the cap must discard a deadline left by the previously
            // capped cadence instead of delaying even one uncapped frame.
            Reset();
            return true;
        }

        return _nextFrameDueSeconds <= 0 ||
               nowSeconds + _dueToleranceSeconds >= _nextFrameDueSeconds;
    }

    public void MarkFramePresented(double nowSeconds, double? frameIntervalSeconds)
    {
        if (frameIntervalSeconds == null)
        {
            Reset();
            return;
        }

        var frameInterval = frameIntervalSeconds.Value;
        if (_nextFrameDueSeconds <= 0 ||
            nowSeconds - _nextFrameDueSeconds >= frameInterval)
        {
            _nextFrameDueSeconds = nowSeconds + frameInterval;
        }
        else
        {
            // Stay phase-locked to the prior target. Re-basing every slightly
            // early composition callback can reduce the effective frame rate.
            _nextFrameDueSeconds += frameInterval;
        }
    }
}
