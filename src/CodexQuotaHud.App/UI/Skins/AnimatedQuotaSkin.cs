using System.Windows;
using System.Windows.Media.Animation;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.UI.Skins;

public abstract class AnimatedQuotaSkin :
    System.Windows.Controls.UserControl,
    IQuotaSkin,
    IOrbAnimationTarget
{
    private const int IdleFrameRate = 4;
    private const int RefreshingFrameRate = 24;

    private static readonly DependencyProperty AnimationRateProperty =
        DependencyProperty.Register(
            nameof(AnimationRate),
            typeof(double),
            typeof(AnimatedQuotaSkin),
            new PropertyMetadata(0d, OnAnimationRateChanged));

    private readonly List<AnimationTrack> _tracks = [];
    private bool _started;
    private int? _desiredFrameRate;

    public abstract SkinId Id { get; }

    public string SelectionKey => SkinSelectionKey.FromBuiltIn(Id);

    public FrameworkElement View => this;

    protected double AnimationRate
    {
        get => (double)GetValue(AnimationRateProperty);
        set => SetValue(AnimationRateProperty, value);
    }

    internal IReadOnlyList<int?> ConfiguredFrameRates =>
        _tracks
            .Select(static track =>
                Timeline.GetDesiredFrameRate(track.Storyboard))
            .ToArray();

    public void Render(QuotaSkinState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        RenderCore(state);
    }

    public void ApplyAnimationState(
        OrbAnimationState state,
        bool animationsEnabled)
    {
        if (state == OrbAnimationState.Hidden || !animationsEnabled)
        {
            StopAnimations();
            return;
        }

        var desiredFrameRate = state == OrbAnimationState.Refreshing
            ? RefreshingFrameRate
            : IdleFrameRate;
        EnsureStarted(desiredFrameRate);
        var target = state == OrbAnimationState.Refreshing ? 1d : 0d;
        var duration = state == OrbAnimationState.Refreshing
            ? TimeSpan.FromMilliseconds(260)
            : TimeSpan.FromMilliseconds(600);
        var rateTransition = new DoubleAnimation
        {
            To = target,
            Duration = duration,
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            },
            FillBehavior = FillBehavior.HoldEnd
        };
        Timeline.SetDesiredFrameRate(rateTransition, desiredFrameRate);
        BeginAnimation(
            AnimationRateProperty,
            rateTransition,
            HandoffBehavior.SnapshotAndReplace);
    }

    protected abstract void RenderCore(QuotaSkinState state);

    protected void ConfigureRotation(
        string targetName,
        double idleSeconds,
        double refreshingSeconds,
        bool clockwise = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (idleSeconds <= 0 || refreshingSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleSeconds),
                "Animation durations must be positive.");
        }

        var direction = clockwise ? 360d : -360d;
        var animation = new DoubleAnimation
        {
            From = 0,
            To = direction,
            Duration = TimeSpan.FromSeconds(idleSeconds),
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTargetName(animation, targetName);
        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath("Angle"));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _tracks.Add(new AnimationTrack(
            storyboard,
            idleSeconds / refreshingSeconds));
    }

    protected void ConfigureSlosh(
        string targetName,
        double idleSeconds,
        double refreshingSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (idleSeconds <= 0 || refreshingSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleSeconds),
                "Animation durations must be positive.");
        }

        var animation = new DoubleAnimation
        {
            From = -2,
            To = 2,
            Duration = TimeSpan.FromSeconds(idleSeconds / 2),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase
            {
                EasingMode = EasingMode.EaseInOut
            }
        };
        Storyboard.SetTargetName(animation, targetName);
        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath("X"));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        _tracks.Add(new AnimationTrack(
            storyboard,
            idleSeconds / refreshingSeconds));
    }

    private static void OnAnimationRateChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var skin = (AnimatedQuotaSkin)dependencyObject;
        skin.ApplySpeedRatios((double)eventArgs.NewValue);
    }

    private void EnsureStarted(int desiredFrameRate)
    {
        if (_started && _desiredFrameRate == desiredFrameRate)
        {
            return;
        }

        if (_started)
        {
            foreach (var track in _tracks)
            {
                track.Storyboard.Remove(this);
            }
        }

        foreach (var track in _tracks)
        {
            Timeline.SetDesiredFrameRate(
                track.Storyboard,
                desiredFrameRate);
            track.Storyboard.Begin(
                this,
                HandoffBehavior.SnapshotAndReplace,
                isControllable: true);
        }

        _started = true;
        _desiredFrameRate = desiredFrameRate;
        ApplySpeedRatios(AnimationRate);
    }

    private void StopAnimations()
    {
        BeginAnimation(AnimationRateProperty, animation: null);
        AnimationRate = 0;
        if (!_started)
        {
            return;
        }

        foreach (var track in _tracks)
        {
            track.Storyboard.Remove(this);
        }

        _started = false;
        _desiredFrameRate = null;
    }

    private void ApplySpeedRatios(double rate)
    {
        if (!_started)
        {
            return;
        }

        var normalized = double.IsFinite(rate)
            ? Math.Clamp(rate, 0, 1)
            : 0;
        foreach (var track in _tracks)
        {
            var speed = 1 + ((track.RefreshingSpeedRatio - 1) * normalized);
            track.Storyboard.SetSpeedRatio(this, speed);
        }
    }

    private sealed record AnimationTrack(
        Storyboard Storyboard,
        double RefreshingSpeedRatio);
}
