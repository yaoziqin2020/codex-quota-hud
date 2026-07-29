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
    private readonly object _notificationSync = new();
    private readonly object _stateSync = new();
    private readonly AsyncLocal<int> _notificationDepth = new();
    private readonly AsyncLocal<NotificationOrigin?> _notificationOrigin = new();
    private readonly List<RefreshSession> _sessions = [];
    private Task _notificationTail = Task.CompletedTask;
    private RefreshSession? _activeSession;
    private QuotaSnapshot? _lastSuccess;
    private QuotaRefreshState _state =
        new(false, false, QuotaDisplayState.Hidden(), null);
    private long _nextGeneration;
    private long _stateGeneration;
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

            var notificationOrigin = _notificationOrigin.Value;
            refresh =
                IsNotifying &&
                notificationOrigin is not null &&
                ReferenceEquals(notificationOrigin.Session, _activeSession)
                    ? notificationOrigin.Refresh
                    : GetOrCreateRefresh(_activeSession, onlyIfStale);
        }

        return WaitForCallerAsync(refresh, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Task disposal;
        TaskCompletionSource<IReadOnlyList<Task>> disposalKickoff;
        RefreshSession[] sessions;

        lock (_lifecycleSync)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposeStarted = true;
            _activeSession = null;
            _ = ReplaceState(
                ++_nextGeneration,
                new QuotaRefreshState(
                    false,
                    false,
                    QuotaDisplayState.Hidden(),
                    null));
            sessions = _sessions.ToArray();
            disposalKickoff = new TaskCompletionSource<IReadOnlyList<Task>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = _disposeTask = FinishDisposeAsync(
                disposalKickoff.Task);
        }

        CancelQuietly(_lifetimeCancellation);
        var retirements = sessions
            .Select(BeginRetirement)
            .ToArray();
        disposalKickoff.TrySetResult(retirements);
        return new ValueTask(disposal);
    }

    private Task StartAsync(CancellationToken callerCancellationToken)
    {
        RefreshSession session;
        Task initialRefresh;

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
            _ = ReplaceState(
                generation,
                new QuotaRefreshState(
                    true,
                    false,
                    QuotaDisplayState.Hidden(),
                    null));
            initialRefresh = GetOrCreateRefresh(session, onlyIfStale: false);
            session.SetPollTask(PollAsync(session));
        }

        session.Start();
        return WaitForInitialCallerAsync(
            initialRefresh,
            session,
            callerCancellationToken);
    }

    private Task StopAsync()
    {
        RefreshSession? stoppedSession;
        Task notification;

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
            _ = BeginRetirement(stoppedSession);
        }

        return IsNotifying ? Task.CompletedTask : notification;
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
            if (!IsNotifying)
            {
                await SnapshotNotificationTail().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (!callerCancellationToken.IsCancellationRequested &&
                  session.Cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task WaitForCallerAsync(
        Task refresh,
        CancellationToken callerCancellationToken)
    {
        await refresh.WaitAsync(callerCancellationToken)
            .ConfigureAwait(false);
        if (!IsNotifying)
        {
            await SnapshotNotificationTail().ConfigureAwait(false);
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

            var trackingReady = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var refresh = RefreshSerializedAsync(
                session,
                onlyIfStale,
                trackingReady.Task);
            session.InFlightRefresh = refresh;
            trackingReady.TrySetResult();
            return refresh;
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
        bool onlyIfStale,
        Task trackingReady)
    {
        var cancellationToken = session.Cancellation.Token;
        var lockTaken = false;

        try
        {
            await trackingReady.ConfigureAwait(false);
            var origin = new NotificationOrigin(
                session,
                session.GetTrackedRefresh());
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
                state => state with { IsRefreshing = true },
                origin);
            if (refreshing is null)
            {
                return;
            }

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
                    PublishFailure(
                        session,
                        "Quota refresh canceled.",
                        origin);
                }

                return;
            }
            catch (Exception exception)
            {
                PublishFailure(session, exception.Message, origin);
                return;
            }

            _ = TryCommitSuccess(session, snapshot, origin);
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

    private void PublishFailure(
        RefreshSession session,
        string error,
        NotificationOrigin origin)
    {
        var display =
            _lastSuccess is not null &&
            _clock.UtcNow - _lastSuccess.FetchedAt <= StaleLifetime
                ? QuotaDisplayState.FromSnapshot(_lastSuccess, isStale: true)
                : QuotaDisplayState.Hidden();
        _ = TryUpdateForSession(
            session,
            _ => new QuotaRefreshState(
                true,
                false,
                display,
                error),
            origin);
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

    private Task? TryCommitSuccess(
        RefreshSession session,
        QuotaSnapshot snapshot,
        NotificationOrigin origin)
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
            return Notify(new StateNotification(_state, origin));
        }
    }

    private Task? TryUpdateForSession(
        RefreshSession session,
        Func<QuotaRefreshState, QuotaRefreshState> update,
        NotificationOrigin origin)
    {
        lock (_stateSync)
        {
            if (_stateGeneration != session.Generation ||
                !_state.IsCodexRunning)
            {
                return null;
            }

            _state = update(_state);
            return Notify(new StateNotification(_state, origin));
        }
    }

    private Task ReplaceState(
        long generation,
        QuotaRefreshState state)
    {
        lock (_stateSync)
        {
            _stateGeneration = generation;
            _state = state;
            return Notify(new StateNotification(state, Origin: null));
        }
    }

    private Task Notify(StateNotification? notification)
    {
        if (notification is null)
        {
            return Task.CompletedTask;
        }

        lock (_notificationSync)
        {
            return _notificationTail =
                DeliverAfterAsync(_notificationTail, notification);
        }
    }

    private async Task DeliverAfterAsync(
        Task previous,
        StateNotification notification)
    {
        await previous.ConfigureAwait(false);
        await Task.Yield();

        var previousOrigin = _notificationOrigin.Value;
        _notificationDepth.Value++;
        _notificationOrigin.Value = notification.Origin;
        try
        {
            var handlers = StateChanged?.GetInvocationList();
            if (handlers is null)
            {
                return;
            }

            foreach (var handler in handlers)
            {
                try
                {
                    ((Action<QuotaRefreshState>)handler)(notification.State);
                }
                catch
                {
                }
            }
        }
        finally
        {
            _notificationOrigin.Value = previousOrigin;
            _notificationDepth.Value--;
        }
    }

    private Task SnapshotNotificationTail()
    {
        lock (_notificationSync)
        {
            return _notificationTail;
        }
    }

    private bool IsNotifying => _notificationDepth.Value > 0;

    private async Task FinishDisposeAsync(
        Task<IReadOnlyList<Task>> retirementKickoff)
    {
        try
        {
            var retirements = await retirementKickoff.ConfigureAwait(false);
            await Task.WhenAll(retirements).ConfigureAwait(false);
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _refreshLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private Task BeginRetirement(RefreshSession session)
    {
        return session.GetOrStartRetirement(
            ownedTasks => RetireSessionAsync(session, ownedTasks));
    }

    private async Task RetireSessionAsync(
        RefreshSession session,
        IReadOnlyList<Task> ownedTasks)
    {
        await Task.WhenAll(ownedTasks).ConfigureAwait(false);
        lock (_lifecycleSync)
        {
            _sessions.Remove(session);
        }

        session.ReleaseResources();
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

    private sealed record NotificationOrigin(
        RefreshSession Session,
        Task Refresh);

    private sealed record StateNotification(
        QuotaRefreshState State,
        NotificationOrigin? Origin);

    private sealed class RefreshSession(
        long generation,
        CancellationTokenSource cancellation)
    {
        private readonly TaskCompletionSource _start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _pollTask = Task.CompletedTask;
        private Task? _retirementTask;

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

        public Task GetTrackedRefresh()
        {
            lock (Sync)
            {
                return InFlightRefresh ??
                    throw new InvalidOperationException(
                        "Refresh task is not tracked.");
            }
        }

        public Task GetOrStartRetirement(
            Func<IReadOnlyList<Task>, Task> retire)
        {
            lock (Sync)
            {
                if (_retirementTask is not null)
                {
                    return _retirementTask;
                }

                IReadOnlyList<Task> ownedTasks = InFlightRefresh is null
                    ? [_pollTask]
                    : [_pollTask, InFlightRefresh];
                return _retirementTask = retire(ownedTasks);
            }
        }

        public void ReleaseResources()
        {
            lock (Sync)
            {
                _pollTask = Task.CompletedTask;
                InFlightRefresh = null;
            }

            Cancellation.Dispose();
        }
    }
}
