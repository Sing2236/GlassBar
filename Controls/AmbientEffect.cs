using System.Windows;
using System.Windows.Media;
using GlassBar.Models;

namespace GlassBar.Controls;

public sealed class AmbientEffect : FrameworkElement
{
    private readonly Random _random = new();
    private readonly List<Particle> _particles = [];
    private DateTime _lastFrame = DateTime.UtcNow;
    private double _phase;
    private double _intensity = 0.72;
    private string _mode = "Rain";
    private string _particleMode = "";
    private Color _accent = Color.FromRgb(125, 211, 252);

    public string Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            _particles.Clear();
            _particleMode = "";
            InvalidateVisual();
        }
    }

    public double Intensity
    {
        get => _intensity;
        set { _intensity = Math.Clamp(value, 0.05, 1); InvalidateVisual(); }
    }

    public Color Accent
    {
        get => _accent;
        set { _accent = value; InvalidateVisual(); }
    }

    public CustomEffectConfig CustomEffect { get; set; } = new();

    public void RefreshCustomEffect(bool rebuildParticles = false)
    {
        if (rebuildParticles) _particles.Clear();
        InvalidateVisual();
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

        if (UsesParticles(_mode))
        {
            if (_particleMode != _mode)
            {
                _particles.Clear();
                _particleMode = _mode;
            }

            var target = GetParticleTarget();
            while (_particles.Count < target) _particles.Add(NewParticle(initial: true));
            while (_particles.Count > target) _particles.RemoveAt(_particles.Count - 1);
            foreach (var particle in _particles) UpdateParticle(particle, elapsed);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0 || _mode == "Off") return;

        switch (_mode)
        {
            case "Aurora": DrawAurora(dc); break;
            case "Pulse": DrawPulse(dc); break;
            case "Snow": DrawSnow(dc); break;
            case "Fireflies": DrawFireflies(dc); break;
            case "Custom": DrawCustom(dc); break;
            default: DrawRain(dc); break;
        }
    }

    private static bool UsesParticles(string mode) => mode is "Rain" or "Snow" or "Fireflies" or "Custom";

    private int GetParticleTarget()
    {
        var factor = _mode switch
        {
            "Snow" => 30,
            "Fireflies" => 52,
            "Custom" => 33 / Math.Max(0.2, CustomEffect.Density),
            _ => 38
        };
        return Math.Clamp((int)(ActualWidth / factor * Intensity), 6, 72);
    }

    private void UpdateParticle(Particle particle, double elapsed)
    {
        particle.Phase += elapsed * (0.7 + particle.Speed / 80);
        switch (_mode)
        {
            case "Rain":
                particle.Y += particle.Speed * elapsed;
                particle.X += 7 * elapsed;
                if (particle.Y > ActualHeight + particle.Length) ResetParticle(particle, false);
                break;
            case "Snow":
                particle.Y += particle.Speed * 0.24 * elapsed;
                particle.X += Math.Sin(particle.Phase) * 9 * elapsed;
                if (particle.Y > ActualHeight + 5) ResetParticle(particle, false);
                WrapHorizontal(particle);
                break;
            case "Fireflies":
                particle.X += Math.Cos(particle.Phase * 1.3) * particle.Speed * 0.055 * elapsed;
                particle.Y += Math.Sin(particle.Phase) * particle.Speed * 0.04 * elapsed;
                Wrap(particle);
                break;
            case "Custom":
                UpdateCustomParticle(particle, elapsed);
                break;
        }
    }

    private void UpdateCustomParticle(Particle particle, double elapsed)
    {
        var velocity = (18 + 105 * CustomEffect.Speed) * elapsed;
        switch (CustomEffect.Motion)
        {
            case "Fall": particle.Y += velocity; particle.X += Math.Sin(particle.Phase) * 5 * elapsed; break;
            case "Rise": particle.Y -= velocity; particle.X += Math.Sin(particle.Phase) * 5 * elapsed; break;
            case "Drift": particle.X += velocity; particle.Y += Math.Sin(particle.Phase) * 7 * elapsed; break;
            default:
                particle.X += Math.Cos(particle.Phase * 1.2) * velocity * 0.16;
                particle.Y += Math.Sin(particle.Phase) * velocity * 0.12;
                break;
        }
        Wrap(particle);
    }

    private void DrawRain(DrawingContext dc)
    {
        foreach (var particle in _particles)
        {
            var alpha = (byte)(38 + 92 * particle.Opacity * Intensity);
            var brush = new LinearGradientBrush(Color.FromArgb(8, 255, 255, 255), Color.FromArgb(alpha, 210, 242, 255), 90);
            var pen = new Pen(brush, particle.Size) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, new Point(particle.X - 2, particle.Y - particle.Length), new Point(particle.X, particle.Y));
        }
    }

    private void DrawAurora(DrawingContext dc)
    {
        var x1 = ActualWidth * (.28 + .12 * Math.Sin(_phase * .45));
        var x2 = ActualWidth * (.72 + .10 * Math.Cos(_phase * .37));
        DrawGlow(dc, new Point(x1, ActualHeight * .65), _accent, ActualWidth * .28, 0.8);
        DrawGlow(dc, new Point(x2, ActualHeight * .35), Color.FromRgb(167, 139, 250), ActualWidth * .24, 0.72);
    }

    private void DrawPulse(DrawingContext dc)
    {
        var pulse = 0.72 + 0.18 * Math.Sin(_phase * 1.8);
        DrawGlow(dc, new Point(ActualWidth * (.5 + .16 * Math.Sin(_phase * .38)), ActualHeight * .5), _accent,
            ActualWidth * .38 * pulse, 0.72);
        DrawGlow(dc, new Point(ActualWidth * (.18 + .05 * Math.Cos(_phase * .5)), ActualHeight * .2),
            Color.FromRgb(52, 211, 153), ActualWidth * .2, 0.45);
    }

    private void DrawSnow(DrawingContext dc)
    {
        foreach (var particle in _particles)
        {
            var alpha = (byte)(45 + 120 * particle.Opacity * Intensity);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, 235, 249, 255)), null,
                new Point(particle.X, particle.Y), particle.Size * 1.35, particle.Size * 1.35);
        }
    }

    private void DrawFireflies(DrawingContext dc)
    {
        foreach (var particle in _particles)
        {
            var shimmer = 0.45 + 0.55 * Math.Abs(Math.Sin(particle.Phase * 1.7));
            var color = particle.Variant ? _accent : Color.FromRgb(253, 224, 71);
            DrawGlow(dc, new Point(particle.X, particle.Y), color, 8 + particle.Size * 4, shimmer * 0.62);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(185 * shimmer * Intensity), color.R, color.G, color.B)),
                null, new Point(particle.X, particle.Y), particle.Size, particle.Size);
        }
    }

    private void DrawCustom(DrawingContext dc)
    {
        var second = ParseColor(CustomEffect.SecondaryColor, Color.FromRgb(167, 139, 250));
        foreach (var particle in _particles)
        {
            var color = particle.Variant ? _accent : second;
            var size = 1 + CustomEffect.Size * 5.5 * particle.Size;
            var alpha = (byte)Math.Clamp(55 + 150 * particle.Opacity * Intensity, 0, 255);
            if (CustomEffect.Glow > 0.05)
                DrawGlow(dc, new Point(particle.X, particle.Y), color, size * (1.8 + 2.5 * CustomEffect.Glow), CustomEffect.Glow * .5);

            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            switch (CustomEffect.Shape)
            {
                case "Spark":
                    var sparkPen = new Pen(brush, Math.Max(0.8, size * .22)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawLine(sparkPen, new Point(particle.X - size, particle.Y), new Point(particle.X + size, particle.Y));
                    dc.DrawLine(sparkPen, new Point(particle.X, particle.Y - size), new Point(particle.X, particle.Y + size));
                    break;
                case "Drop":
                    var trail = size * (1.2 + 4 * CustomEffect.Trail);
                    var dropPen = new Pen(brush, Math.Max(0.8, size * .24)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawLine(dropPen, new Point(particle.X, particle.Y - trail), new Point(particle.X, particle.Y));
                    break;
                case "Diamond":
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        context.BeginFigure(new Point(particle.X, particle.Y - size), true, true);
                        context.LineTo(new Point(particle.X + size, particle.Y), true, false);
                        context.LineTo(new Point(particle.X, particle.Y + size), true, false);
                        context.LineTo(new Point(particle.X - size, particle.Y), true, false);
                    }
                    geometry.Freeze();
                    dc.DrawGeometry(brush, null, geometry);
                    break;
                default:
                    dc.DrawEllipse(brush, null, new Point(particle.X, particle.Y), size, size);
                    break;
            }
        }
    }

    private void DrawGlow(DrawingContext dc, Point center, Color color, double radius, double opacity)
    {
        var alpha = (byte)Math.Clamp(80 * Intensity * opacity, 0, 255);
        var brush = new RadialGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(alpha, color.R, color.G, color.B), 0),
                new(Color.FromArgb(0, color.R, color.G, color.B), 1)
            }
        };
        dc.DrawEllipse(brush, null, center, radius, radius * .42);
    }

    private Particle NewParticle(bool initial) => new()
    {
        X = _random.NextDouble() * Math.Max(1, ActualWidth),
        Y = initial ? _random.NextDouble() * Math.Max(1, ActualHeight) : -12,
        Speed = 34 + _random.NextDouble() * 68,
        Length = 5 + _random.NextDouble() * 13,
        Size = .65 + _random.NextDouble() * 1.15,
        Opacity = .35 + _random.NextDouble() * .65,
        Phase = _random.NextDouble() * Math.PI * 2,
        Variant = _random.Next(2) == 0
    };

    private void ResetParticle(Particle particle, bool initial)
    {
        var next = NewParticle(initial);
        particle.X = next.X; particle.Y = next.Y; particle.Speed = next.Speed;
        particle.Length = next.Length; particle.Size = next.Size; particle.Opacity = next.Opacity;
        particle.Phase = next.Phase; particle.Variant = next.Variant;
    }

    private void Wrap(Particle particle)
    {
        if (particle.X < -10) particle.X = ActualWidth + 10;
        if (particle.X > ActualWidth + 10) particle.X = -10;
        if (particle.Y < -12) particle.Y = ActualHeight + 12;
        if (particle.Y > ActualHeight + 12) particle.Y = -12;
    }

    private void WrapHorizontal(Particle particle)
    {
        if (particle.X < -8) particle.X = ActualWidth + 8;
        if (particle.X > ActualWidth + 8) particle.X = -8;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return fallback; }
    }

    private sealed class Particle
    {
        public double X, Y, Speed, Length, Size, Opacity, Phase;
        public bool Variant;
    }
}
