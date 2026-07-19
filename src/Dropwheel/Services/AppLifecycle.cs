namespace Dropwheel.Services;

internal static class AppFailurePolicy
{
    public static bool MustTerminate(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return true;
    }
}

internal sealed class ShutdownCoordinator
{
    private readonly object _gate = new();
    private Task? _shutdown;

    public Task RequestAsync(Func<Task> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_shutdown is not null) return _shutdown;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdown = completion.Task;
        }

        _ = CompleteAsync(cleanup, completion);
        return completion.Task;
    }

    private static async Task CompleteAsync(Func<Task> cleanup, TaskCompletionSource completion)
    {
        try
        {
            await cleanup();
            completion.SetResult();
        }
        catch (OperationCanceledException ex)
        {
            completion.SetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
    }
}

internal static class BackgroundTaskShutdown
{
    public static async Task CancelAndWaitAsync(
        CancellationTokenSource? cancellation,
        Task? task,
        TimeSpan timeout,
        Action<Exception> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            cancellation?.Cancel();
        }
        catch (Exception ex)
        {
            report(ex.GetBaseException());
        }
        if (task is null) return;

        try
        {
            await task.WaitAsync(timeout);
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
        }
        catch (TimeoutException ex)
        {
            report(ex);
            ObserveFault(task, report);
        }
        catch (Exception ex)
        {
            report(ex);
        }
    }

    private static void ObserveFault(Task task, Action<Exception> report) =>
        _ = task.ContinueWith(
            static (completed, state) =>
                ((Action<Exception>)state!)(completed.Exception!.GetBaseException()),
            report,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
