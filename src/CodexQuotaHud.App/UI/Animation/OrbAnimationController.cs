namespace CodexQuotaHud.App.UI.Animation;

public enum OrbAnimationState
{
    Hidden,
    Idle,
    Refreshing
}

public interface IOrbAnimationTarget
{
    void ApplyAnimationState(
        OrbAnimationState state,
        bool animationsEnabled);
}

public sealed class OrbAnimationController
{
    private IOrbAnimationTarget? _target;

    public OrbAnimationController(IOrbAnimationTarget? target = null)
    {
        _target = target;
        Apply();
    }

    public OrbAnimationState State { get; private set; } =
        OrbAnimationState.Hidden;

    public bool AnimationsEnabled { get; private set; } = true;

    public void Attach(IOrbAnimationTarget? target)
    {
        if (ReferenceEquals(_target, target))
        {
            return;
        }

        _target?.ApplyAnimationState(
            OrbAnimationState.Hidden,
            animationsEnabled: false);
        _target = target;
        Apply();
    }

    public void SetState(OrbAnimationState state)
    {
        if (!Enum.IsDefined(state))
        {
            state = OrbAnimationState.Hidden;
        }

        State = state;
        Apply();
    }

    public void SetAnimationsEnabled(bool enabled)
    {
        AnimationsEnabled = enabled;
        Apply();
    }

    private void Apply() =>
        _target?.ApplyAnimationState(State, AnimationsEnabled);
}
