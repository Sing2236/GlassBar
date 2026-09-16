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
    private const int BarCount = 30;
    private readonly BarSettings _settings;
    private readonly SettingsService _settingsService = new();
    private readonly AudioMeterService _meter = new();
    private readonly DispatcherTimer _timer;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly double[] _history = new double[BarCount];
    private double _smoothedPeak;

    public AudioVisualizerWindow(BarSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        BuildBars();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(34) };
        _timer.Tick += (_, _) => DrawFrame();
        Loaded += (_, _) => PositionWindow();
        Closed += (_, _) => { _timer.Stop(); _meter.Dispose(); };
        _timer.Start();
    }

    private void BuildBars()
    {
        var accent = ColorConverter.ConvertFromString(_settings.Accent) is Color color ? color : Color.FromRgb(125, 211, 252);
        for (var index = 0; index < BarCount; index++)
        {
            var bar = new Rectangle
            {
                Width = 5,
                Height = 4,
                Margin = new Thickness(2, 0, 2, 0),
                RadiusX = 3,
                RadiusY = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = new LinearGradientBrush(Color.FromArgb(230, accent.R, accent.G, accent.B), Color.FromArgb(110, 167, 139, 250), 90)
            };
            _bars[index] = bar;
            Bars.Items.Add(bar);
        }
    }

    private void DrawFrame()
    {
        var peak = Math.Pow(_meter.ReadPeak(), 0.62);
        _smoothedPeak = Math.Max(peak, _smoothedPeak * 0.88);
        Array.Copy(_history, 0, _history, 1, _history.Length - 1);
        _history[0] = _smoothedPeak;

        for (var index = 0; index < BarCount; index++)
        {
            var sample = _history[Math.Min(_history.Length - 1, index / 2)];
            var wave = 0.68 + (Math.Sin((index * 0.78) + DateTime.UtcNow.Millisecond * 0.012) + 1) * 0.16;
            _bars[index].Height = 4 + Math.Clamp(sample * wave * 92, 0, 92);
            _bars[index].Opacity = 0.42 + Math.Clamp(sample * 1.4, 0, 0.58);
        }
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
