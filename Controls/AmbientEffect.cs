using System.Windows;
using System.Windows.Media;

namespace GlassBar.Controls;

public sealed class AmbientEffect : FrameworkElement
{
    private readonly Random _random = new();
    private readonly List<RainDrop> _drops = [];
    private DateTime _lastFrame = DateTime.UtcNow;
    private double _phase;
    private string _mode = "Rain";
    private Color _accent = Color.FromRgb(125, 211, 252);

    public string Mode
    {
        get => _mode;
        set { _mode = value; InvalidateVisual(); }
    }

    public double Intensity { get; set; } = 0.72;

    public Color Accent
    {
        get => _accent;
        set { _accent = value; InvalidateVisual(); }
    }

    public AmbientEffect()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        _phase += elapsed;

        if (_mode == "Rain")
        {
            var target = Math.Max(8, (int)(ActualWidth / 38 * Intensity));
            while (_drops.Count < target) _drops.Add(NewDrop(initial: true));
            foreach (var drop in _drops)
            {
                drop.Y += drop.Speed * elapsed;
                if (drop.Y > ActualHeight + drop.Length) Reset(drop);
            }
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0 || _mode == "Off") return;

        if (_mode == "Aurora") DrawAurora(dc);
        else DrawRain(dc);
    }

    private void DrawRain(DrawingContext dc)
    {
        foreach (var drop in _drops)
        {
            var alpha = (byte)(38 + 92 * drop.Opacity * Intensity);
            var brush = new LinearGradientBrush(
                Color.FromArgb(8, 255, 255, 255),
                Color.FromArgb(alpha, 210, 242, 255), 90);
            var pen = new Pen(brush, drop.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, new Point(drop.X, drop.Y - drop.Length), new Point(drop.X, drop.Y));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(alpha * .55), 255, 255, 255)), null,
                new Point(drop.X - drop.Width * .3, drop.Y - 2), drop.Width * .45, drop.Width * .45);
        }
    }

    private void DrawAurora(DrawingContext dc)
    {
        var x1 = ActualWidth * (.28 + .12 * Math.Sin(_phase * .45));
        var x2 = ActualWidth * (.72 + .10 * Math.Cos(_phase * .37));
        DrawGlow(dc, new Point(x1, ActualHeight * .65), _accent, ActualWidth * .28);
        var second = Color.FromRgb(167, 139, 250);
        DrawGlow(dc, new Point(x2, ActualHeight * .35), second, ActualWidth * .24);
    }

    private void DrawGlow(DrawingContext dc, Point center, Color color, double radius)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb((byte)(80 * Intensity), color.R, color.G, color.B), 0),
                new(Color.FromArgb(0, color.R, color.G, color.B), 1)
            }
        };
        dc.DrawEllipse(brush, null, center, radius, radius * .42);
    }

    private RainDrop NewDrop(bool initial) => new()
    {
        X = _random.NextDouble() * Math.Max(1, ActualWidth),
        Y = initial ? _random.NextDouble() * Math.Max(1, ActualHeight) : -10,
        Speed = 34 + _random.NextDouble() * 68,
        Length = 5 + _random.NextDouble() * 13,
        Width = .7 + _random.NextDouble() * 1.3,
        Opacity = .35 + _random.NextDouble() * .65
    };

    private void Reset(RainDrop drop)
    {
        var next = NewDrop(initial: false);
        drop.X = next.X; drop.Y = next.Y; drop.Speed = next.Speed;
        drop.Length = next.Length; drop.Width = next.Width; drop.Opacity = next.Opacity;
    }

    private sealed class RainDrop
    {
        public double X, Y, Speed, Length, Width, Opacity;
    }
}
