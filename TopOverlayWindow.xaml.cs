using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;
using GlassBar.Models;
using GlassBar.Services;
using GlassBar.Controls;

namespace GlassBar;

public partial class TopOverlayWindow : Window
{
    private readonly SystemMetricsService _metrics = new();
    private readonly DispatcherTimer _timer;
    private DateTime? _focusEndsAt;

    public TopOverlayWindow(BarSettings settings)
    {
        InitializeComponent();
        ApplyConfiguration(settings);
        _metrics.CpuPercent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshWidgets();
        Closed += OnClosed;
        Loaded += (_, _) => { PositionAtTop(); RenderTopStickers(); };
        SizeChanged += (_, _) => RenderTopStickers();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        RefreshWidgets();
        _timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SystemParameters.PrimaryScreenWidth) or nameof(SystemParameters.WorkArea))
            PositionAtTop();
    }

    public void ApplyAppearance(BarSettings settings)
    {
        if (ColorConverter.ConvertFromString(settings.Accent) is Color accent)
            Resources["TopAccent"] = new SolidColorBrush(accent);
        if (Resources["TopGlassBackground"] is SolidColorBrush glass) glass.Opacity = settings.Opacity;
        TopEffectsLayer.Mode = settings.Effect;
        TopEffectsLayer.Intensity = settings.EffectIntensity;
        TopEffectsLayer.CustomEffect = settings.CustomEffect;
        if (ColorConverter.ConvertFromString(settings.Accent) is Color effectAccent)
            TopEffectsLayer.Accent = effectAccent;
        if (IsLoaded) RenderTopStickers(settings.TopStickers);
    }

    public void ApplyConfiguration(BarSettings settings)
    {
        ApplyAppearance(settings);
        ClockWidget.Visibility = settings.TopShowClock ? Visibility.Visible : Visibility.Collapsed;
        PerformanceWidget.Visibility = settings.TopShowPerformance ? Visibility.Visible : Visibility.Collapsed;
        ConnectionWidget.Visibility = settings.TopShowConnection ? Visibility.Visible : Visibility.Collapsed;
        PowerWidget.Visibility = settings.TopShowPower ? Visibility.Visible : Visibility.Collapsed;
        FocusWidget.Visibility = settings.TopShowFocus ? Visibility.Visible : Visibility.Collapsed;
        RenderTopStickers(settings.TopStickers);
    }

    private void PositionAtTop()
    {
        Width = Math.Min(760, Math.Max(520, SystemParameters.PrimaryScreenWidth - 32));
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 8;
    }

    private void RefreshWidgets()
    {
        var now = DateTime.Now;
        TopClockText.Text = now.ToString("h:mm");
        TopDateText.Text = now.ToString("ddd, MMM d").ToUpperInvariant();
        PerformanceText.Text = $"CPU {_metrics.CpuPercent()}%  ·  RAM {_metrics.MemoryPercent()}%";
        ConnectionText.Text = _metrics.ConnectionLabel();
        ConnectionDot.Fill = ConnectionText.Text == "Offline"
            ? new SolidColorBrush(Color.FromRgb(251, 113, 133))
            : new SolidColorBrush(Color.FromRgb(52, 211, 153));
        PowerText.Text = _metrics.PowerLabel();

        if (_focusEndsAt is null) return;
        var remaining = _focusEndsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            _focusEndsAt = null;
            SetFocusLabel("Focus done");
            return;
        }
        SetFocusLabel($"Focus {remaining.Minutes:00}:{remaining.Seconds:00}");
    }

    private void FocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focusEndsAt is not null)
        {
            _focusEndsAt = null;
            SetFocusLabel("Focus 25");
            return;
        }
        _focusEndsAt = DateTime.Now.AddMinutes(25);
        RefreshWidgets();
    }

    private void SetFocusLabel(string value)
    {
        if (FocusButton.Template.FindName("FocusText", FocusButton) is TextBlock label) label.Text = value;
    }

    private void RenderTopStickers(IReadOnlyList<StickerConfig>? stickers = null)
    {
        if (TopStickerCanvas is null) return;
        TopStickerCanvas.Children.Clear();
        foreach (var sticker in stickers ?? [])
        {
            if (!File.Exists(sticker.FilePath)) continue;
            var image = new GifSticker(sticker.FilePath);
            var frame = new Border
            {
                Width = Math.Clamp(sticker.Size, 20, 70), Height = Math.Clamp(sticker.Size, 20, 70),
                Opacity = Math.Clamp(sticker.Opacity, 0.1, 1), Child = image,
                Background = Brushes.Transparent, CornerRadius = new CornerRadius(8)
            };
            TopStickerCanvas.Children.Add(frame);
            Canvas.SetLeft(frame, Math.Clamp(sticker.X, 0, 1) * Math.Max(0, TopStickerCanvas.ActualWidth - frame.Width));
            Canvas.SetTop(frame, Math.Clamp(sticker.Y, 0, 1) * Math.Max(0, TopStickerCanvas.ActualHeight - frame.Height));
        }
    }
}
