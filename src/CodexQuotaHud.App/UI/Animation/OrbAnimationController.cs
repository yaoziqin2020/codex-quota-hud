namespace CodexQuotaHud.App.UI.Animation;

public enum OrbAnimationState
{
    Hidden,
    Idle,
    Refreshing
}

public interface IOrbAnimationTarget
{
    TimeSpan RefreshHoldDuration => TimeSpan.FromSeconds(1.5);

    void ApplyAnimationState(
        OrbAnimationState state,
        bool animationsEnabled);
}

public sealed class OrbAnimationController : IDisposable
{
    private readonly object _gate = new();
    private readonly IAnimationDelay _delay;
    private IOrbAnimationTarget? _target;
    private CancellationTokenSource? _holdCancellation;
    private OrbAnimationState _requestedState = OrbAnimationState.Hidden;
    private OrbAnimationState _effectiveState = OrbAnimationState.Hidden;
    private bool _animationsEnabled = true;
    private long _generation;
    private bool _disposed;

    public OrbAnimationController(
        IOrbAnimationTarget? target = null,
        IAnimationDelay? delay = null)
    {
        _target = target;
        _delay = delay ?? new SystemAnimationDelay();
        ApplyLocked();
    }

    public OrbAnimationState State
    {
        get
        {
            lock (_gate)
            {
                return _requestedState;
            }
        }
    }

    public bool AnimationsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _animationsEnabled;
            }
        }
    }

    public void Attach(IOrbAnimationTarget? target)
    {
        lock (_gate)
        {
            if (_disposed || ReferenceEquals(_target, target))
            {
                return;
            }

            InvalidateHoldLocked();
            _target?.ApplyAnimationState(
                OrbAnimationState.Hidden,
                animationsEnabled: false);
            _target = target;
            _effectiveState = _requestedState;
            ApplyLocked();
        }
    }

    public void SetState(OrbAnimationState state)
    {
        if (!Enum.IsDefined(state))
        {
            state = OrbAnimationState.Hidden;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var previousRequestedState = _requestedState;
            _requestedState = state;
            switch (state)
            {
                case OrbAnimationState.Refreshing:
                    InvalidateHoldLocked();
                    _effectiveState = OrbAnimationState.Refreshing;
                    ApplyLocked();
                    break;
                case OrbAnimationState.Hidden:
                    InvalidateHoldLocked();
                    _effectiveState = OrbAnimationState.Hidden;
                    ApplyLocked();
                    break;
                default:
                    RequestIdleLocked(previousRequestedState);
                    break;
            }
        }
    }

    public void SetAnimationsEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_animationsEnabled == enabled)
            {
                return;
            }

            InvalidateHoldLocked();
            _animationsEnabled = enabled;
            _effectiveState = _requestedState;
            ApplyLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            InvalidateHoldLocked();
            _effectiveState = OrbAnimationState.Hidden;
            _target?.ApplyAnimationState(
                OrbAnimationState.Hidden,
                animationsEnabled: false);
            _target = null;
        }
    }

    private void RequestIdleLocked(OrbAnimationState previousRequestedState)
    {
        if (!_animationsEnabled ||
            _target is null ||
            _effectiveState != OrbAnimationState.Refreshing)
        {
            InvalidateHoldLocked();
            _effectiveState = OrbAnimationState.Idle;
            ApplyLocked();
            return;
        }

        if (previousRequestedState == OrbAnimationState.Idle &&
            _holdCancellation is not null)
        {
            return;
        }

        var duration = _target.RefreshHoldDuration;
        if (duration <= TimeSpan.Zero)
        {
            InvalidateHoldLocked();
            _effectiveState = OrbAnimationState.Idle;
            ApplyLocked();
            return;
        }

        InvalidateHoldLocked();
        var cancellation = new CancellationTokenSource();
        _holdCancellation = cancellation;
        var generation = _generation;
        var target = _target;
        _ = CompleteHoldAsync(
            duration,
            generation,
            target,
            cancellation);
    }

    private async Task CompleteHoldAsync(
        TimeSpan duration,
        long generation,
        IOrbAnimationTarget target,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await _delay.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed ||
                generation != _generation ||
                !ReferenceEquals(target, _target) ||
                !_animationsEnabled ||
                _requestedState != OrbAnimationState.Idle ||
                _effectiveState != OrbAnimationState.Refreshing)
            {
                return;
            }

            _holdCancellation = null;
            cancellation.Dispose();
            _effectiveState = OrbAnimationState.Idle;
            ApplyLocked();
        }
    }

    private void InvalidateHoldLocked()
    {
        _generation++;
        var cancellation = _holdCancellation;
        _holdCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void ApplyLocked() =>
        _target?.ApplyAnimationState(
            _effectiveState,
            _animationsEnabled);
}
