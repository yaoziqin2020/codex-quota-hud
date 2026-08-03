namespace CodexQuotaHud.App.UI.About;

internal sealed class AboutWindowCoordinator : IDisposable
{
    private readonly Func<IAboutWindow> _createWindow;
    private IAboutWindow? _window;
    private int _disposed;

    internal AboutWindowCoordinator()
        : this(() => new AboutWindow(AboutInformation.Current))
    {
    }

    internal AboutWindowCoordinator(Func<IAboutWindow> createWindow)
    {
        _createWindow = createWindow ?? throw new ArgumentNullException(
            nameof(createWindow));
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        if (_window is not null)
        {
            _ = _window.Activate();
            return;
        }

        var window = _createWindow() ?? throw new InvalidOperationException(
            "The About window factory returned null.");
        _window = window;
        window.Closed += OnWindowClosed;
        try
        {
            window.Show();
        }
        catch
        {
            window.Closed -= OnWindowClosed;
            _window = null;
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var window = _window;
        _window = null;
        if (window is not null)
        {
            window.Closed -= OnWindowClosed;
            window.Close();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _window))
        {
            return;
        }

        _window!.Closed -= OnWindowClosed;
        _window = null;
    }
}
