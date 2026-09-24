namespace WoWRenderLib.Structs;

/// <summary>
/// Integrates an emitter's nonnegative rate (particles/second) and inverts
/// half-integer birth thresholds without replaying previous render frames.
/// </summary>
internal sealed class M2ParticleEmissionSchedule
{
    private readonly record struct Segment(
        double Start, double End, double RateStart, double RateEnd,
        double AreaStart, double AreaEnd);

    private readonly Segment[] _segments;
    private readonly double _period;
    private readonly double _areaPerPeriod;
    private readonly double _heldRate;
    private readonly bool _loop;
    private readonly bool _constant;

    private M2ParticleEmissionSchedule(
        Segment[] segments, double period, double areaPerPeriod,
        double heldRate, bool loop, bool constant)
    {
        _segments = segments;
        _period = period;
        _areaPerPeriod = areaPerPeriod;
        _heldRate = heldRate;
        _loop = loop;
        _constant = constant;
    }

    public static bool HasSupportedKeys(M2Track<float> track) =>
        track.Timelines.All(timeline =>
        {
            if (timeline.Times.Length != timeline.Values.Length)
                return false;
            for (var i = 0; i < timeline.Values.Length; i++)
            {
                if (!float.IsFinite(timeline.Values[i]) ||
                    (i > 0 && timeline.Times[i] < timeline.Times[i - 1]))
                    return false;
            }
            return true;
        });

    public static M2ParticleEmissionSchedule? Create(
        M2Track<float> track, int sequenceIndex, M2Sequence sequence,
        ReadOnlySpan<uint> globalLoops)
    {
        var timelineIndex = track.GlobalSequence >= 0 ? 0 : sequenceIndex;
        if ((uint)timelineIndex >= track.Timelines.Length)
            return null;
        var timeline = track.Timelines[timelineIndex];
        if (timeline.Times.Length == 0)
            return null;
        var times = timeline.Times;
        var values = timeline.Values;
        if (times.Length == 1)
        {
            var rate = Math.Max(0d, values[0]);
            return new([], 0d, 0d, rate, false, true);
        }

        var period = track.GlobalSequence >= 0
            ? track.GlobalSequence < globalLoops.Length
                ? globalLoops[track.GlobalSequence] : 0u
            : sequence.Duration;
        if (period == 0)
            period = times[^1];
        if (period == 0)
            return new([], 0d, 0d, Math.Max(0d, values[0]), false, true);

        var segments = new List<Segment>(times.Length + 2);
        double area = 0d;
        var firstEnd = Math.Min((double)period, times[0]);
        Add(0d, firstEnd, values[0], values[0]);
        for (var i = 0; i < times.Length - 1 && times[i] < period; i++)
        {
            if (times[i + 1] == times[i])
                continue;
            var start = (double)times[i];
            var end = Math.Min((double)times[i + 1], period);
            var endRate = track.Interpolation == 0 ? values[i] :
                values[i] + ((double)values[i + 1] - values[i]) *
                (end - start) / (times[i + 1] - times[i]);
            Add(start, end, values[i], endRate);
        }
        if (times[^1] < period)
            Add(times[^1], period, values[^1], values[^1]);

        var loop = track.GlobalSequence >= 0 || (sequence.Flags & 1) == 0;
        var heldRate = Math.Max(0d, track.Sample(sequenceIndex, sequence,
            globalLoops, period, 0f, float.Lerp));
        return new([.. segments], period, area, heldRate, loop, false);

        void Add(double start, double end, double rateStart, double rateEnd)
        {
            if (end <= start)
                return;
            if (rateStart < 0d && rateEnd > 0d ||
                rateStart > 0d && rateEnd < 0d)
            {
                var crossing = start + (end - start) *
                    (-rateStart) / (rateEnd - rateStart);
                Add(start, crossing, rateStart, 0d);
                Add(crossing, end, 0d, rateEnd);
                return;
            }
            rateStart = Math.Max(0d, rateStart);
            rateEnd = Math.Max(0d, rateEnd);
            var next = area + (rateStart + rateEnd) * 0.5d *
                (end - start) / 1000d;
            segments.Add(new(start, end, rateStart, rateEnd, area, next));
            area = next;
        }
    }

    public double BirthCountBy(double timeMilliseconds)
    {
        if (timeMilliseconds <= 0d)
            return 0d;
        if (_constant)
            return _heldRate * timeMilliseconds / 1000d;
        if (_loop)
        {
            var cycles = Math.Floor(timeMilliseconds / _period);
            return cycles * _areaPerPeriod +
                AreaWithin(timeMilliseconds - cycles * _period);
        }
        return timeMilliseconds <= _period
            ? AreaWithin(timeMilliseconds)
            : _areaPerPeriod + _heldRate *
                (timeMilliseconds - _period) / 1000d;
    }

    public double BirthTime(long index)
    {
        var threshold = index + 0.5d;
        if (_constant)
            return _heldRate > 0d ? threshold * 1000d / _heldRate : double.NaN;
        if (_loop)
        {
            if (_areaPerPeriod <= 0d)
                return double.NaN;
            var cycles = Math.Floor(threshold / _areaPerPeriod);
            return cycles * _period + TimeWithin(threshold - cycles * _areaPerPeriod);
        }
        if (threshold <= _areaPerPeriod)
            return TimeWithin(threshold);
        return _heldRate > 0d
            ? _period + (threshold - _areaPerPeriod) * 1000d / _heldRate
            : double.NaN;
    }

    private double AreaWithin(double time)
    {
        foreach (var segment in _segments)
        {
            if (time > segment.End)
                continue;
            var elapsed = Math.Clamp(time - segment.Start, 0d,
                segment.End - segment.Start);
            var slope = (segment.RateEnd - segment.RateStart) /
                (segment.End - segment.Start);
            return segment.AreaStart +
                (segment.RateStart * elapsed +
                 0.5d * slope * elapsed * elapsed) / 1000d;
        }
        return _areaPerPeriod;
    }

    private double TimeWithin(double target)
    {
        if (target <= 0d)
            return 0d;
        foreach (var segment in _segments)
        {
            if (target > segment.AreaEnd ||
                segment.AreaEnd <= segment.AreaStart)
                continue;
            var remaining = target - segment.AreaStart;
            var slope = (segment.RateEnd - segment.RateStart) /
                (segment.End - segment.Start);
            var root = Math.Sqrt(Math.Max(0d,
                segment.RateStart * segment.RateStart +
                2000d * slope * remaining));
            var denominator = segment.RateStart + root;
            var elapsed = denominator > 0d
                ? 2000d * remaining / denominator
                : 0d;
            return segment.Start + Math.Clamp(elapsed, 0d,
                segment.End - segment.Start);
        }
        return _period;
    }
}
