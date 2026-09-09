using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Streaming;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class BackgroundResourceQueueSmokeTests
{
    [TestMethod]
    public void FailedResourcePlaceholderIsRetainedUntilItsLastUserLeaves()
    {
        Assert.IsFalse(ResourceCachePolicy.ShouldRemovePlaceholderAfterFailure(hasUsers: true));
        Assert.IsTrue(ResourceCachePolicy.ShouldRemovePlaceholderAfterFailure(hasUsers: false));
    }

    [TestMethod]
    public void ResourceFailuresRetryOnlyWhileResidentAndWithinTheAttemptLimit()
    {
        var failures = new ResourceFailureTracker<int>(maximumAttempts: 3);

        Assert.IsTrue(failures.CanRetry(42, isResident: true));
        Assert.IsTrue(failures.CanRetry(42, isResident: true));
        Assert.IsFalse(failures.CanRetry(42, isResident: true));
        failures.Forget(42);
        Assert.IsTrue(failures.CanRetry(42, isResident: true));
        Assert.IsFalse(failures.CanRetry(7, isResident: false));
    }

    [TestMethod]
    public void QueueProcessesRequestsAndCanRestartAfterStop()
    {
        var queue = new BackgroundResourceQueue<int, int>(value => value * 2);
        try
        {
            queue.Enqueue(21);
            var first = WaitForResult(queue);

            Assert.AreEqual(21, first.Request);
            Assert.AreEqual(42, first.Value);
            Assert.IsNull(first.Error);
            Assert.AreEqual(1, queue.Metrics.Completed);
            Assert.AreEqual(0, queue.Metrics.Failed);

            queue.StopAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, queue.Metrics.Completed);
            queue.Enqueue(7);
            var second = WaitForResult(queue);

            Assert.AreEqual(14, second.Value);
            Assert.IsNull(second.Error);
        }
        finally
        {
            queue.StopAsync().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void QueueReturnsWorkerExceptionsToTheConsumer()
    {
        var queue = new BackgroundResourceQueue<int, int>(
            _ => throw new InvalidOperationException("parse failed"));
        try
        {
            queue.Enqueue(123);
            var result = WaitForResult(queue);

            Assert.AreEqual(123, result.Request);
            Assert.IsInstanceOfType<InvalidOperationException>(result.Error);
            Assert.AreEqual("parse failed", result.Error.Message);
            Assert.AreEqual(1, queue.Metrics.Failed);
            Assert.AreEqual(0, queue.Metrics.Completed);
        }
        finally
        {
            queue.StopAsync().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void QueueCountIncludesTheRequestBeingProcessed()
    {
        using var started = new ManualResetEventSlim();
        using var continueProcessing = new ManualResetEventSlim();
        var queue = new BackgroundResourceQueue<int, int>(value =>
        {
            started.Set();
            continueProcessing.Wait();
            return value;
        });

        try
        {
            queue.Enqueue(5);
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(1, queue.Count);

            continueProcessing.Set();
            _ = WaitForResult(queue);
            Assert.AreEqual(0, queue.Count);
        }
        finally
        {
            continueProcessing.Set();
            queue.StopAsync().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void QueueCanSkipStaleWorkBeforeProcessing()
    {
        var processCount = 0;
        var queue = new BackgroundResourceQueue<int, int>(
            request =>
            {
                Interlocked.Increment(ref processCount);
                return request;
            },
            shouldProcess: _ => false);

        try
        {
            queue.Enqueue(42);
            var result = WaitForResult(queue);

            Assert.IsTrue(result.IsSkipped);
            Assert.AreEqual(42, result.Request);
            Assert.AreEqual(0, processCount);
            Assert.AreEqual(1, queue.Metrics.Skipped);
            Assert.AreEqual(0, queue.Metrics.Completed);
        }
        finally
        {
            queue.StopAsync().GetAwaiter().GetResult();
        }
    }


    private static BackgroundResourceResult<int, int> WaitForResult(
        BackgroundResourceQueue<int, int> queue)
    {
        BackgroundResourceResult<int, int> result = default;
        Assert.IsTrue(SpinWait.SpinUntil(
            () => queue.TryDequeue(out result),
            TimeSpan.FromSeconds(2)));
        return result;
    }
}
