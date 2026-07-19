using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class AppLifecycleTests
{
    [Fact]
    public async Task Repeated_shutdown_requests_share_one_cleanup()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ShutdownCoordinator();
        var cleanupCount = 0;

        var first = coordinator.RequestAsync(async () =>
        {
            Interlocked.Increment(ref cleanupCount);
            await release.Task;
        });
        var second = coordinator.RequestAsync(() => throw new InvalidOperationException("must not run"));

        Assert.Same(first, second);
        Assert.Equal(1, Volatile.Read(ref cleanupCount));

        release.SetResult();
        await first;
    }

    [Fact]
    public async Task Reentrant_shutdown_request_reuses_the_in_flight_cleanup()
    {
        var coordinator = new ShutdownCoordinator();
        var cleanupCount = 0;
        Task? reentrant = null;

        var shutdown = coordinator.RequestAsync(() =>
        {
            Interlocked.Increment(ref cleanupCount);
            reentrant = coordinator.RequestAsync(() =>
            {
                Interlocked.Increment(ref cleanupCount);
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        });

        await shutdown;
        Assert.Same(shutdown, reentrant);
        Assert.Equal(1, cleanupCount);
    }

    [Fact]
    public async Task Background_shutdown_cancels_and_awaits_the_task()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<Exception>();
        var background = Task.Run(async () =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
        });
        await started.Task;

        await BackgroundTaskShutdown.CancelAndWaitAsync(
            cancellation,
            background,
            TimeSpan.FromSeconds(1),
            errors.Add);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(background.IsCompleted);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Background_shutdown_reports_a_cancellation_callback_fault()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("cancellation callback failed");
        using var registration = cancellation.Token.Register(() => throw expected);
        var errors = new List<Exception>();

        await BackgroundTaskShutdown.CancelAndWaitAsync(
            cancellation,
            Task.CompletedTask,
            TimeSpan.FromSeconds(1),
            errors.Add);

        Assert.Collection(errors, error => Assert.Same(expected, error));
    }

    [Fact]
    public async Task Background_shutdown_reports_a_terminal_fault()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("bridge failed");
        var errors = new List<Exception>();

        await BackgroundTaskShutdown.CancelAndWaitAsync(
            cancellation,
            Task.FromException(expected),
            TimeSpan.FromSeconds(1),
            errors.Add);

        Assert.Collection(errors, error => Assert.Same(expected, error));
    }

    [Fact]
    public async Task Timed_out_background_shutdown_observes_a_late_fault()
    {
        using var cancellation = new CancellationTokenSource();
        var background = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = new List<Exception>();
        var lateReport = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await BackgroundTaskShutdown.CancelAndWaitAsync(
            cancellation,
            background.Task,
            TimeSpan.FromMilliseconds(20),
            error =>
            {
                lock (reports) reports.Add(error);
                if (error is not TimeoutException) lateReport.TrySetResult(error);
            });

        Assert.IsType<TimeoutException>(Assert.Single(reports));
        var expected = new InvalidOperationException("late bridge failure");
        background.SetException(expected);
        Assert.Same(expected, await lateReport.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Unknown_dispatcher_exception_requires_termination()
    {
        Assert.True(AppFailurePolicy.MustTerminate(new InvalidOperationException("unexpected")));
    }
}
