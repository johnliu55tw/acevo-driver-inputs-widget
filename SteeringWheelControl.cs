using System.Windows;
using System.Windows.Media;

namespace ACEvo_Simple_Telemetry;

public sealed class SteeringWheelControl : FrameworkElement
{
    public static readonly DependencyProperty AngleDegreesProperty = DependencyProperty.Register(
        nameof(AngleDegrees), typeof(double), typeof(SteeringWheelControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double AngleDegrees
    {
        get => (double)GetValue(AngleDegreesProperty);
        set => SetValue(AngleDegreesProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double size = Math.Min(ActualWidth, ActualHeight);
        Point center = new(ActualWidth / 2.0, ActualHeight / 2.0);
        double radius = Math.Max(0, size / 2.0 - 7.0);
        Pen rimPen = new(FindBrush("WheelRimBrush", Color.FromRgb(225, 225, 228)), 8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        Pen spokePen = new(FindBrush("WheelSpokeBrush", Color.FromRgb(182, 182, 188)), 7)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        Pen markerPen = new(new SolidColorBrush(Color.FromRgb(46, 208, 110)), 8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        drawingContext.DrawEllipse(FindBrush("WheelBackgroundBrush", Color.FromRgb(24, 24, 28)), rimPen, center, radius, radius);
        drawingContext.PushTransform(new RotateTransform(AngleDegrees, center.X, center.Y));
        drawingContext.DrawLine(spokePen, center, new Point(center.X, center.Y - radius + 7));
        drawingContext.DrawLine(spokePen, center, PointOnCircle(center, radius - 7, 150));
        drawingContext.DrawLine(spokePen, center, PointOnCircle(center, radius - 7, 30));
        drawingContext.DrawLine(markerPen,
            new Point(center.X, center.Y - radius - 1),
            new Point(center.X, center.Y - radius + 16));
        drawingContext.Pop();

        drawingContext.DrawEllipse(FindBrush("WheelHubBrush", Color.FromRgb(45, 45, 50)), null, center, 15, 15);
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
    }

    private Brush FindBrush(string resourceKey, Color fallback) =>
        TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);
}
