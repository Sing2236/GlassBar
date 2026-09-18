using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class AudioVisualizerWindow : Window
{
    private readonly BarSettings _settings;
    private readonly SettingsService _settingsService = new();
    private readonly AudioMeterService _meter = new();
    private readonly DispatcherTimer _timer;
    private readonly List<Rectangle> _bars = [];
    private double[] _history = [];
    private double _smoothedPeak;

    public AudioVisualizerWindow(BarSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ApplyConfiguration();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(34) };
        _timer.Tick += (_, _) => DrawFrame();
        Loaded += (_, _) => PositionWindow();
        Closed += (_, _) => { _timer.Stop(); _meter.Dispose(); };
        _timer.Start();
    }

    private void BuildBars()
    {
        Bars.Items.Clear();
        _bars.Clear();
        var count = Math.Clamp(_settings.AudioVisualizerBarCount, 12, 64);
        _history = new double[count];
        var (start, end) = GetPalette();
        for (var index = 0; index < count; index++)
        {
            var bar = new Rectangle
            {
                Width = 5,
                Height = 4,
                Margin = new Thickness(2, 0, 2, 0),
                RadiusX = 3,
                RadiusY = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransform = new TranslateTransform(),
                Fill = new LinearGradientBrush(Color.FromArgb(235, start.R, start.G, start.B), Color.FromArgb(125, end.R, end.G, end.B), 90)
            };
            _bars.Add(bar);
            Bars.Items.Add(bar);
        }
    }

    public void ApplyConfiguration()
    {
        Width = Math.Clamp(_settings.AudioVisualizerWidth, 260, 720);
        Height = Math.Clamp(_settings.AudioVisualizerHeight, 96, 300);
        var accent = ColorConverter.ConvertFromString(_settings.Accent) is Color parsed
            ? parsed : Color.FromRgb(125, 211, 252);
        VisualizerGlow.Color = accent;
        VisualizerGlow.Opacity = Math.Clamp(_settings.AudioVisualizerGlow, 0, 1);
        VisualizerGlow.BlurRadius = 8 + 38 * Math.Clamp(_settings.AudioVisualizerGlow, 0, 1);
        VisualizerSurface.Background = _settings.AudioVisualizerBackground switch
        {
            "Transparent" => Brushes.Transparent,
            "Tint" => new SolidColorBrush(Color.FromArgb(34, accent.R, accent.G, accent.B)),
            _ => new SolidColorBrush(Color.FromArgb(92, 11, 18, 32))
        };
        VisualizerSurface.BorderBrush = _settings.AudioVisualizerBackground == "Transparent"
            ? new SolidColorBrush(Color.FromArgb(28, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(54, 255, 255, 255));
        BuildBars();
        PositionWindow();
    }

    private void DrawFrame()
    {
        if (_bars.Count == 0) return;
        var peak = Math.Clamp(Math.Pow(_meter.ReadPeak(), 0.62) * Math.Clamp(_settings.AudioVisualizerSensitivity, 0.5, 2), 0, 1.4);
        _smoothedPeak = Math.Max(peak, _smoothedPeak * Math.Clamp(_settings.AudioVisualizerSmoothing, 0.65, 0.96));
        Array.Copy(_history, 0, _history, 1, _history.Length - 1);
        _history[0] = _smoothedPeak;

        var now = DateTime.UtcNow.TimeOfDay.TotalMilliseconds;
        var availableHeight = Math.Max(24, ActualHeight - 42);
        for (var index = 0; index < _bars.Count; index++)
        {
            var bar = _bars[index];
            var sample = _history[Math.Min(_history.Length - 1, index / 2)];
            var wave = 0.68 + (Math.Sin((index * 0.78) + now * 0.012) + 1) * 0.16;
            var transform = (TranslateTransform)bar.RenderTransform;
            transform.Y = 0;
            bar.Width = _settings.AudioVisualizerMode == "Wave" ? 7 : 5;
            bar.RadiusX = bar.RadiusY = _settings.AudioVisualizerMode == "Wave" ? 4 : 3;

            switch (_settings.AudioVisualizerMode)
            {
                case "Mirror":
                    bar.VerticalAlignment = VerticalAlignment.Center;
                    bar.Height = 5 + Math.Clamp(sample * wave * availableHeight, 0, availableHeight);
                    break;
                case "Wave":
                    bar.VerticalAlignment = VerticalAlignment.Center;
                    bar.Height = 7;
                    transform.Y = Math.Sin(index * 0.62 + now * 0.008) * (6 + sample * availableHeight * 0.42);
                    break;
                case "Pulse":
                    bar.VerticalAlignment = VerticalAlignment.Center;
                    var distance = Math.Abs(index - (_bars.Count - 1) / 2d) / Math.Max(1, _bars.Count / 2d);
                    var pulse = (Math.Sin(now * 0.009 - distance * 4.2) + 1) / 2;
                    bar.Height = 5 + Math.Clamp(sample * pulse * availableHeight * (1.15 - distance * 0.45), 0, availableHeight);
                    break;
                case "Rain":
                    bar.VerticalAlignment = VerticalAlignment.Top;
                    bar.Height = 5 + Math.Clamp(sample * wave * availableHeight * 0.55, 0, availableHeight * 0.55);
                    transform.Y = (now * 0.045 + index * 19) % Math.Max(12, availableHeight * 0.55);
                    break;
                default:
                    bar.VerticalAlignment = VerticalAlignment.Bottom;
                    bar.Height = 4 + Math.Clamp(sample * wave * availableHeight, 0, availableHeight);
                    break;
            }
            bar.Opacity = 0.38 + Math.Clamp(sample * 1.5, 0, 0.62);
        }
    }

    private (Color Start, Color End) GetPalette()
    {
        var accent = ColorConverter.ConvertFromString(_settings.Accent) is Color parsed
            ? parsed : Color.FromRgb(125, 211, 252);
        return _settings.AudioVisualizerPalette switch
        {
            "Ocean" => (Color.FromRgb(56, 189, 248), Color.FromRgb(45, 212, 191)),
            "Sunset" => (Color.FromRgb(251, 113, 133), Color.FromRgb(251, 191, 36)),
            "Neon" => (Color.FromRgb(192, 132, 252), Color.FromRgb(34, 211, 238)),
            "Mono" => (Color.FromRgb(248, 250, 252), Color.FromRgb(148, 163, 184)),
            _ => (accent, Color.FromRgb(167, 139, 250))
        };
    }

    private void PositionWindow()
    {
        Left = _settings.AudioVisualizerX >= 0
            ? Math.Clamp(_settings.AudioVisualizerX, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width)
            : SystemParameters.PrimaryScreenWidth - Width - 28;
        Top = _settings.AudioVisualizerY >= 0
            ? Math.Clamp(_settings.AudioVisualizerY, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height)
            : 92;
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
            _settings.AudioVisualizerX = Left;
            _settings.AudioVisualizerY = Top;
            _settingsService.Save(_settings);
        }
        catch (InvalidOperationException) { }
    }

    private void CloseVisualizer_Click(object sender, RoutedEventArgs e)
    {
        _settings.AudioVisualizerEnabled = false;
        _settingsService.Save(_settings);
        Close();
    }
}
