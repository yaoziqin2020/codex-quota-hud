using System.Windows.Input;

namespace CodexQuotaHud.SkinDesigner.UI;

public sealed class AsyncRelayCommand : ICommand, IDisposable
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly SynchronizationContext? _context;
    private readonly Func<CancellationTokenSource> _createCancellation;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private int _running;
    private int _disposed;

    public AsyncRelayCommand(
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null)
        : this(
            execute,
            canExecute,
            SynchronizationContext.Current,
            static () => new CancellationTokenSource())
    {
    }

    internal AsyncRelayCommand(
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute,
        SynchronizationContext? context,
        Func<CancellationTokenSource> createCancellation)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _context = context;
        _createCancellation = createCancellation ??
            throw new ArgumentNullException(nameof(createCancellation));
    }

    public event EventHandler? CanExecuteChanged;

    public event EventHandler<Exception>? ExecutionFailed;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public Exception? LastException { get; private set; }

    public bool CanExecute(object? parameter) =>
        Volatile.Read(ref _disposed) == 0 &&
        !IsRunning &&
        (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                LastException = exception;
                TryPublishExecutionFailure(exception);
            }
        }
    }

    public async Task ExecuteAsync()
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_running != 0 || !(_canExecute?.Invoke() ?? true))
            {
                return;
            }

            cancellation = _createCancellation();
            _cancellation = cancellation;
            Volatile.Write(ref _running, 1);
        }

        TryRaiseCanExecuteChanged();
        try
        {
            await _execute(cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            bool publishAvailability;
            lock (_gate)
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                }

                Volatile.Write(ref _running, 0);
                publishAvailability = _disposed == 0;
            }

            cancellation.Dispose();
            if (publishAvailability)
            {
                TryRaiseCanExecuteChanged();
            }
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _cancellation;
        }

        TryCancel(cancellation);
    }

    public void NotifyCanExecuteChanged()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            TryRaiseCanExecuteChanged();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed != 0)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
            cancellation = _cancellation;
        }

        TryCancel(cancellation);
    }

    private void TryRaiseCanExecuteChanged()
    {
        void Raise()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // A UI notification is advisory and must never replace the
                // command's execution result.
            }
        }

        try
        {
            if (_context is null ||
                ReferenceEquals(SynchronizationContext.Current, _context))
            {
                Raise();
            }
            else
            {
                _context.Post(_ => Raise(), null);
            }
        }
        catch
        {
            // Dispatcher shutdown or a failing SynchronizationContext cannot
            // change the outcome of the command operation.
        }
    }

    private void TryPublishExecutionFailure(Exception exception)
    {
        try
        {
            ExecutionFailed?.Invoke(this, exception);
        }
        catch
        {
            // Failure observers are isolated from the original exception.
        }
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion won the race after the reference was observed.
        }
    }
}
