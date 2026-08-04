using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Skins.Contracts;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace CodexQuotaHud.Skins.Templates.FreeDecorationRing;

public partial class FreeDecorationRingRenderer : CustomSkinRenderer
{
    private const int IdleFrameRate = 4;
    private const int RefreshingFrameRate = 24;

    private readonly List<AnimationTrack> _animationTracks = [];
    private readonly SlotTransforms _backgroundTransforms;
    private readonly SlotTransforms _centerTransforms;
    private readonly SlotTransforms _decorationTransforms;
    private bool _animationsStarted;

    internal FreeDecorationRingRenderer(SkinPackageDocument package)
    {
        ArgumentNullException.ThrowIfNull(package);
        InitializeComponent();

        var theme = package.Theme;
        BackgroundImage.Fill = CreateImageBrush(
            package,
            SkinAssetSlot.Background,
            theme.Background);
        CenterImage.Fill = CreateImageBrush(
            package,
            SkinAssetSlot.Center,
            theme.Center);
        DecorationImage.Fill = CreateImageBrush(
            package,
            SkinAssetSlot.Decoration,
            theme.Decoration);

        _backgroundTransforms = ApplyTransform(BackgroundImage, theme.Background);
        _centerTransforms = ApplyTransform(CenterImage, theme.Center);
        _decorationTransforms = ApplyTransform(DecorationImage, theme.Decoration);

        ApplyTheme(theme);
        ConfigureAnimations(theme.Animation);
    }

    internal int AnimationTrackCount => _animationTracks.Count;

    internal IReadOnlyList<int?> ConfiguredFrameRates =>
        _animationTracks
            .Select(track => Timeline.GetDesiredFrameRate(track.Storyboard))
            .ToArray();

    public override void Render(CustomSkinRenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Mode == QuotaDisplayMode.Hidden)
        {
            SetQuotaVisibility(Visibility.Collapsed, Visibility.Collapsed);
            return;
        }

        var secondaryVisibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetQuotaVisibility(Visibility.Visible, secondaryVisibility);

