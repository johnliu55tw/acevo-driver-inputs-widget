using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace ACEvo_Simple_Telemetry;

public sealed class PedalGraphControl : FrameworkElement
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Pen ThrottlePen = CreatePen(Color.FromRgb(46, 208, 110));
    private static readonly Pen BrakePen = CreatePen(Color.FromRgb(255, 75, 85));
    private static readonly Pen ClutchPen = CreatePen(Color.FromRgb(76, 166, 255));

    private readonly List<GraphSample> _samples = [];
    private bool _showThrottle = true;
    private bool _showBrake = true;
    private bool _showClutch = true;
    private double _timeSpanSeconds = 10;

    public bool ShowThrottle
    {
        get => _showThrottle;
        set { _showThrottle = value; InvalidateVisual(); }
    }

    public bool ShowBrake
    {
        get => _showBrake;
        set { _showBrake = value; InvalidateVisual(); }
    }

    public bool ShowClutch
    {
        get => _showClutch;
        set { _showClutch = value; InvalidateVisual(); }
    }

    public double TimeSpanSeconds
    {
        get => _timeSpanSeconds;
        set { _timeSpanSeconds = Math.Clamp(value, 5, 30); Trim(Clock.Elapsed.TotalSeconds); InvalidateVisual(); }
    }

    public void AddSample(float throttle, float brake, float clutch)
    {
        double now = Clock.Elapsed.TotalSeconds;
        _samples.Add(new GraphSample(now, throttle, brake, clutch));
        Trim(now);
        InvalidateVisual();
    }

    public void Clear()
    {
        _samples.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Rect bounds = new(0, 0, ActualWidth, ActualHeight);
        Brush background = FindBrush("GraphBackgroundBrush", Color.FromRgb(20, 20, 23));
        Pen gridPen = new(FindBrush("GraphGridBrush", Color.FromArgb(70, 90, 90, 96)), 1);
        drawingContext.DrawRoundedRectangle(background, null, bounds, 4, 4);

        if (ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        for (int i = 1; i < 4; i++)
        {
            double y = ActualHeight * i / 4.0;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(ActualWidth, y));
        }

        for (int i = 1; i < 5; i++)
        {
            double x = ActualWidth * i / 5.0;
            drawingContext.DrawLine(gridPen, new Point(x, 0), new Point(x, ActualHeight));
        }

        double now = Clock.Elapsed.TotalSeconds;
        drawingContext.PushClip(new RectangleGeometry(bounds));
        if (ShowThrottle) DrawSeries(drawingContext, now, static s => s.Throttle, ThrottlePen);
        if (ShowBrake) DrawSeries(drawingContext, now, static s => s.Brake, BrakePen);
        if (ShowClutch) DrawSeries(drawingContext, now, static s => s.Clutch, ClutchPen);
        drawingContext.Pop();
    }

    private void DrawSeries(DrawingContext context, double now, Func<GraphSample, float> selector, Pen pen)
    {
        if (_samples.Count < 2)
        {
            return;
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext path = geometry.Open())
        {
            bool started = false;
            foreach (GraphSample sample in _samples)
            {
                double age = now - sample.Time;
                if (age > TimeSpanSeconds)
                {
                    continue;
                }

                double x = ActualWidth * (1.0 - age / TimeSpanSeconds);
                double y = ActualHeight * (1.0 - Math.Clamp(selector(sample), 0f, 1f));
                Point point = new(x, y);
                if (!started)
                {
                    path.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    path.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private void Trim(double now)
    {
        double oldest = now - TimeSpanSeconds - 0.25;
        int removeCount = 0;
        while (removeCount < _samples.Count && _samples[removeCount].Time < oldest)
        {
            removeCount++;
        }

        if (removeCount > 0)
        {
            _samples.RemoveRange(0, removeCount);
        }
    }

    private static Pen CreatePen(Color color)
    {
        Pen pen = new(new SolidColorBrush(color), 2.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private Brush FindBrush(string resourceKey, Color fallback) =>
        TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);

    private readonly record struct GraphSample(double Time, float Throttle, float Brake, float Clutch);
}
