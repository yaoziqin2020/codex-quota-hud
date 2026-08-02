using System.Diagnostics;
using System.IO;

namespace CodexQuotaHud.App.Infrastructure.LocalControl;

public sealed class LocalControlServer : IAsyncDisposable
{
    private readonly object _activeHandlersSync = new();
    private readonly HashSet<Task> _activeHandlers = [];
    private readonly object _sync = new();
    private readonly string _pipeName;
    private readonly Func<LocalControlRequest, CancellationToken, Task<LocalControlResponse>> _handle;
    private readonly ILocalControlPipeFactory _pipes;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;
    private int _disposed;

    public LocalControlServer(
        string pipeName,
        Func<LocalControlRequest, CancellationToken, Task<LocalControlResponse>> handle,
        ILocalControlPipeFactory? pipes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        _pipes = pipes ?? new CurrentUserLocalControlPipeFactory();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        lock (_sync)
        {
            _loop ??= Task.Run(() => AcceptLoopAsync(_stopping.Token));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopping.Cancel();
        using var shutdownDeadline = new CancellationTokenSource(
            LocalControlProtocol.ResponseTimeout);
        Task? loop;
        lock (_sync)
        {
            loop = _loop;
        }

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(shutdownDeadline.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                shutdownDeadline.IsCancellationRequested)
            {
            }
        }

        if (!shutdownDeadline.IsCancellationRequested)
        {
            try
            {
                await DrainActiveHandlersAsync()
                    .WaitAsync(shutdownDeadline.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                shutdownDeadline.IsCancellationRequested)
            {
            }
        }

        lock (_activeHandlersSync)
        {
            _activeHandlers.Clear();
        }

        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Stream? connection = null;
            try
            {
                connection = await _pipes.AcceptAsync(
                        _pipeName,
                        cancellationToken)
                    .ConfigureAwait(false);
                await ProcessConnectionAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Trace.TraceWarning(
                    "Local-control connection failed: {0}",
                    exception.GetType().Name);
            }
            finally
            {
                if (connection is not null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private async Task ProcessConnectionAsync(
        Stream connection,
        CancellationToken serverCancellationToken)
    {
        using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(
            serverCancellationToken);
        requestDeadline.CancelAfter(LocalControlTimeBudget.HandlerCommitWindow);

        LocalControlResponse response;
        Task<LocalControlResponse>? handlerTask = null;
        try
        {
            var request = await LocalControlProtocol.ReadRequestAsync(
                    connection,
                    requestDeadline.Token)
                .ConfigureAwait(false);
            handlerTask = TrackHandler(Task.Run(
                () => _handle(request, requestDeadline.Token),
                CancellationToken.None));
            response = await handlerTask
                .WaitAsync(requestDeadline.Token)
                .ConfigureAwait(false);
            if (!response.Succeeded)
            {
                requestDeadline.Token.ThrowIfCancellationRequested();
            }
        }
        catch (LocalControlProtocolException exception)
        {
            response = Failure(exception.ErrorCode, "The request is invalid.");
        }
        catch (OperationCanceledException) when (
            serverCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException) when (requestDeadline.IsCancellationRequested)
        {
            response = handlerTask is null
                ? Failure(
                    "control.timeout",
                    "The local-control request timed out.")
                : await TryResolveCommittedSuccessAsync(handlerTask)
                    .ConfigureAwait(false) ?? Failure(
                        "control.timeout",
                        "The local-control request timed out.");
        }
        catch (Exception)
        {
            response = Failure(
                "control.handler.failed",
                "The local-control handler failed.");
        }

        using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(
            serverCancellationToken);
        writeDeadline.CancelAfter(LocalControlTimeBudget.ResponseWriteWindow);
        await LocalControlProtocol.WriteResponseAsync(
                connection,
                response,
                writeDeadline.Token)
            .ConfigureAwait(false);
    }

    private static async Task<LocalControlResponse?> TryResolveCommittedSuccessAsync(
        Task<LocalControlResponse> handlerTask)
    {
        using var arbitrationDeadline = new CancellationTokenSource(
            LocalControlTimeBudget.CommitOutcomeArbitration);
        try
        {
            var response = await handlerTask
                .WaitAsync(arbitrationDeadline.Token)
                .ConfigureAwait(false);
            return response.Succeeded ? response : null;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or TimeoutException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Task<LocalControlResponse> TrackHandler(
        Task<LocalControlResponse> handlerTask)
    {
        ArgumentNullException.ThrowIfNull(handlerTask);
        lock (_activeHandlersSync)
        {
            _activeHandlers.RemoveWhere(static task => task.IsCompleted);
            _activeHandlers.Add(handlerTask);
        }

        _ = handlerTask.ContinueWith(
            static completed =>
            {
                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously |
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        return handlerTask;
    }

    private async Task DrainActiveHandlersAsync()
    {
        Task[] handlers;
        lock (_activeHandlersSync)
        {
            handlers = [.. _activeHandlers];
        }

        try
        {
            await Task.WhenAll(handlers).ConfigureAwait(false);
        }
        catch (Exception)
        {
            foreach (var handler in handlers)
            {
                if (handler.IsFaulted)
                {
                    _ = handler.Exception;
                }
            }
        }
    }

    private static LocalControlResponse Failure(string errorCode, string message) =>
        new(false, errorCode, message);
}
