namespace CodexQuotaHud.App.Infrastructure;

internal sealed class CodexRunningCoordinator : IAsyncDisposable
{
    private readonly Func<bool, CancellationToken, Task> _setRunningAsync;
    private readonly Func<Task> _resetQuotaClientAsync;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _sync = new();
    private readonly List<Waiter> _waiters = [];
    private Task? _worker;
    private bool _desiredState;
    private long _desiredVersion;
    private long _appliedVersion;
    private bool _disposed;

    public CodexRunningCoordinator(
        Func<bool, CancellationToken, Task> setRunningAsync,
        Func<Task> resetQuotaClientAsync)
    {
        _setRunningAsync =
            setRunningAsync ?? throw new ArgumentNullException(nameof(setRunningAsync));
        _resetQuotaClientAsync =
            resetQuotaClientAsync ??
            throw new ArgumentNullException(nameof(resetQuotaClientAsync));
    }

    public Exception? LastError { get; private set; }

    public Task SetDesiredStateAsync(bool isRunning)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _desiredState = isRunning;
            var version = ++_desiredVersion;
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add(new Waiter(version, completion));
            if (_worker is null || _worker.IsCompleted)
            {
                _worker = Task.Run(RunAsync);
            }

            return completion.Task;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? worker;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping.Cancel();
            worker = _worker;
        }

        if (worker is not null)
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (_stopping.IsCancellationRequested)
            {
            }
        }

        lock (_sync)
        {
            foreach (var waiter in _waiters)
            {
                waiter.Completion.TrySetCanceled(_stopping.Token);
            }

            _waiters.Clear();
        }

        _stopping.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync()
    {
        while (true)
        {
            bool desiredState;
            long version;
            lock (_sync)
            {
                if (_disposed)
                {
                    _worker = null;
                    return;
                }

                if (_appliedVersion == _desiredVersion)
                {
                    _worker = null;
                    return;
                }

                desiredState = _desiredState;
                version = _desiredVersion;
            }

            try
            {
                await _setRunningAsync(desiredState, _stopping.Token)
                    .ConfigureAwait(false);
                if (!desiredState)
                {
                    await _resetQuotaClientAsync().ConfigureAwait(false);
                }

                LastError = null;
            }
            catch (OperationCanceledException)
                when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LastError = exception;
            }

            CompleteThrough(version);
        }
    }

    private void CompleteThrough(long version)
    {
        List<TaskCompletionSource> completions;
        lock (_sync)
        {
            _appliedVersion = Math.Max(_appliedVersion, version);
            completions = _waiters
                .Where(waiter => waiter.Version <= version)
                .Select(waiter => waiter.Completion)
                .ToList();
            _waiters.RemoveAll(waiter => waiter.Version <= version);
        }

        foreach (var completion in completions)
        {
            completion.TrySetResult();
        }
    }

    private sealed record Waiter(
        long Version,
        TaskCompletionSource Completion);
}
