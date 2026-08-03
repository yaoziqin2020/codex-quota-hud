namespace CodexQuotaHud.App.UI.About;

internal sealed class AboutWindowCoordinator : IDisposable
{
    private readonly Func<IAboutWindow> _createWindow;
    private readonly Action<string> _reportError;
    private IAboutWindow? _window;
    private int _disposed;

    internal AboutWindowCoordinator()
        : this(
            () => new AboutWindow(AboutInformation.Current),
            ShowErrorMessage)
    {
    }

    internal AboutWindowCoordinator(Func<IAboutWindow> createWindow)
        : this(createWindow, _ => { })
    {
    }

    internal AboutWindowCoordinator(
        Func<IAboutWindow> createWindow,
        Action<string> reportError)
    {
        _createWindow = createWindow ?? throw new ArgumentNullException(
            nameof(createWindow));
        _reportError = reportError ?? throw new ArgumentNullException(
            nameof(reportError));
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        try
        {
            if (_window is not null)
            {
                _ = _window.Activate();
                return;
            }

            var window = _createWindow() ?? throw new InvalidOperationException(
                "The About window factory returned null.");
            _window = window;
            window.Closed += OnWindowClosed;
            window.Show();
        }
        catch (Exception exception)
        {
            ResetFailedWindow();
            ReportFailure(exception);
        }
    }

    private void ResetFailedWindow()
    {
        var window = _window;
        _window = null;
        if (window is null)
        {
            return;
        }

        window.Closed -= OnWindowClosed;
        try
        {
            window.Close();
        }
        catch
        {
            // The About window is optional UI; cleanup failure must not crash
            // the HUD or Skin Designer either.
        }
    }

    private void ReportFailure(Exception exception)
    {
        var message = $"无法打开“关于”窗口。\n\n{exception.Message}";
        try
        {
            _reportError(message);
        }
        catch
        {
            // Error reporting is best-effort and must remain non-fatal.
        }
    }

    private static void ShowErrorMessage(string message) =>
        _ = System.Windows.MessageBox.Show(
            message,
            "Codex Quota HUD",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);

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
