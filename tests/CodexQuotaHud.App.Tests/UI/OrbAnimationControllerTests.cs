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

    private sealed class RecordingAnimationTarget : IOrbAnimationTarget
    {
        public List<(OrbAnimationState State, bool Enabled)> AppliedStates { get; } = [];

        public (OrbAnimationState State, bool Enabled) Last =>
            Assert.Single(AppliedStates.TakeLast(1));

        public void ApplyAnimationState(
            OrbAnimationState state,
            bool animationsEnabled) =>
            AppliedStates.Add((state, animationsEnabled));
    }
}
