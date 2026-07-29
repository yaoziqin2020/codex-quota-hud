using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.RateLimits;

namespace CodexQuotaHud.Core.Refresh;

public sealed class QuotaRefreshService : IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly IQuotaClient _quotaClient;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _disposing = new();
    private readonly object _pollSync = new();
    private readonly object _refreshTaskSync = new();
    private readonly object _stateSync = new();
    private Task _pollTask = Task.CompletedTask;
    private CancellationTokenSource? _pollCancellation;
    private QuotaSnapshot? _lastSuccess;
    private Task? _inFlightRefresh;
    private QuotaRefreshState _state =
        new(false, false, QuotaDisplayState.Hidden(), null);

    public QuotaRefreshService(IQuotaClient quotaClient, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(quotaClient);
        ArgumentNullException.ThrowIfNull(clock);

        _quotaClient = quotaClient;
        _clock = clock;
    }

    public event Action<QuotaRefreshState>? StateChanged;

    public async Task SetCodexRunningAsync(
        bool isRunning,
        CancellationToken cancellationToken)
    {
        if (!isRunning)
        {
            Publish(new QuotaRefreshState(
                false,
                false,
                QuotaDisplayState.Hidden(),
                null));
            await StopPollingAsync().ConfigureAwait(false);
            return;
        }

        if (IsCodexRunning)
        {
            return;
        }

        var pollCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_disposing.Token);
        var pollToken = pollCancellation.Token;
        lock (_pollSync)
        {
            _pollCancellation = pollCancellation;
            _pollTask = Task.CompletedTask;
        }

        Publish(state => state with { IsCodexRunning = true });
        await RefreshNowAsync(false, cancellationToken).ConfigureAwait(false);
        lock (_pollSync)
        {
            if (ReferenceEquals(_pollCancellation, pollCancellation) &&
                IsCodexRunning)
            {
                _pollTask = PollAsync(pollToken);
            }
        }
    }

    public Task RefreshNowAsync(
        bool onlyIfStale,
        CancellationToken cancellationToken)
    {
        if (!IsCodexRunning)
        {
            return Task.CompletedTask;
        }

        lock (_refreshTaskSync)
        {
            if (_inFlightRefresh is { IsCompleted: false } inFlight)
            {
                return inFlight.WaitAsync(cancellationToken);
            }

            return _inFlightRefresh =
                RefreshSerializedAsync(onlyIfStale, cancellationToken);
        }
    }

    private async Task RefreshSerializedAsync(
        bool onlyIfStale,
        CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (onlyIfStale &&
                _lastSuccess is not null &&
                _clock.UtcNow - _lastSuccess.FetchedAt < RefreshInterval)
            {
                return;
            }

            if (!TryPublishWhileRunning(
                    state => state with { IsRefreshing = true }))
            {
                return;
            }

            var snapshot = await _quotaClient.ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            _lastSuccess = snapshot;
            TryPublishWhileRunning(_ => new QuotaRefreshState(
                    true,
                    false,
                    QuotaDisplayState.FromSnapshot(snapshot),
                    null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var display =
                _lastSuccess is not null &&
                _clock.UtcNow - _lastSuccess.FetchedAt <= TimeSpan.FromMinutes(5)
                    ? QuotaDisplayState.FromSnapshot(_lastSuccess, isStale: true)
                    : QuotaDisplayState.Hidden();

            TryPublishWhileRunning(_ => new QuotaRefreshState(
                    true,
                    false,
                    display,
                    exception.Message));
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposing.Cancel();
        await StopPollingAsync().ConfigureAwait(false);
        _disposing.Dispose();
        _refreshLock.Dispose();
    }

    private void Publish(QuotaRefreshState state)
    {
        Publish(_ => state);
    }

    private void Publish(Func<QuotaRefreshState, QuotaRefreshState> createState)
    {
        lock (_stateSync)
        {
            _state = createState(_state);
            StateChanged?.Invoke(_state);
        }
    }

    private bool TryPublishWhileRunning(
        Func<QuotaRefreshState, QuotaRefreshState> createState)
    {
        lock (_stateSync)
        {
            if (!_state.IsCodexRunning)
            {
                return false;
            }

            _state = createState(_state);
            StateChanged?.Invoke(_state);
            return true;
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (IsCodexRunning)
            {
                await _clock.DelayAsync(
                        RefreshInterval,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!IsCodexRunning)
                {
                    break;
                }

                await RefreshNowAsync(false, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopPollingAsync()
    {
        CancellationTokenSource cancellation;
        Task pollTask;
        lock (_pollSync)
        {
            if (_pollCancellation is null)
            {
                return;
            }

            cancellation = _pollCancellation;
            _pollCancellation = null;
            cancellation.Cancel();
            pollTask = _pollTask;
            _pollTask = Task.CompletedTask;
        }

        try
        {
            await pollTask.ConfigureAwait(false);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private bool IsCodexRunning
    {
        get
        {
            lock (_stateSync)
            {
                return _state.IsCodexRunning;
            }
        }
    }
}
