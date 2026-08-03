using CodexQuotaHud.App.UI.About;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class AboutWindowCoordinatorTests
{
    [Fact]
    public void Show_ReusesOpenWindowAndReopensAfterClose()
    {
        var created = new List<FakeAboutWindow>();
        using var sut = new AboutWindowCoordinator(() =>
        {
            var window = new FakeAboutWindow();
            created.Add(window);
            return window;
        });

        sut.Show();
        sut.Show();

        Assert.Single(created);
        Assert.Equal(1, created[0].ShowCalls);
        Assert.Equal(1, created[0].ActivateCalls);

        created[0].RaiseClosed();
        sut.Show();

        Assert.Equal(2, created.Count);
        Assert.Equal(1, created[1].ShowCalls);
    }

    [Fact]
    public void Dispose_ClosesCurrentWindowOnceAndIsIdempotent()
    {
        var window = new FakeAboutWindow();
        var sut = new AboutWindowCoordinator(() => window);
        sut.Show();

        sut.Dispose();
        sut.Dispose();

        Assert.Equal(1, window.CloseCalls);
    }

    [Fact]
    public void Show_AfterDisposeIsRejected()
    {
        var sut = new AboutWindowCoordinator(() => new FakeAboutWindow());
        sut.Dispose();

        Assert.Throws<ObjectDisposedException>(sut.Show);
    }

    [Fact]
    public void Show_WindowCreationFailureIsContainedReportedAndRetryable()
    {
        var attempts = 0;
        var errors = new List<string>();
        var recovered = new FakeAboutWindow();
        using var sut = new AboutWindowCoordinator(
            () => ++attempts == 1
                ? throw new InvalidOperationException("missing resource")
                : recovered,
            errors.Add);

        sut.Show();

        Assert.Equal(1, attempts);
        Assert.Single(errors);
        Assert.Contains("missing resource", errors[0]);

        sut.Show();

        Assert.Equal(2, attempts);
        Assert.Equal(1, recovered.ShowCalls);
    }
}

internal sealed class FakeAboutWindow : IAboutWindow
{
    public event EventHandler? Closed;

    public int ShowCalls { get; private set; }

    public int ActivateCalls { get; private set; }

    public int CloseCalls { get; private set; }

    public void Show() => ShowCalls++;

    public bool Activate()
    {
        ActivateCalls++;
        return true;
    }

    public void Close() => CloseCalls++;

    public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);
}
