using System.Windows;
using System.Windows.Media.Animation;
using CodexQuotaHud.App.UI.Animation;

namespace CodexQuotaHud.App.UI.Skins;

internal sealed class LiquidTankMotionController
{
    private const int IdleFrameRate = 4;
    private const int RefreshingFrameRate = 24;

    private readonly FrameworkElement _owner;
    private readonly List<MotionTrack> _tracks = [];
    private bool _running;
    private int? _frameRate;

    public LiquidTankMotionController(FrameworkElement owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _tracks.Add(CreateWaveTrack());
        _tracks.Add(CreateBubbleTrack(
            "BubbleOne",
            "BubbleOneTransform",
            idleSeconds: 10.5,
            refreshingSeconds: 2.5,
            beginDelaySeconds: 0,
            horizontalDrift: 4));
        _tracks.Add(CreateBubbleTrack(
            "BubbleTwo",
            "BubbleTwoTransform",
            idleSeconds: 13,
            refreshingSeconds: 2.9,
            beginDelaySeconds: 3.2,
            horizontalDrift: -3));
        _tracks.Add(CreateBubbleTrack(
            "BubbleThree",
            "BubbleThreeTransform",
            idleSeconds: 9,
            refreshingSeconds: 2.3,
            beginDelaySeconds: 5.4,
            horizontalDrift: 3));
    }

    internal int ConfiguredTrackCount => _tracks.Count;

    internal IReadOnlyList<int?> ConfiguredFrameRates =>
        _tracks
            .Select(static track =>
                Timeline.GetDesiredFrameRate(track.Storyboard))
            .ToArray();

    internal int ActiveClockCount =>
        _running ? _tracks.Count : 0;

    public void Apply(
        OrbAnimationState state,
        bool animationsEnabled)
    {
        if (state == OrbAnimationState.Hidden || !animationsEnabled)
        {
            Stop();
            return;
        }

        var frameRate = state == OrbAnimationState.Refreshing
            ? RefreshingFrameRate
            : IdleFrameRate;
        var refreshing = state == OrbAnimationState.Refreshing;
        Start(frameRate, refreshing);
    }

    private void Start(int frameRate, bool refreshing)
    {
        if (_running && _frameRate == frameRate)
        {
            return;
        }

        Stop();
        foreach (var track in _tracks)
        {
            Timeline.SetDesiredFrameRate(track.Storyboard, frameRate);
            track.Storyboard.Begin(
                _owner,
                HandoffBehavior.SnapshotAndReplace,
                isControllable: true);
            track.Storyboard.SetSpeedRatio(
                _owner,
                refreshing ? track.RefreshingSpeedRatio : 1);
        }

        _running = true;
        _frameRate = frameRate;
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        foreach (var track in _tracks)
        {
            track.Storyboard.Remove(_owner);
        }

        _running = false;
        _frameRate = null;
    }

    private static MotionTrack CreateWaveTrack()
    {
        const double idleSeconds = 9;
        const double refreshingSeconds = 2.5;
        var halfCycle = TimeSpan.FromSeconds(idleSeconds / 2);
        var storyboard = new Storyboard();
        AddOscillation(
            storyboard,
            "TankSurfaceTranslateTransform",
            "X",
            from: -10,
            to: 10,
            halfCycle);
        AddOscillation(
            storyboard,
            "TankSurfaceTranslateTransform",
            "Y",
            from: -1.4,
            to: 1.4,
            halfCycle);
        AddOscillation(
            storyboard,
            "TankSurfaceRotateTransform",
            "Angle",
            from: -1.6,
            to: 1.6,
            halfCycle);
        return new MotionTrack(
            storyboard,
            idleSeconds / refreshingSeconds);
    }

    private static MotionTrack CreateBubbleTrack(
        string bubbleName,
        string transformName,
        double idleSeconds,
        double refreshingSeconds,
        double beginDelaySeconds,
        double horizontalDrift)
    {
        var duration = TimeSpan.FromSeconds(idleSeconds);
        var beginTime = TimeSpan.FromSeconds(beginDelaySeconds);
        var storyboard = new Storyboard();

        var rise = new DoubleAnimation
        {
            From = 12,
            To = -76,
            Duration = duration,
            BeginTime = beginTime,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };
        Storyboard.SetTargetName(rise, transformName);
        Storyboard.SetTargetProperty(rise, new PropertyPath("Y"));
        storyboard.Children.Add(rise);

        var drift = new DoubleAnimation
        {
            From = -horizontalDrift,
            To = horizontalDrift,
            Duration = TimeSpan.FromSeconds(idleSeconds / 2),
            BeginTime = beginTime,
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase
            {
                EasingMode = EasingMode.EaseInOut
            }
        };
        Storyboard.SetTargetName(drift, transformName);
        Storyboard.SetTargetProperty(drift, new PropertyPath("X"));
        storyboard.Children.Add(drift);

        var fade = new DoubleAnimationUsingKeyFrames
        {
            Duration = duration,
            BeginTime = beginTime,
            RepeatBehavior = RepeatBehavior.Forever
        };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(.82, KeyTime.FromPercent(.18)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(.62, KeyTime.FromPercent(.72)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        Storyboard.SetTargetName(fade, bubbleName);
        Storyboard.SetTargetProperty(
            fade,
            new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        return new MotionTrack(
            storyboard,
            idleSeconds / refreshingSeconds);
    }

    private static void AddOscillation(
        Storyboard storyboard,
        string targetName,
        string property,
        double from,
        double to,
        TimeSpan halfCycle)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = halfCycle,
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
            new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private sealed record MotionTrack(
        Storyboard Storyboard,
        double RefreshingSpeedRatio);
}