        PrimaryProgress.SweepAngle = state.PrimaryPercent * 3.6;
        SecondaryProgress.SweepAngle = (state.SecondaryPercent ?? 0) * 3.6;
        PrimaryProgress.Stroke = CreateFrozenBrush(state.PrimaryRingColor);
        SecondaryProgress.Stroke = CreateFrozenBrush(
            state.SecondaryRingColor ?? Colors.Transparent);
        QuotaNumber.Text = $"{state.PrimaryPercent:0}%";
        QuotaLabel.Text = state.PrimaryLabel ?? string.Empty;
    }

    public override void ApplyAnimationState(
        CustomSkinAnimationState state,
        bool globalAnimationsEnabled)
    {
        if (state == CustomSkinAnimationState.Hidden ||
            !globalAnimationsEnabled ||
            _animationTracks.Count == 0)
        {
            StopAnimations();
            return;
        }

        var desiredFrameRate = state == CustomSkinAnimationState.Refreshing
            ? RefreshingFrameRate
            : IdleFrameRate;
        var speedRatio = state == CustomSkinAnimationState.Refreshing ? 2d : 1d;
        StartAnimations(desiredFrameRate, speedRatio);
    }

    private void ApplyTheme(SkinTheme theme)
    {
        BaseFill.Fill = CreateFrozenBrush(ParseColor(theme.BaseBackgroundColor));
        BaseFill.Opacity = theme.BaseBackgroundOpacity;
        BaseFill.Effect = new DropShadowEffect
        {
            Color = ParseColor(theme.GlowColor),
            BlurRadius = 18,
            ShadowDepth = 0,
            Opacity = theme.GlowIntensity
        };

        var primaryColor = ParseColor(theme.PrimaryRingColor);
        var secondaryColor = ParseColor(theme.SecondaryRingColor);
        var glowColor = ParseColor(theme.GlowColor);
        PrimaryTrack.Stroke = CreateTrackBrush(primaryColor);
        SecondaryTrack.Stroke = CreateTrackBrush(secondaryColor);
        PrimaryProgress.Stroke = CreateFrozenBrush(primaryColor);
        SecondaryProgress.Stroke = CreateFrozenBrush(secondaryColor);
        AnimatedGlow.Stroke = CreateFrozenBrush(glowColor);
        AnimatedGlow.Effect = new DropShadowEffect
        {
            Color = glowColor,
            BlurRadius = 18,
            ShadowDepth = 0,
            Opacity = theme.GlowIntensity
        };

        SetRingMetrics(PrimaryTrack, PrimaryProgress, theme.RingDiameter, theme);
        AnimatedGlow.Width = theme.RingDiameter;
        AnimatedGlow.Height = theme.RingDiameter;
        AnimatedGlow.StrokeThickness = theme.RingThickness;
        var secondaryDiameter = Math.Max(
            2 * theme.RingThickness,
            theme.RingDiameter -
            (2 * (theme.RingThickness + theme.RingGap)));
        SetRingMetrics(SecondaryTrack, SecondaryProgress, secondaryDiameter, theme);

        QuotaNumber.FontSize = theme.NumberTextSize;
        QuotaLabel.FontSize = theme.LabelTextSize;
        var weight = theme.TextWeight switch
        {
            SkinTextWeight.Regular => FontWeights.Normal,
            SkinTextWeight.SemiBold => FontWeights.SemiBold,
            SkinTextWeight.Bold => FontWeights.Bold,
            _ => FontWeights.Normal
        };
        QuotaNumber.FontWeight = weight;
        QuotaLabel.FontWeight = weight;
        ApplyTextPlacement(theme.TextPlacement);
    }

    private static void SetRingMetrics(
        System.Windows.Shapes.Ellipse track,
        FreeDecorationRingArc progress,
        double diameter,
        SkinTheme theme)
    {
        track.Width = diameter;
        track.Height = diameter;
        track.StrokeThickness = theme.RingThickness;
        progress.Width = diameter;
        progress.Height = diameter;
        progress.StrokeThickness = theme.RingThickness;
        progress.StartAngle = theme.StartAngle;
    }

    private void ApplyTextPlacement(SkinTextPlacement placement)
    {
        switch (placement)
        {
            case SkinTextPlacement.LabelAboveNumber:
                QuotaLabel.Margin = new Thickness(0, -22, 0, 0);
                QuotaNumber.Margin = new Thickness(0, 18, 0, 0);
                break;
            case SkinTextPlacement.NumberAboveLabel:
                QuotaNumber.Margin = new Thickness(0, -18, 0, 0);
                QuotaLabel.Margin = new Thickness(0, 25, 0, 0);
                break;
            default:
                QuotaNumber.Margin = new Thickness(0);
                QuotaLabel.Margin = new Thickness(0, 26, 0, 0);
                break;
        }
    }

    private void SetQuotaVisibility(
        Visibility primaryVisibility,
        Visibility secondaryVisibility)
    {
        PrimaryTrack.Visibility = primaryVisibility;
        PrimaryProgress.Visibility = primaryVisibility;
        QuotaNumber.Visibility = primaryVisibility;
        QuotaLabel.Visibility = primaryVisibility;
        SecondaryTrack.Visibility = secondaryVisibility;
        SecondaryProgress.Visibility = secondaryVisibility;
    }

    private static ImageBrush? CreateImageBrush(
        SkinPackageDocument package,
        SkinAssetSlot slot,
        SkinImageTransform transform)
    {
        if (!package.Assets.TryGetValue(slot, out var asset))
        {
            return null;
        }

        using var stream = new MemoryStream(asset.Content, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = CalculateCropViewbox(asset, transform),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 1, 1)
        };
        brush.Freeze();
        return brush;
    }

    private static Rect CalculateCropViewbox(
        SkinAsset asset,
        SkinImageTransform transform)
    {
        if (asset.PixelWidth <= 0 || asset.PixelHeight <= 0)
        {
            return new Rect(0, 0, 1, 1);
        }

        var aspect = (double)asset.PixelWidth / asset.PixelHeight;
        if (aspect > 1)
        {
            var visibleWidth = 1 / aspect;
            return new Rect(
                (1 - visibleWidth) * transform.CropFocusX,
                0,
                visibleWidth,
                1);
        }

        if (aspect < 1)
        {
            var visibleHeight = aspect;
            return new Rect(
                0,
                (1 - visibleHeight) * transform.CropFocusY,
                1,
                visibleHeight);
        }

        return new Rect(0, 0, 1, 1);
    }

    private static SlotTransforms ApplyTransform(
        FrameworkElement element,
        SkinImageTransform settings)
    {
        var scale = new ScaleTransform(settings.Scale, settings.Scale);
        var rotate = new RotateTransform(settings.Rotation);
        var translate = new TranslateTransform(settings.OffsetX, settings.OffsetY);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        group.Children.Add(translate);
        element.RenderTransform = group;
        element.Opacity = settings.Opacity;
        return new SlotTransforms(
            scale,
            rotate,
            translate,
            settings.Scale,
            settings.Rotation,
            settings.OffsetX,
            settings.OffsetY);
    }

    private void ConfigureAnimations(SkinAnimationSettings settings)
    {
        if (settings.RotationIntensity > 0)
        {
            var rotation = new DoubleAnimation
            {
                From = _decorationTransforms.BaseRotation,
                To = _decorationTransforms.BaseRotation + 360,
                Duration = TimeSpan.FromSeconds(30 / settings.RotationIntensity),
                RepeatBehavior = RepeatBehavior.Forever
            };
            _animationTracks.Add(CreateTrack(
                _decorationTransforms.Rotate,
                RotateTransform.AngleProperty,
                rotation,
                settings.RotationIntensity));
        }

        if (settings.BreathingIntensity > 0)
        {
            var range = FreeDecorationRingMotionProfile.Breathing(
                _centerTransforms.BaseScale,
                settings.BreathingIntensity);
            var scaleX = CreatePulse(range);
            var scaleY = CreatePulse(range);
            var storyboard = new Storyboard();
            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            _animationTracks.Add(new AnimationTrack(
                storyboard,
                settings.BreathingIntensity,
                [
                    new AnimationBinding(
                        _centerTransforms.Scale,
                        ScaleTransform.ScaleXProperty,
                        scaleX),
                    new AnimationBinding(
                        _centerTransforms.Scale,
                        ScaleTransform.ScaleYProperty,
                        scaleY)
                ]));
        }

        if (settings.GlowIntensity > 0)
        {
            var glow = CreatePulse(
                FreeDecorationRingMotionProfile.Glow(settings.GlowIntensity));
            _animationTracks.Add(CreateTrack(
                AnimatedGlow,
                OpacityProperty,
                glow,
                settings.GlowIntensity));
        }

        if (settings.FloatingIntensity > 0)
        {
            var floating = CreatePulse(new AnimationRange(
                _decorationTransforms.BaseY - (2 * settings.FloatingIntensity),
                _decorationTransforms.BaseY + (2 * settings.FloatingIntensity),
                2.5 / settings.FloatingIntensity));
            _animationTracks.Add(CreateTrack(
                _decorationTransforms.Translate,
                TranslateTransform.YProperty,
                floating,
                settings.FloatingIntensity));
        }
    }

    private static DoubleAnimation CreatePulse(AnimationRange range) =>
        new()
        {
            From = range.From,
            To = range.To,
            Duration = TimeSpan.FromSeconds(range.HalfCycleSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

    private static AnimationTrack CreateTrack(
        DependencyObject target,
        DependencyProperty property,
        DoubleAnimation animation,
        double intensity)
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return new AnimationTrack(
            storyboard,
            intensity,
            [new AnimationBinding(target, property, animation)]);
    }

    private void StartAnimations(int desiredFrameRate, double speedRatio)
    {
        if (_animationsStarted)
        {
            RemoveAnimationClocks();
        }

        foreach (var track in _animationTracks)
        {
            Timeline.SetDesiredFrameRate(track.Storyboard, desiredFrameRate);
            var scaledSpeed = 1 + ((speedRatio - 1) * track.Intensity);
            var storyboard = track.Storyboard.Clone();
            storyboard.SpeedRatio = scaledSpeed;
            Timeline.SetDesiredFrameRate(storyboard, desiredFrameRate);
            var clockGroup = (ClockGroup)storyboard.CreateClock(true);
            if (clockGroup.Children.Count != track.Bindings.Count)
            {
                throw new InvalidOperationException(
                    "Storyboard animation bindings are inconsistent.");
            }

            for (var index = 0; index < track.Bindings.Count; index++)
            {
                var binding = track.Bindings[index];
                var clock = (AnimationClock)clockGroup.Children[index];
                ApplyAnimationClock(binding.Target, binding.Property, clock);
            }

            track.ActiveClock = clockGroup;
            clockGroup.Controller!.Begin();
            clockGroup.Controller.SeekAlignedToLastTick(
                TimeSpan.Zero,
                TimeSeekOrigin.BeginTime);
        }

        _animationsStarted = true;
        HasActiveAnimations = true;
        DesiredFrameRate = desiredFrameRate;
    }

    private void StopAnimations()
    {
        if (_animationsStarted)
        {
            RemoveAnimationClocks();
        }

        _animationsStarted = false;
        HasActiveAnimations = false;
        DesiredFrameRate = null;
        ResetTransforms();
    }

    private void RemoveAnimationClocks()
    {
        foreach (var track in _animationTracks)
        {
            foreach (var binding in track.Bindings)
            {
                ApplyAnimationClock(binding.Target, binding.Property, null);
            }

            track.ActiveClock?.Controller?.Remove();
            track.ActiveClock = null;
        }
    }

    private static void ApplyAnimationClock(
        DependencyObject target,
        DependencyProperty property,
        AnimationClock? clock)
    {
        switch (target)
        {
            case Animatable animatable:
                animatable.ApplyAnimationClock(
                    property,
                    clock,
                    HandoffBehavior.SnapshotAndReplace);
                break;
            case UIElement element:
                element.ApplyAnimationClock(
                    property,
                    clock,
                    HandoffBehavior.SnapshotAndReplace);
                break;
            default:
                throw new InvalidOperationException(
                    $"Animation target '{target.GetType().Name}' is not animatable.");
        }
    }

    private void ResetTransforms()
    {
        Reset(_backgroundTransforms);
        Reset(_centerTransforms);
        Reset(_decorationTransforms);
        AnimatedGlow.BeginAnimation(OpacityProperty, null);
        AnimatedGlow.Opacity = 0;
    }

    private static void Reset(SlotTransforms transforms)
    {
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transforms.Rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        transforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        transforms.Translate.BeginAnimation(TranslateTransform.YProperty, null);
        transforms.Scale.ScaleX = transforms.BaseScale;
        transforms.Scale.ScaleY = transforms.BaseScale;
        transforms.Rotate.Angle = transforms.BaseRotation;
        transforms.Translate.X = transforms.BaseX;
        transforms.Translate.Y = transforms.BaseY;
    }

    private static Color ParseColor(string value) =>
        (Color)ColorConverter.ConvertFromString(value)!;

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateTrackBrush(Color color)
    {
        color.A = Math.Min(color.A, (byte)56);
        return CreateFrozenBrush(color);
    }

    private sealed record SlotTransforms(
        ScaleTransform Scale,
        RotateTransform Rotate,
        TranslateTransform Translate,
        double BaseScale,
        double BaseRotation,
        double BaseX,
        double BaseY);

    private sealed class AnimationTrack(
        Storyboard storyboard,
        double intensity,
        IReadOnlyList<AnimationBinding> bindings)
    {
        public Storyboard Storyboard { get; } = storyboard;

        public double Intensity { get; } = intensity;

        public IReadOnlyList<AnimationBinding> Bindings { get; } = bindings;

        public ClockGroup? ActiveClock { get; set; }
    }

    private sealed class AnimationBinding(
        DependencyObject target,
        DependencyProperty property,
        AnimationTimeline animation)
    {
        public DependencyObject Target { get; } = target;

        public DependencyProperty Property { get; } = property;

        public AnimationTimeline Animation { get; } = animation;
    }
}

