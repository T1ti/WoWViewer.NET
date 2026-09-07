using System.Diagnostics;
using System.Threading.Channels;

namespace WoWRenderLib.DX11.Streaming;

internal static class ResourceCachePolicy
{
    public static bool ShouldRemovePlaceholderAfterFailure(bool hasUsers) => !hasUsers;
}

internal readonly record struct BackgroundResourceResult<TRequest, TResult>(
    TRequest Request,
    TResult Value,
    Exception? Error,
    bool IsSkipped = false);

internal sealed class BackgroundResourceQueue<TRequest, TResult>(
    Func<TRequest, TResult> process,
    int bufferedResultCount = 4,
    int? bufferedRequestCount = null,
    Func<TRequest, bool>? shouldProcess = null)
{
    private readonly Lock _lifecycleLock = new();
    private Channel<TRequest>? _requests;
    private Channel<BackgroundResourceResult<TRequest, TResult>>? _results;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private int _activeRequestCount;
    private long _completedCount;
    private long _skippedCount;
    private long _failedCount;
    private long _lastProcessingTicks;
    private long _maximumProcessingTicks;

    public int Count
    {
        get
        {
            lock (_lifecycleLock)
                return GetCount(_requests?.Reader) +
                       GetCount(_results?.Reader) +
                       Volatile.Read(ref _activeRequestCount);
        }
    }

    public AssetPipelineMetrics Metrics => new(
        Count,
        Volatile.Read(ref _activeRequestCount),
        Interlocked.Read(ref _completedCount),
        Interlocked.Read(ref _skippedCount),
        Interlocked.Read(ref _failedCount),
        ToMilliseconds(Interlocked.Read(ref _lastProcessingTicks)),
        ToMilliseconds(Interlocked.Read(ref _maximumProcessingTicks)));

    public void Enqueue(TRequest request)
    {
        lock (_lifecycleLock)
        {
            EnsureStarted();
            if (_requests?.Writer.TryWrite(request) != true)
                throw new InvalidOperationException("The background resource queue is not accepting work.");
        }
    }

    public bool TryDequeue(out BackgroundResourceResult<TRequest, TResult> result)
    {
        lock (_lifecycleLock)
        {
            if (_results?.Reader.TryRead(out result) == true)
                return true;
        }

        result = default;
        return false;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? worker;
        Channel<TRequest>? requests;

        lock (_lifecycleLock)
        {
            cancellation = _cancellation;
            worker = _worker;
            requests = _requests;
            _cancellation = null;
            _worker = null;
            _requests = null;
            _results = null;
        }

        if (cancellation == null)
            return;

        requests?.Writer.TryComplete();
        try
        {
            cancellation.Cancel();
            if (worker != null)
                await worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
            ResetMetrics();
        }
    }

    private void EnsureStarted()
    {
        if (_worker is { IsCompleted: false })
            return;

        _cancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        var requests = CreateRequestChannel();
        var results = Channel.CreateBounded<BackgroundResourceResult<TRequest, TResult>>(
            new BoundedChannelOptions(Math.Max(1, bufferedResultCount))
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        _cancellation = cancellation;
        _requests = requests;
        _results = results;
        _worker = Task.Run(() => ProcessAsync(
            requests.Reader,
            results.Writer,
            cancellation.Token));
    }

    private Channel<TRequest> CreateRequestChannel()
    {
        if (bufferedRequestCount is > 0)
        {
            return Channel.CreateBounded<TRequest>(new BoundedChannelOptions(bufferedRequestCount.Value)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        return Channel.CreateUnbounded<TRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    private async Task ProcessAsync(
        ChannelReader<TRequest> requests,
        ChannelWriter<BackgroundResourceResult<TRequest, TResult>> results,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in requests.ReadAllAsync(cancellationToken))
            {
                Interlocked.Increment(ref _activeRequestCount);
                try
                {
                    BackgroundResourceResult<TRequest, TResult> result;
                    if (shouldProcess?.Invoke(request) == false)
                    {
                        Interlocked.Increment(ref _skippedCount);
                        result = new(request, default!, null, IsSkipped: true);
                    }
                    else try
                    {
                        var started = Stopwatch.GetTimestamp();
                        try
                        {
                            result = new(request, process(request), null);
                            Interlocked.Increment(ref _completedCount);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            Interlocked.Increment(ref _failedCount);
                            result = new(request, default!, exception);
                        }
                        finally
                        {
                            RecordProcessingDuration(Stopwatch.GetTimestamp() - started);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await results.WriteAsync(result, cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeRequestCount);
                }
            }
        }
        finally
        {
            results.TryComplete();
        }
    }

    private static int GetCount<T>(ChannelReader<T>? reader) =>
        reader is { CanCount: true } ? reader.Count : 0;

    private void RecordProcessingDuration(long elapsedTicks)
    {
        Interlocked.Exchange(ref _lastProcessingTicks, elapsedTicks);
        var currentMaximum = Interlocked.Read(ref _maximumProcessingTicks);
        while (elapsedTicks > currentMaximum)
        {
            var observed = Interlocked.CompareExchange(
                ref _maximumProcessingTicks,
                elapsedTicks,
                currentMaximum);
            if (observed == currentMaximum)
                return;
            currentMaximum = observed;
        }
    }

    private static double ToMilliseconds(long stopwatchTicks) =>
        stopwatchTicks * 1000d / Stopwatch.Frequency;

    private void ResetMetrics()
    {
        Interlocked.Exchange(ref _completedCount, 0);
        Interlocked.Exchange(ref _skippedCount, 0);
        Interlocked.Exchange(ref _failedCount, 0);
        Interlocked.Exchange(ref _lastProcessingTicks, 0);
        Interlocked.Exchange(ref _maximumProcessingTicks, 0);
    }
}
