using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Animation;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class OrbAnimationControllerTests
{
    [Theory]
    [InlineData(OrbAnimationState.Hidden)]
    [InlineData(OrbAnimationState.Idle)]
    [InlineData(OrbAnimationState.Refreshing)]
    public void SetState_AppliesRequestedStateToAttachedTarget(
        OrbAnimationState expected)
    {
        var target = new RecordingAnimationTarget();
        var controller = new OrbAnimationController(target);

        controller.SetState(expected);

        Assert.Equal((expected, true), target.Last);
    }

    [Fact]
    public void AnimationsDisabled_StopsMotionWithoutChangingLogicalState()
    {
        var target = new RecordingAnimationTarget();
        var controller = new OrbAnimationController(target);
        controller.SetState(OrbAnimationState.Refreshing);

        controller.SetAnimationsEnabled(false);

        Assert.Equal(OrbAnimationState.Refreshing, controller.State);
        Assert.Equal((OrbAnimationState.Refreshing, false), target.Last);
    }

    [Fact]
    public void Attach_StopsOldTargetAndAppliesCurrentStateToNewTarget()
    {
        var oldTarget = new RecordingAnimationTarget();
        var newTarget = new RecordingAnimationTarget();
        var controller = new OrbAnimationController(oldTarget);
        controller.SetState(OrbAnimationState.Idle);

        controller.Attach(newTarget);

        Assert.Contains(
            (OrbAnimationState.Hidden, false),
            oldTarget.AppliedStates);
        Assert.Equal((OrbAnimationState.Idle, true), newTarget.Last);
    }

    [Fact]
    public void Hidden_StopsAnimationEvenWhenAnimationsAreEnabled()
    {
        var target = new RecordingAnimationTarget();
        var controller = new OrbAnimationController(target);
        controller.SetState(OrbAnimationState.Refreshing);

        controller.SetState(OrbAnimationState.Hidden);

        Assert.Equal((OrbAnimationState.Hidden, true), target.Last);
    }

    [Fact]
    public void RefreshCompletion_HoldsEffectiveRefreshingForTargetDuration()
    {
        var delay = new FakeAnimationDelay();
        var target = new RecordingAnimationTarget(
            TimeSpan.FromMilliseconds(725));
        using var controller = new OrbAnimationController(target, delay);

        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);

        Assert.Equal(OrbAnimationState.Idle, controller.State);
        Assert.Equal((OrbAnimationState.Refreshing, true), target.Last);
        var pending = Assert.Single(delay.Requests);
        Assert.Equal(TimeSpan.FromMilliseconds(725), pending.Duration);

        pending.Complete();

        Assert.Equal((OrbAnimationState.Idle, true), target.Last);
    }

    [Fact]
    public void ZeroHoldDuration_AppliesIdleImmediatelyWithoutSchedulingDelay()
    {
        var delay = new FakeAnimationDelay();
        var target = new RecordingAnimationTarget(TimeSpan.Zero);
        using var controller = new OrbAnimationController(target, delay);
        controller.SetState(OrbAnimationState.Refreshing);

        controller.SetState(OrbAnimationState.Idle);

        Assert.Equal((OrbAnimationState.Idle, true), target.Last);
        Assert.Empty(delay.Requests);
    }

    [Fact]
    public void TargetWithoutCustomTiming_UsesBuiltInOnePointFiveSecondHold()
    {
        var delay = new FakeAnimationDelay();
        var target = new DefaultHoldAnimationTarget();
        using var controller = new OrbAnimationController(target, delay);
        controller.SetState(OrbAnimationState.Refreshing);

        controller.SetState(OrbAnimationState.Idle);

        Assert.Equal(
            TimeSpan.FromSeconds(1.5),
            Assert.Single(delay.Requests).Duration);
    }

    [Fact]
    public void RepeatedIdleDuringHold_DoesNotStackOrRestartDelay()
    {
        var delay = new FakeAnimationDelay();
        var target = new RecordingAnimationTarget(TimeSpan.FromSeconds(2));
        using var controller = new OrbAnimationController(target, delay);
        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);
        var pending = Assert.Single(delay.Requests);

        controller.SetState(OrbAnimationState.Idle);

        Assert.Single(delay.Requests);
        Assert.False(pending.IsCancellationRequested);
        pending.Complete();
        Assert.Equal((OrbAnimationState.Idle, true), target.Last);
    }

    [Fact]
    public void RepeatedRefresh_CancelsOldHoldAndRestartsFullTargetDuration()
    {
        var delay = new FakeAnimationDelay();
        var target = new RecordingAnimationTarget(TimeSpan.FromSeconds(2));
        using var controller = new OrbAnimationController(target, delay);
        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);
        var first = Assert.Single(delay.Requests);

        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);

        Assert.True(first.IsCancellationRequested);
        Assert.Equal(2, delay.Requests.Count);
        var second = delay.Requests[1];
        Assert.Equal(TimeSpan.FromSeconds(2), second.Duration);
        first.Complete();
        Assert.Equal((OrbAnimationState.Refreshing, true), target.Last);

        second.Complete();
        Assert.Equal((OrbAnimationState.Idle, true), target.Last);
    }

    [Fact]
    public void AttachNewTarget_CancelsOldContinuationAndAppliesRequestedState()
    {
        var delay = new FakeAnimationDelay();
        var oldTarget = new RecordingAnimationTarget(TimeSpan.FromSeconds(2));
        var newTarget = new RecordingAnimationTarget(TimeSpan.FromSeconds(3));
        using var controller = new OrbAnimationController(oldTarget, delay);
        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);
        var oldHold = Assert.Single(delay.Requests);

        controller.Attach(newTarget);

        Assert.True(oldHold.IsCancellationRequested);
        Assert.Equal((OrbAnimationState.Hidden, false), oldTarget.Last);
        Assert.Equal((OrbAnimationState.Idle, true), newTarget.Last);
        oldHold.Complete();
        Assert.Equal((OrbAnimationState.Idle, true), newTarget.Last);
    }

    [Fact]
    public void Hidden_CancelsHoldAndCannotBeOverwrittenByOldContinuation()
    {
        var delay = new FakeAnimationDelay();
        var target = new RecordingAnimationTarget(TimeSpan.FromSeconds(2));
        using var controller = new OrbAnimationController(target, delay);
        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);
        var oldHold = Assert.Single(delay.Requests);

        controller.SetState(OrbAnimationState.Hidden);

        Assert.True(oldHold.IsCancellationRequested);
        Assert.Equal((OrbAnimationState.Hidden, true), target.Last);
        oldHold.Complete();
        Assert.Equal((OrbAnimationState.Hidden, true), target.Last);
    }

    [Fact]
    public void Disabled_CancelsHoldAndAppliesRequestedSafeStateImmediately()
    {
        var delay = new FakeAnimationDelay();
        var target = new RecordingAnimationTarget(TimeSpan.FromSeconds(2));
        using var controller = new OrbAnimationController(target, delay);
        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);
        var oldHold = Assert.Single(delay.Requests);

        controller.SetAnimationsEnabled(false);

        Assert.True(oldHold.IsCancellationRequested);
        Assert.Equal((OrbAnimationState.Idle, false), target.Last);
        oldHold.Complete();
        Assert.Equal((OrbAnimationState.Idle, false), target.Last);
    }

    [Fact]
    public void Detach_CancelsHoldAndHidesOldTargetImmediately()
    {
        var delay = new FakeAnimationDelay();
        var target = new RecordingAnimationTarget(TimeSpan.FromSeconds(2));
        using var controller = new OrbAnimationController(target, delay);
        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);
        var oldHold = Assert.Single(delay.Requests);

        controller.Attach(target: null);

        Assert.True(oldHold.IsCancellationRequested);
        Assert.Equal((OrbAnimationState.Hidden, false), target.Last);
        oldHold.Complete();
        Assert.Equal((OrbAnimationState.Hidden, false), target.Last);
    }

    [Fact]
    public void Dispose_CancelsHoldHidesTargetAndIgnoresOldContinuation()
    {
        var delay = new FakeAnimationDelay();
        var target = new RecordingAnimationTarget(TimeSpan.FromSeconds(2));
        var controller = new OrbAnimationController(target, delay);
        controller.SetState(OrbAnimationState.Refreshing);
        controller.SetState(OrbAnimationState.Idle);
        var oldHold = Assert.Single(delay.Requests);

        controller.Dispose();
        controller.Dispose();

        Assert.True(oldHold.IsCancellationRequested);
        Assert.Equal((OrbAnimationState.Hidden, false), target.Last);
        oldHold.Complete();
        Assert.Equal((OrbAnimationState.Hidden, false), target.Last);
    }

    [Theory]
    [InlineData(false, true, true, OrbAnimationState.Hidden)]
    [InlineData(true, false, true, OrbAnimationState.Hidden)]
    [InlineData(true, true, false, OrbAnimationState.Idle)]
    [InlineData(true, true, true, OrbAnimationState.Refreshing)]
    public void WindowAnimationState_RequiresBothWindowAndDisplayVisibility(
        bool windowVisible,
        bool displayVisible,
        bool refreshing,
        OrbAnimationState expected)
    {
        Assert.Equal(
            expected,
            QuotaOrbWindow.SelectAnimationState(
                windowVisible,
                displayVisible,
                refreshing));
    }

    private sealed class RecordingAnimationTarget(
        TimeSpan? refreshHoldDuration = null) : IOrbAnimationTarget
    {
        public TimeSpan RefreshHoldDuration { get; } =
            refreshHoldDuration ?? TimeSpan.FromSeconds(1.5);

        public List<(OrbAnimationState State, bool Enabled)> AppliedStates { get; } = [];

        public (OrbAnimationState State, bool Enabled) Last =>
            Assert.Single(AppliedStates.TakeLast(1));

        public void ApplyAnimationState(
            OrbAnimationState state,
            bool animationsEnabled) =>
            AppliedStates.Add((state, animationsEnabled));
    }

    private sealed class DefaultHoldAnimationTarget : IOrbAnimationTarget
    {
        public void ApplyAnimationState(
            OrbAnimationState state,
            bool animationsEnabled)
        {
        }
    }
}
