using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GlassBar.Controls;
using GlassBar.Interop;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class MainWindow : Window
{
    private const int EmergencyHotkeyId = 0xB411;
    private readonly WindowService _windowService = new();
    private readonly SettingsService _settingsService = new();
    private readonly ObservableCollection<AppItem> _apps = [];
    private readonly DispatcherTimer _refreshTimer;
    private StartMenuWindow? _startMenu;
    private StickerConfig? _selectedSticker;
    private Border? _draggedSticker;
    private Point _dragStart;
    private Point _dragOrigin;
    private BarSettings _settings;
    private bool _settingsOpen;
    private bool _initializing = true;

    public MainWindow(bool safeMode = false)
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        if (safeMode) _settings.HideNativeTaskbar = false;
        RunningApps.ItemsSource = _apps;
        ApplySettings();

        Loaded += OnLoaded;
        BarSurface.SizeChanged += (_, _) => PositionStickers();
        Closed += OnClosed;
        SourceInitialized += OnSourceInitialized;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => RefreshBar();
        _refreshTimer.Start();
        RefreshBar();
        _initializing = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        RenderStickers();
        if (_settings.HideNativeTaskbar) NativeTaskbar.Hide();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        HwndSource.FromHwnd(helper.Handle)?.AddHook(WndProc);
        NativeMethods.RegisterHotKey(helper.Handle, EmergencyHotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, (uint)'T');
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _startMenu?.Close();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero) NativeMethods.UnregisterHotKey(handle, EmergencyHotkeyId);
        NativeTaskbar.Show();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == EmergencyHotkeyId)
        {
            handled = true;
            NativeTaskbar.Show();
            Application.Current.Shutdown();
        }
        return nint.Zero;
    }

    private void PositionWindow()
    {
        var availableWidth = Math.Max(720, SystemParameters.PrimaryScreenWidth - 24);
        Width = Math.Clamp(_settings.BarWidth, 720, availableWidth);
        BarRow.Height = new GridLength(_settings.BarHeight);
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = SystemParameters.PrimaryScreenHeight - Height - 8;
    }

    private void RefreshBar()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("h:mm");
        DateText.Text = now.ToString("MMM d").ToUpperInvariant();

        var visibleAppLimit = Math.Clamp((int)((Width - 460) / 46), 3, 10);
        var latest = _windowService.GetOpenWindows().Take(visibleAppLimit);
        _apps.Clear();
        foreach (var app in latest) _apps.Add(app);
    }

    private void ApplySettings()
    {
        if (ColorConverter.ConvertFromString(_settings.Accent) is Color accent)
        {
            Resources["AccentBrush"] = new SolidColorBrush(accent);
            EffectsLayer.Accent = accent;
        }
        EffectsLayer.Mode = _settings.Effect;
        EffectsLayer.Intensity = _settings.EffectIntensity;
        if (Resources["GlassBackground"] is SolidColorBrush glass) glass.Opacity = _settings.Opacity;
        OpacitySlider.Value = _settings.Opacity;
        IntensitySlider.Value = _settings.EffectIntensity;
        WidthSlider.Maximum = Math.Max(720, SystemParameters.PrimaryScreenWidth - 24);
        WidthSlider.Value = Math.Min(_settings.BarWidth, WidthSlider.Maximum);
        HeightSlider.Value = _settings.BarHeight;
        CornerSlider.Value = _settings.CornerRadius;
        BarSurface.CornerRadius = new CornerRadius(_settings.CornerRadius);
        BarRow.Height = new GridLength(_settings.BarHeight);
        HideNativeCheck.IsChecked = _settings.HideNativeTaskbar;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        _selectedSticker ??= _settings.Stickers.FirstOrDefault();
        RefreshStickerPicker();
        if (_selectedSticker is not null) SetStickerSliders(_selectedSticker);
        StickerSizeSlider.IsEnabled = _settings.Stickers.Count > 0;
        StickerOpacitySlider.IsEnabled = _settings.Stickers.Count > 0;
    }

    private void SaveSettings() => _settingsService.Save(_settings);

    private void Start_Click(object sender, RoutedEventArgs e) => OpenStartMenu();
    private void Search_Click(object sender, RoutedEventArgs e) => OpenStartMenu();
    private void TaskView_Click(object sender, RoutedEventArgs e) => SystemActions.TaskView();
    private void Explorer_Click(object sender, RoutedEventArgs e) => SystemActions.OpenExplorer();
    private void Terminal_Click(object sender, RoutedEventArgs e) => SystemActions.OpenTerminal();
    private void QuickSettings_Click(object sender, RoutedEventArgs e) => SystemActions.QuickSettings();
    private void Clock_Click(object sender, RoutedEventArgs e) => SystemActions.Notifications();

    private void RunningApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AppItem app }) _windowService.Activate(app);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SetSettingsOpen(!_settingsOpen);
    private void CloseSettings_Click(object sender, RoutedEventArgs e) => SetSettingsOpen(false);

    private void WindowRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_settingsOpen || e.OriginalSource is not DependencyObject source) return;
        if (IsWithin(source, SettingsPanel) || IsWithin(source, BarSurface)) return;
        SetSettingsOpen(false);
    }

    private static bool IsWithin(DependencyObject source, DependencyObject container)
    {
        for (var current = source; current is not null; current = GetParent(current))
            if (ReferenceEquals(current, container)) return true;
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        try { return VisualTreeHelper.GetParent(child); }
        catch (InvalidOperationException) { return LogicalTreeHelper.GetParent(child); }
    }

    private void SetSettingsOpen(bool open)
    {
        if (open) _startMenu?.Hide();
        _settingsOpen = open;
        SettingsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        StickerCanvas.IsHitTestVisible = open;
        Height = open ? Math.Min(690, SystemParameters.PrimaryScreenHeight - 18) : _settings.BarHeight + 10;
        PositionWindow();
        RenderStickers();
    }

    private void OpenStartMenu()
    {
        SetSettingsOpen(false);
        _startMenu ??= new StartMenuWindow();
        if (_startMenu.IsVisible) _startMenu.Hide(); else _startMenu.OpenNear(this);
    }

    private void Effect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string effect }) return;
        _settings.Effect = effect;
        EffectsLayer.Mode = effect;
        SaveSettings();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BarSurface is null) return;
        if (Resources["GlassBackground"] is SolidColorBrush glass) glass.Opacity = e.NewValue;
        if (_initializing) return;
        _settings.Opacity = e.NewValue;
        SaveSettings();
    }

    private void IntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EffectsLayer is null) return;
        EffectsLayer.Intensity = e.NewValue;
        if (_initializing) return;
        _settings.EffectIntensity = e.NewValue;
        SaveSettings();
    }

    private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        _settings.BarWidth = e.NewValue;
        PositionWindow();
        SaveSettings();
    }

    private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        _settings.BarHeight = e.NewValue;
        BarRow.Height = new GridLength(e.NewValue);
        if (!_settingsOpen) Height = e.NewValue + 10;
        PositionWindow();
        SaveSettings();
    }

    private void CornerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BarSurface is null) return;
        BarSurface.CornerRadius = new CornerRadius(e.NewValue);
        if (_initializing) return;
        _settings.CornerRadius = e.NewValue;
        SaveSettings();
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } || ColorConverter.ConvertFromString(value) is not Color accent) return;
        _settings.Accent = value;
        Resources["AccentBrush"] = new SolidColorBrush(accent);
        EffectsLayer.Accent = accent;
        SaveSettings();
    }

    private void AddSticker_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an animated GIF sticker",
            Filter = "GIF images (*.gif)|*.gif",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var sticker = new StickerConfig
            {
                FilePath = StickerService.Import(dialog.FileName),
                DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName)
            };
            _settings.Stickers.Add(sticker);
            _selectedSticker = sticker;
            RefreshStickerPicker();
            StickerSizeSlider.IsEnabled = StickerOpacitySlider.IsEnabled = true;
            SetStickerSliders(sticker);
            RenderStickers();
            SaveSettings();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"GlassBar could not add that GIF.\n\n{exception.Message}", "Sticker import failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearStickers_Click(object sender, RoutedEventArgs e)
    {
        _settings.Stickers.Clear();
        _selectedSticker = null;
        RefreshStickerPicker();
        StickerSizeSlider.IsEnabled = StickerOpacitySlider.IsEnabled = false;
        RenderStickers();
        SaveSettings();
    }

    private void StickerSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _selectedSticker is null) return;
        _selectedSticker.Size = e.NewValue;
        RenderStickers();
        SaveSettings();
    }

    private void StickerOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _selectedSticker is null) return;
        _selectedSticker.Opacity = e.NewValue;
        RenderStickers();
        SaveSettings();
    }

    private void StickerPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || StickerPicker.SelectedItem is not StickerConfig sticker) return;
        _selectedSticker = sticker;
        SetStickerSliders(sticker);
        foreach (Border frame in StickerCanvas.Children)
            frame.BorderBrush = ReferenceEquals(frame.Tag, sticker)
                ? (Brush)Resources["AccentBrush"] : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    }

    private void RefreshStickerPicker()
    {
        if (StickerPicker is null) return;
        var previous = _initializing;
        _initializing = true;
        StickerPicker.ItemsSource = null;
        StickerPicker.ItemsSource = _settings.Stickers;
        StickerPicker.SelectedItem = _selectedSticker;
        StickerPicker.Visibility = _settings.Stickers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _initializing = previous;
    }

    private void RenderStickers()
    {
        if (StickerCanvas is null) return;
        StickerCanvas.Children.Clear();
        foreach (var sticker in _settings.Stickers.Where(item => File.Exists(item.FilePath)))
        {
            var image = new GifSticker(sticker.FilePath);
            System.Windows.Automation.AutomationProperties.SetAutomationId(image, $"Sticker-{sticker.Id}");
            System.Windows.Automation.AutomationProperties.SetName(image, sticker.DisplayName);
            var frame = new Border
            {
                Width = sticker.Size,
                Height = sticker.Size,
                Opacity = sticker.Opacity,
                Child = image,
                Tag = sticker,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                BorderThickness = _settingsOpen ? new Thickness(1) : new Thickness(0),
                BorderBrush = ReferenceEquals(sticker, _selectedSticker)
                    ? (Brush)Resources["AccentBrush"] : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                Cursor = _settingsOpen ? Cursors.SizeAll : Cursors.Arrow
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(frame, $"Sticker-{sticker.Id}");
            System.Windows.Automation.AutomationProperties.SetName(frame, sticker.DisplayName);
            frame.MouseLeftButtonDown += Sticker_MouseLeftButtonDown;
            frame.MouseMove += Sticker_MouseMove;
            frame.MouseLeftButtonUp += Sticker_MouseLeftButtonUp;
            StickerCanvas.Children.Add(frame);
        }
        PositionStickers();
    }

    private void PositionStickers()
    {
        if (StickerCanvas is null) return;
        foreach (Border frame in StickerCanvas.Children)
        {
            if (frame.Tag is not StickerConfig sticker) continue;
            Canvas.SetLeft(frame, Math.Clamp(sticker.X, 0, 1) * Math.Max(0, StickerCanvas.ActualWidth - sticker.Size));
            Canvas.SetTop(frame, Math.Clamp(sticker.Y, 0, 1) * Math.Max(0, StickerCanvas.ActualHeight - sticker.Size));
        }
    }

    private void Sticker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_settingsOpen || sender is not Border { Tag: StickerConfig sticker } frame) return;
        _selectedSticker = sticker;
        StickerPicker.SelectedItem = sticker;
        _draggedSticker = frame;
        _dragStart = e.GetPosition(StickerCanvas);
        _dragOrigin = new Point(Canvas.GetLeft(frame), Canvas.GetTop(frame));
        frame.CaptureMouse();
        SetStickerSliders(sticker);
        foreach (Border item in StickerCanvas.Children)
            item.BorderBrush = ReferenceEquals(item, frame)
                ? (Brush)Resources["AccentBrush"] : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        e.Handled = true;
    }

    private void Sticker_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedSticker is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(StickerCanvas);
        var left = Math.Clamp(_dragOrigin.X + current.X - _dragStart.X, 0, Math.Max(0, StickerCanvas.ActualWidth - _draggedSticker.Width));
        var top = Math.Clamp(_dragOrigin.Y + current.Y - _dragStart.Y, 0, Math.Max(0, StickerCanvas.ActualHeight - _draggedSticker.Height));
        Canvas.SetLeft(_draggedSticker, left);
        Canvas.SetTop(_draggedSticker, top);
    }

    private void Sticker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedSticker?.Tag is not StickerConfig sticker) return;
        sticker.X = Canvas.GetLeft(_draggedSticker) / Math.Max(1, StickerCanvas.ActualWidth - sticker.Size);
        sticker.Y = Canvas.GetTop(_draggedSticker) / Math.Max(1, StickerCanvas.ActualHeight - sticker.Size);
        _draggedSticker.ReleaseMouseCapture();
        _draggedSticker = null;
        SaveSettings();
    }

    private void SetStickerSliders(StickerConfig sticker)
    {
        var previous = _initializing;
        _initializing = true;
        StickerSizeSlider.Value = sticker.Size;
        StickerOpacitySlider.Value = sticker.Opacity;
        _initializing = previous;
    }

    private void HideNativeCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.HideNativeTaskbar = HideNativeCheck.IsChecked == true;
        if (_settings.HideNativeTaskbar) NativeTaskbar.Hide(); else NativeTaskbar.Show();
        SaveSettings();
    }

    private void StartWithWindowsCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settingsService.SetStartWithWindows(_settings.StartWithWindows);
        SaveSettings();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
