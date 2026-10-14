using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GlassBar.Controls;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class TopOverlayWindow : Window
{
    private readonly BarSettings _settings;
    private readonly SettingsService _settingsService = new();
    private readonly SystemMetricsService _metrics = new();
    private readonly DailyNewsService _news = new();
    private readonly DispatcherTimer _timer;
    private DateTime? _focusEndsAt;
    private DateTime _nextNewsRefresh = DateTime.MinValue;
    private string _newsLink = "https://www.bbc.com/news";
    private bool _applyingSize;
    // Widget tiles in declaration order, and the order currently applied. Both
    // are touched only by ApplyConfiguration (an apply/reload event), never by
    // the per-second RefreshWidgets tick.
    private readonly List<(string Canonical, FrameworkElement Element)> _widgetTiles = [];
    private string _appliedWidgetOrder = BuiltInWidgetOrderValue;
    private const string BuiltInWidgetOrderValue = "clock,performance,connection,power,focus,news";
    // Validated once per apply, so the per-second RefreshWidgets never formats
    // with a bad pattern and never needs a try/catch on its hot path.
    private string _clockFormat = "h:mm";
    // Font sizes currently written into the resources; guarded so an unchanged
    // apply does not invalidate layout. Initialised to the XAML defaults.
    private double _valueSize = 12;
    private double _labelSize = 9;

    // Frozen and cached: RefreshWidgets reassigns one of these every tick, and a frozen
    // brush is pixel-identical to the fresh SolidColorBrush it replaces.
    private static readonly SolidColorBrush OnlineDot = Frozen(Color.FromRgb(52, 211, 153));
    private static readonly SolidColorBrush OfflineDot = Frozen(Color.FromRgb(251, 113, 133));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public TopOverlayWindow(BarSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _widgetTiles.Add(("clock", ClockWidget));
        _widgetTiles.Add(("performance", PerformanceWidget));
        _widgetTiles.Add(("connection", ConnectionWidget));
        _widgetTiles.Add(("power", PowerWidget));
        _widgetTiles.Add(("focus", FocusWidget));
        _widgetTiles.Add(("news", NewsWidget));
        PerformanceText.FontSize = 11;
        FocusButton.Width = 108;
        FocusButton.Height = 29;
        ApplyConfiguration(settings, fitToWidgets: settings.TopBarWidth <= 0);
        _metrics.CpuPercent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) =>
        {
            RefreshWidgets();
            if (DateTime.UtcNow >= _nextNewsRefresh) await RefreshNewsAsync();
        };
        Closed += OnClosed;
        Loaded += async (_, _) => { PositionWindow(); RenderTopStickers(); await RefreshNewsAsync(); };
        SizeChanged += (_, _) => RenderTopStickers();
        SizeChanged += OnUserResize;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        // Hide() does not fire Unloaded, so without this the 1s timer keeps polling CPU,
        // memory, network adapters and power state while the overlay is invisible
        // (fullscreen mode). Refreshing on reveal only makes the first visible frame fresher.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { RefreshWidgets(); _timer.Start(); }
            else _timer.Stop();
        };
        RefreshWidgets();
        _timer.Start();
    }

    private bool IsVertical => _settings.TopBarOrientation.Equals("Vertical", StringComparison.OrdinalIgnoreCase);

    /// <summary>Clock tile width; the default (92) is the shipped horizontal width.</summary>
    private double ClockTileWidth => Math.Clamp(_settings.TopClockWidth, 40, 400);

    /// <summary>
    /// Reorders the widget tiles to match the active theme. A no-op unless the
    /// order actually changed, so Default (null order) leaves the built-in
    /// layout untouched and repeated applies do not touch the visual tree.
    /// </summary>
    private void ApplyWidgetOrder()
    {
        var order = string.IsNullOrWhiteSpace(_settings.TopWidgetOrder) ? BuiltInWidgetOrderValue : _settings.TopWidgetOrder;
        if (order.Equals(_appliedWidgetOrder, StringComparison.OrdinalIgnoreCase)) return;
        _appliedWidgetOrder = order;

        foreach (var (_, element) in _widgetTiles) TopWidgetsPanel.Children.Remove(element);
        var insertAt = TopWidgetsPanel.Children.IndexOf(TopDragHandle) + 1;
        foreach (var canonical in order.Split(','))
        {
            var tile = FindTile(canonical.Trim());
            if (tile is null) continue;
            TopWidgetsPanel.Children.Insert(insertAt++, tile);
        }
        // Any tile the order did not name keeps a slot at the end.
        foreach (var (_, element) in _widgetTiles)
            if (!TopWidgetsPanel.Children.Contains(element)) TopWidgetsPanel.Children.Add(element);
    }

    private FrameworkElement? FindTile(string canonical)
    {
        foreach (var (name, element) in _widgetTiles)
            if (name.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                return element;
        return null;
    }

    private static string ValidClockFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Length > 32) return "h:mm";
        try { _ = DateTime.Now.ToString(format); return format; }
        catch (FormatException) { return "h:mm"; }
    }

    /// <summary>
    /// Applies the theme's widget font sizes. Assigning an unchanged value to a
    /// resource would still invalidate layout, so each write is guarded — the
    /// same "do nothing when nothing changed" rule the refresh paths follow.
    /// </summary>
    private void ApplyTypography(BarSettings settings)
    {
        if (_valueSize != settings.TopValueFontSize)
        {
            _valueSize = settings.TopValueFontSize;
            Resources["WidgetValueSize"] = settings.TopValueFontSize;
        }
        if (_labelSize != settings.TopLabelFontSize)
        {
            _labelSize = settings.TopLabelFontSize;
            Resources["WidgetLabelSize"] = settings.TopLabelFontSize;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SystemParameters.PrimaryScreenWidth) or nameof(SystemParameters.WorkArea))
            PositionWindow();
    }

    public void ApplyAppearance(BarSettings settings)
    {
        if (ColorConverter.ConvertFromString(settings.Accent) is Color accent)
            Resources["TopAccent"] = new SolidColorBrush(accent);
        if (settings.BackgroundVisible &&
            ColorConverter.ConvertFromString(settings.BackgroundStart) is Color start &&
            ColorConverter.ConvertFromString(settings.BackgroundEnd) is Color end)
            TopSurface.Background = new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 1)) { Opacity = settings.Opacity };
        else
            TopSurface.Background = Brushes.Transparent;
        if (settings.BackgroundVisible && ColorConverter.ConvertFromString(settings.Border) is Color border)
            TopSurface.BorderBrush = new SolidColorBrush(border) { Opacity = 0.32 };
        else
            TopSurface.BorderBrush = Brushes.Transparent;
        TopHighlight.Opacity = settings.BackgroundVisible ? 0.26 : 0;
        TopShadow.Opacity = settings.BackgroundVisible ? 0.42 : 0;
        TopShadow.BlurRadius = settings.BackgroundVisible ? settings.Shadow : 0;
        TopEffectsLayer.Mode = settings.Effect;
        TopEffectsLayer.Intensity = settings.EffectIntensity;
        TopEffectsLayer.CustomEffect = settings.CustomEffect;
        if (ColorConverter.ConvertFromString(settings.Accent) is Color effectAccent)
            TopEffectsLayer.Accent = effectAccent;
        if (IsLoaded) RenderTopStickers(settings.TopStickers);
    }

    public void ApplyConfiguration(BarSettings settings, bool fitToWidgets = true)
    {
        ApplyAppearance(settings);
        // Typography and tile order come from the active theme; both are cheap
        // resource/children writes on an apply, and neither allocates per frame.
        ApplyTypography(settings);
        _clockFormat = ValidClockFormat(settings.TopClockFormat);
        ApplyWidgetOrder();        ClockWidget.Visibility = settings.TopShowClock ? Visibility.Visible : Visibility.Collapsed;
        PerformanceWidget.Visibility = settings.TopShowPerformance ? Visibility.Visible : Visibility.Collapsed;
        ConnectionWidget.Visibility = settings.TopShowConnection ? Visibility.Visible : Visibility.Collapsed;
        PowerWidget.Visibility = settings.TopShowPower ? Visibility.Visible : Visibility.Collapsed;
        FocusWidget.Visibility = settings.TopShowFocus ? Visibility.Visible : Visibility.Collapsed;
        NewsWidget.Visibility = settings.TopShowNews ? Visibility.Visible : Visibility.Collapsed;
        SetFocusLabel($"Focus {Math.Clamp(settings.TopFocusMinutes, 1, 180)}m");
        ApplyOrientationLayout();
        ApplySize(fitToWidgets);
        PositionWindow();
        RenderTopStickers(settings.TopStickers);
    }

    private void ApplyOrientationLayout()
    {
        TopWidgetsPanel.Orientation = IsVertical ? Orientation.Vertical : Orientation.Horizontal;
        TopDragHandle.Width = IsVertical ? 150 : 20;
        TopDragHandle.Height = IsVertical ? 18 : double.NaN;
        ClockWidget.Width = IsVertical ? 160 : ClockTileWidth;

        SetWidgetLayout(PerformanceWidget, PerformanceDivider, PerformanceContent, 130);
        SetWidgetLayout(ConnectionWidget, ConnectionDivider, ConnectionContent, 86);
        SetWidgetLayout(PowerWidget, PowerDivider, PowerContent, 105);
        SetWidgetLayout(FocusWidget, FocusDivider, null, 112);
        SetWidgetLayout(NewsWidget, NewsDivider, null, 190);
        FocusButton.Width = IsVertical ? 160 : 108;
        NewsButton.Width = IsVertical ? 160 : 190;
    }

    private void SetWidgetLayout(StackPanel wrapper, Border divider, FrameworkElement? content, double horizontalWidth)
    {
        wrapper.Orientation = IsVertical ? Orientation.Vertical : Orientation.Horizontal;
        divider.Width = IsVertical ? 150 : 1;
        divider.Height = IsVertical ? 1 : 22;
        divider.Margin = IsVertical ? new Thickness(0, 7, 0, 7) : new Thickness(9, 0, 9, 0);
        if (content is not null) content.Width = IsVertical ? 160 : horizontalWidth;
    }

    private void ApplySize(bool fitToWidgets)
    {
        _applyingSize = true;
        var maxWidth = Math.Max(180, SystemParameters.PrimaryScreenWidth - 24);
        var maxHeight = Math.Max(160, SystemParameters.PrimaryScreenHeight - 24);
        if (fitToWidgets || _settings.TopBarWidth <= 0)
        {
            Width = IsVertical ? 196 : Math.Clamp(CalculateWidgetWidth(), 280, Math.Min(1100, maxWidth));
            Height = IsVertical ? Math.Clamp(CalculateWidgetHeight(), 180, Math.Min(900, maxHeight)) : 58;
            _settings.TopBarWidth = Width;
            _settings.TopBarHeight = Height;
        }
        else
        {
            Width = Math.Clamp(_settings.TopBarWidth, IsVertical ? 150 : 280, maxWidth);
            Height = Math.Clamp(_settings.TopBarHeight, IsVertical ? 160 : 46, maxHeight);
        }
        _applyingSize = false;
    }

    private double CalculateWidgetWidth()
    {
        var width = 48d;
        if (ClockWidget.Visibility == Visibility.Visible) width += ClockTileWidth;
        if (PerformanceWidget.Visibility == Visibility.Visible) width += 150;
        if (ConnectionWidget.Visibility == Visibility.Visible) width += 106;
        if (PowerWidget.Visibility == Visibility.Visible) width += 125;
        if (FocusWidget.Visibility == Visibility.Visible) width += 131;
        if (NewsWidget.Visibility == Visibility.Visible) width += 210;
        return width;
    }

    private double CalculateWidgetHeight()
    {
        var height = 52d;
        // Each widget is 6px taller than it was: the pill chrome adds 1px of border
        // and 3px of padding top and bottom. Vertical mode stacks them, so the
        // estimate has to grow with them or the window comes up short and clips.
        if (ClockWidget.Visibility == Visibility.Visible) height += 44;
        if (PerformanceWidget.Visibility == Visibility.Visible) height += 55;
        if (ConnectionWidget.Visibility == Visibility.Visible) height += 55;
        if (PowerWidget.Visibility == Visibility.Visible) height += 55;
        if (FocusWidget.Visibility == Visibility.Visible) height += 49;
        if (NewsWidget.Visibility == Visibility.Visible) height += 58;
        return height;
    }

    private void PositionWindow()
    {
        _applyingSize = true;
        var defaultLeft = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        var left = _settings.TopBarX >= 0 ? _settings.TopBarX : defaultLeft;
        var top = _settings.TopBarY >= 0 ? _settings.TopBarY : 8;
        Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(top, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        _applyingSize = false;
    }

    private void OnUserResize(object? sender, SizeChangedEventArgs e)
    {
        if (_applyingSize || !IsLoaded) return;
        _settings.TopBarWidth = Width;
        _settings.TopBarHeight = Height;
        SavePosition();
    }

    private void TopSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || e.OriginalSource is not DependencyObject source || FindParent<Button>(source) is not null)
            return;
        try
        {
            DragMove();
            SavePosition();
            e.Handled = true;
        }
        catch (InvalidOperationException) { }
    }

    private void SavePosition()
    {
        _settings.TopBarX = Left;
        _settings.TopBarY = Top;
        _settingsService.Save(_settings);
    }

    private static T? FindParent<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    private void RefreshWidgets()
    {
        var now = DateTime.Now;
        TopClockText.Text = now.ToString(_clockFormat);
        TopDateText.Text = now.ToString("ddd, MMM d").ToUpperInvariant();
        PerformanceText.Text = $"CPU {_metrics.CpuPercent()}% · RAM {_metrics.MemoryPercent()}%";
        ConnectionText.Text = _metrics.ConnectionLabel();
        ConnectionDot.Fill = ConnectionText.Text == "Offline" ? OfflineDot : OnlineDot;
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

    private async Task RefreshNewsAsync()
    {
        _nextNewsRefresh = DateTime.UtcNow.AddMinutes(30);
        if (!_settings.TopShowNews) return;
        try
        {
            var headline = await _news.GetLatestAsync();
            _newsLink = headline.Link;
            SetNewsLabel(headline.Title);
        }
        catch
        {
            SetNewsLabel("News unavailable — click to retry");
            _nextNewsRefresh = DateTime.UtcNow.AddMinutes(5);
        }
    }

    private async void NewsButton_Click(object sender, RoutedEventArgs e)
    {
        if (NewsButton.Template.FindName("NewsText", NewsButton) is TextBlock { Text: var text } && text.StartsWith("News unavailable"))
        {
            await RefreshNewsAsync();
            return;
        }
        try { Process.Start(new ProcessStartInfo(_newsLink) { UseShellExecute = true }); } catch { }
    }

    private void SetNewsLabel(string value)
    {
        if (NewsButton.Template.FindName("NewsText", NewsButton) is TextBlock label) label.Text = value;
        NewsButton.ToolTip = value;
    }

    private void FocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focusEndsAt is not null)
        {
            _focusEndsAt = null;
            SetFocusLabel($"Focus {_settings.TopFocusMinutes}m");
            return;
        }
        _focusEndsAt = DateTime.Now.AddMinutes(Math.Clamp(_settings.TopFocusMinutes, 1, 180));
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
