using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace CodexQuotaHud.App.UI.Controls;

public sealed class ProgressArc : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(ProgressArc),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(ProgressArc),
            new FrameworkPropertyMetadata(
                Brushes.DeepSkyBlue,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackStrokeProperty =
        DependencyProperty.Register(
            nameof(TrackStroke),
            typeof(Brush),
            typeof(ProgressArc),
            new FrameworkPropertyMetadata(
                Brushes.Transparent,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(ProgressArc),
            new FrameworkPropertyMetadata(
                4d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StartAngleProperty =
        DependencyProperty.Register(
            nameof(StartAngle),
            typeof(double),
            typeof(ProgressArc),
            new FrameworkPropertyMetadata(
                -90d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush TrackStroke
    {
        get => (Brush)GetValue(TrackStrokeProperty);
        set => SetValue(TrackStrokeProperty, value);
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

    public static double CalculateSweepAngle(double progress) =>
        Math.Clamp(progress, 0, 100) >= 100
            ? 359.999
            : Math.Clamp(progress, 0, 100) * 3.6;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var thickness = Math.Max(0, StrokeThickness);
        var radius =
            Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2);
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var trackPen = new Pen(TrackStroke, thickness);
        drawingContext.DrawEllipse(
            brush: null,
            trackPen,
            center,
            radius,
            radius);

        var sweep = CalculateSweepAngle(Progress);
        if (sweep <= 0)
        {
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
        var geometry = new PathGeometry([figure]);
        var pen = new Pen(Stroke, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(
        Point center,
        double radius,
        double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