public sealed class FreeDecorationRingArc : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(FreeDecorationRingArc),
            new FrameworkPropertyMetadata(
                Brushes.Transparent,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(FreeDecorationRingArc),
            new FrameworkPropertyMetadata(
                4d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StartAngleProperty =
        DependencyProperty.Register(
            nameof(StartAngle),
            typeof(double),
            typeof(FreeDecorationRingArc),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SweepAngleProperty =
        DependencyProperty.Register(
            nameof(SweepAngle),
            typeof(double),
            typeof(FreeDecorationRingArc),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double StartAngle
    {
        get => (double)GetValue(StartAngleProperty);
        set => SetValue(StartAngleProperty, value);
    }

    public double SweepAngle
    {
        get => (double)GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var thickness = Math.Max(0, StrokeThickness);
        var radius = Math.Max(
            0,
            (Math.Min(ActualWidth, ActualHeight) - thickness) / 2);
        var sweep = Math.Clamp(SweepAngle, 0, 360);
        if (radius <= 0 || sweep <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var pen = new Pen(Stroke, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (sweep >= 360)
        {
            drawingContext.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var start = PointOnCircle(center, radius, StartAngle);
        var end = PointOnCircle(center, radius, StartAngle + sweep);
        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = sweep > 180,
            SweepDirection = SweepDirection.Clockwise
        });
        drawingContext.DrawGeometry(
            null,
            pen,
            new PathGeometry([figure]));
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }
}
