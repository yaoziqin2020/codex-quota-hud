using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.RateLimits;

namespace CodexQuotaHud.Core.Refresh;

public sealed class QuotaRefreshService : IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaleLifetime = TimeSpan.FromMinutes(5);

    private readonly IQuotaClient _quotaClient;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _lifecycleSync = new();
    private readonly object _stateSync = new();
    private readonly List<RefreshSession> _sessions = [];
    private RefreshSession? _activeSession;
    private QuotaSnapshot? _lastSuccess;
    private QuotaRefreshState _state =
        new(false, false, QuotaDisplayState.Hidden(), null);
    private long _nextGeneration;
    private long _stateGeneration;
    private long _stateVersion;
    private bool _disposeStarted;
    private Task? _disposeTask;

    public QuotaRefreshService(IQuotaClient quotaClient, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(quotaClient);
        ArgumentNullException.ThrowIfNull(clock);

        _quotaClient = quotaClient;
        _clock = clock;
    }

    public event Action<QuotaRefreshState>? StateChanged;

    public Task SetCodexRunningAsync(
        bool isRunning,
        CancellationToken cancellationToken)
    {
        return isRunning
            ? StartAsync(cancellationToken)
            : StopAsync();
    }

    public Task RefreshNowAsync(
        bool onlyIfStale,
        CancellationToken cancellationToken)
    {
        Task refresh;
        lock (_lifecycleSync)
        {
            ThrowIfDisposing();
            if (_activeSession is null)
            {
                return Task.CompletedTask;
            }

            refresh = GetOrCreateRefresh(_activeSession, onlyIfStale);
        }

        return refresh.WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        StateNotification? notification;
        Task disposal;
        TaskCompletionSource disposalKickoff;
        RefreshSession[] sessions;

        lock (_lifecycleSync)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposeStarted = true;
            _activeSession = null;
            notification = ReplaceState(
                ++_nextGeneration,
                new QuotaRefreshState(
                    false,
                    false,
                    QuotaDisplayState.Hidden(),
                    null));
            sessions = _sessions.ToArray();
            var ownedTasks = sessions
                .SelectMany(static session => session.SnapshotOwnedTasks())
                .ToArray();
            disposalKickoff = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = _disposeTask = FinishDisposeAsync(
                disposalKickoff.Task,
                ownedTasks,
                sessions);
        }

        CancelQuietly(_lifetimeCancellation);
        Notify(notification);
        disposalKickoff.TrySetResult();
        return new ValueTask(disposal);
    }

    private Task StartAsync(CancellationToken callerCancellationToken)
    {
        RefreshSession session;
        Task initialRefresh;
        StateNotification notification;

        lock (_lifecycleSync)
        {
            ThrowIfDisposing();
            if (_activeSession is not null)
            {
                return Task.CompletedTask;
            }

            var generation = ++_nextGeneration;
            session = new RefreshSession(
                generation,
                CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetimeCancellation.Token));
            _sessions.Add(session);
            _activeSession = session;
            notification = ReplaceState(
                generation,
                new QuotaRefreshState(
                    true,
                    false,
                    QuotaDisplayState.Hidden(),
                    null));
            initialRefresh = GetOrCreateRefresh(session, onlyIfStale: false);
            session.SetPollTask(PollAsync(session));
        }

        Notify(notification);
        session.Start();
        return WaitForInitialCallerAsync(
            initialRefresh,
            session,
            callerCancellationToken);
    }

    private Task StopAsync()
    {
        RefreshSession? stoppedSession;
        StateNotification notification;

        lock (_lifecycleSync)
        {
            ThrowIfDisposing();
            stoppedSession = _activeSession;
            _activeSession = null;
            notification = ReplaceState(
                ++_nextGeneration,
                new QuotaRefreshState(
                    false,
                    false,
                    QuotaDisplayState.Hidden(),
                    null));
        }

        if (stoppedSession is not null)
        {
            CancelQuietly(stoppedSession.Cancellation);
        }

        Notify(notification);
        return Task.CompletedTask;
    }

    private async Task WaitForInitialCallerAsync(
        Task initialRefresh,
        RefreshSession session,
        CancellationToken callerCancellationToken)
    {
        try
        {
            await initialRefresh.WaitAsync(callerCancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!callerCancellationToken.IsCancellationRequested &&
                  session.Cancellation.IsCancellationRequested)
        {
        }
    }

    private Task GetOrCreateRefresh(
        RefreshSession session,
        bool onlyIfStale)
    {
        lock (session.Sync)
        {
            if (session.InFlightRefresh is { IsCompleted: false } inFlight)
            {
                return inFlight;
            }

            return session.InFlightRefresh =
                RefreshSerializedAsync(session, onlyIfStale);
        }
    }

    private Task? GetOrCreateActiveRefresh(
        RefreshSession session,
        bool onlyIfStale)
    {
        lock (_lifecycleSync)
        {
            if (_disposeStarted ||
                !ReferenceEquals(_activeSession, session))
            {
                return null;
            }

            return GetOrCreateRefresh(session, onlyIfStale);
        }
    }

    private async Task RefreshSerializedAsync(
        RefreshSession session,
        bool onlyIfStale)
    {
        var cancellationToken = session.Cancellation.Token;
        var lockTaken = false;

        try
        {
            await session.WaitForStartAsync(cancellationToken)
                .ConfigureAwait(false);
            await _refreshLock.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            lockTaken = true;

            if (onlyIfStale &&
                _lastSuccess is not null &&
                _clock.UtcNow - _lastSuccess.FetchedAt < RefreshInterval)
            {
                return;
            }

            var refreshing = TryUpdateForSession(
                session,
                state => state with { IsRefreshing = true });
            if (refreshing is null)
            {
                return;
            }

            Notify(refreshing);

            QuotaSnapshot snapshot;
            try
            {
                snapshot = await _quotaClient.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    PublishFailure(session, "Quota refresh canceled.");
                }

                return;
            }
            catch (Exception exception)
            {
                PublishFailure(session, exception.Message);
                return;
            }

            var succeeded = TryCommitSuccess(session, snapshot);
            Notify(succeeded);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (lockTaken)
            {
                _refreshLock.Release();
            }
        }
    }

    private void PublishFailure(RefreshSession session, string error)
    {
        var display =
            _lastSuccess is not null &&
            _clock.UtcNow - _lastSuccess.FetchedAt <= StaleLifetime
                ? QuotaDisplayState.FromSnapshot(_lastSuccess, isStale: true)
                : QuotaDisplayState.Hidden();
        var failed = TryUpdateForSession(
            session,
            _ => new QuotaRefreshState(
                true,
                false,
                display,
                error));
        Notify(failed);
    }

    private async Task PollAsync(RefreshSession session)
    {
        var cancellationToken = session.Cancellation.Token;

        try
        {
            await session.WaitForStartAsync(cancellationToken)
                .ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                await _clock.DelayAsync(RefreshInterval, cancellationToken)
                    .ConfigureAwait(false);
                var refresh = GetOrCreateActiveRefresh(
                    session,
                    onlyIfStale: false);
                if (refresh is null)
                {
                    return;
                }

                await refresh.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private StateNotification? TryCommitSuccess(
        RefreshSession session,
        QuotaSnapshot snapshot)
    {
        lock (_stateSync)
        {
            if (_stateGeneration != session.Generation ||
                !_state.IsCodexRunning)
            {
                return null;
            }

            _lastSuccess = snapshot;
            _state = new QuotaRefreshState(
                true,
                false,
                QuotaDisplayState.FromSnapshot(snapshot),
                null);
            return new StateNotification(++_stateVersion, _state);
        }
    }

    private StateNotification? TryUpdateForSession(
        RefreshSession session,
        Func<QuotaRefreshState, QuotaRefreshState> update)
    {
        lock (_stateSync)
        {
            if (_stateGeneration != session.Generation ||
                !_state.IsCodexRunning)
            {
                return null;
            }

            _state = update(_state);
            return new StateNotification(++_stateVersion, _state);
        }
    }

    private StateNotification ReplaceState(
        long generation,
        QuotaRefreshState state)
    {
        lock (_stateSync)
        {
            _stateGeneration = generation;
            _state = state;
            return new StateNotification(++_stateVersion, state);
        }
    }

    private void Notify(StateNotification? notification)
    {
        if (notification is null)
        {
            return;
        }

        var handlers = StateChanged?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers)
        {
            if (!IsCurrent(notification))
            {
                return;
            }

            try
            {
                ((Action<QuotaRefreshState>)handler)(notification.State);
            }
            catch
            {
            }
        }
    }

    private bool IsCurrent(StateNotification notification)
    {
        lock (_stateSync)
        {
            return notification.Version == _stateVersion;
        }
    }

    private async Task FinishDisposeAsync(
        Task kickoff,
        IReadOnlyList<Task> ownedTasks,
        IReadOnlyList<RefreshSession> sessions)
    {
        try
        {
            await kickoff.ConfigureAwait(false);
            await Task.WhenAll(ownedTasks).ConfigureAwait(false);
        }
        finally
        {
            foreach (var session in sessions)
            {
                session.Cancellation.Dispose();
            }

            _lifetimeCancellation.Dispose();
            _refreshLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private void ThrowIfDisposing()
    {
        ObjectDisposedException.ThrowIf(_disposeStarted, this);
    }

    private static void CancelQuietly(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch
        {
        }
    }

    private sealed record StateNotification(
        long Version,
        QuotaRefreshState State);

    private sealed class RefreshSession(
        long generation,
        CancellationTokenSource cancellation)
    {
        private readonly TaskCompletionSource _start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _pollTask = Task.CompletedTask;

        public long Generation { get; } = generation;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public object Sync { get; } = new();
        public Task? InFlightRefresh { get; set; }

        public void Start() => _start.TrySetResult();

        public Task WaitForStartAsync(CancellationToken cancellationToken) =>
            _start.Task.WaitAsync(cancellationToken);

        public void SetPollTask(Task pollTask)
        {
            lock (Sync)
            {
                _pollTask = pollTask;
            }
        }

        public IReadOnlyList<Task> SnapshotOwnedTasks()
        {
            lock (Sync)
            {
                return InFlightRefresh is null
                    ? [_pollTask]
                    : [_pollTask, InFlightRefresh];
            }
        }
    }
}
