namespace CodexQuotaHud.App.Infrastructure;

internal sealed class InstalledAppShutdownListener : IDisposable
{
    internal const string EventName =
        @"Local\CodexQuotaHud.ShutdownRequested";

    private readonly EventWaitHandle _shutdownEvent;
    private readonly ManualResetEvent _stopEvent = new(false);
    private readonly Thread _thread;
    private readonly Action _requestExit;
    private int _disposed;

    public InstalledAppShutdownListener(Action requestExit)
        : this(EventName, requestExit)
    {
    }

    internal InstalledAppShutdownListener(string eventName, Action requestExit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
        _shutdownEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            eventName);
        _thread = new Thread(Listen)
        {
            IsBackground = true,
            Name = "CodexQuotaHud.ShutdownListener"
        };
        _thread.Start();
    }

    public static bool TrySignal() => TrySignal(EventName);

    internal static bool TrySignal(string eventName)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(eventName, out var shutdownEvent))
            {
                return false;
            }

            using (shutdownEvent)
            {
                shutdownEvent.Set();
                return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopEvent.Set();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _shutdownEvent.Dispose();
        _stopEvent.Dispose();
    }

    private void Listen()
    {
        try
        {
            while (WaitHandle.WaitAny([_shutdownEvent, _stopEvent]) == 0)
            {
                try
                {
                    _requestExit();
                }
                catch
                {
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
