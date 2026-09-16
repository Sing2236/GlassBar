using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly LicenseService _licenseService = new();
    private readonly ObservableCollection<AppItem> _apps = [];
    private readonly ObservableCollection<BackgroundProcessItem> _backgroundProcesses = [];
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _taskbarGuardTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly bool _keepVisibleForUiTests;
    private StartMenuWindow? _startMenu;
    private TopOverlayWindow? _topOverlay;
    private StickerConfig? _selectedSticker;
    private StickerConfig? _selectedTopSticker;
    private Border? _draggedSticker;
    private Point _dragStart;
    private Point _dragOrigin;
    private BarSettings _settings;
    private bool _settingsOpen;
    private bool _backgroundProcessesOpen;
    private bool _hiddenForFullscreen;
    private bool _widthDragActive;
    private bool _initializing = true;

    public MainWindow(bool safeMode = false, bool keepVisibleForUiTests = false)
    {
        InitializeComponent();
        _keepVisibleForUiTests = keepVisibleForUiTests;
        WidthSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(WidthSlider_DragStarted));
        WidthSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(WidthSlider_DragCompleted));
        _settings = _settingsService.Load();
        if (safeMode) _settings.HideNativeTaskbar = false;
        RunningApps.ItemsSource = _apps;
        BackgroundProcessesList.ItemsSource = _backgroundProcesses;
        ApplySettings();

        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
        BarSurface.SizeChanged += (_, _) => PositionStickers();
        Closed += OnClosed;
        SourceInitialized += OnSourceInitialized;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => RefreshBar();
        _refreshTimer.Start();
        // Explorer may recreate or re-show its taskbar when a fullscreen or
        // borderless game changes foreground state. Re-apply our hide state
        // while GlassBar owns the replacement taskbar.
        _taskbarGuardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _taskbarGuardTimer.Tick += (_, _) =>
        {
            if (_settings.HideNativeTaskbar) NativeTaskbar.EnsureHidden();
        };
        _taskbarGuardTimer.Start();
        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _fullscreenTimer.Tick += (_, _) => UpdateFullscreenVisibility();
        _fullscreenTimer.Start();
        RefreshBar();
        _initializing = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        RenderStickers();
        ApplyTopOverlayState();
        if (_settings.HideNativeTaskbar) NativeTaskbar.Hide();
        UpdateFullscreenVisibility();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_settingsOpen) SetSettingsOpen(false);
        if (_backgroundProcessesOpen) SetBackgroundProcessesOpen(false);
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
        _taskbarGuardTimer.Stop();
        _fullscreenTimer.Stop();
        _startMenu?.Close();
        _topOverlay?.Close();
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

    private void UpdateWindowHeight()
    {
        Height = _settingsOpen
            ? Math.Min(690, SystemParameters.PrimaryScreenHeight - 18)
            : _backgroundProcessesOpen
                ? Math.Min(_settings.BarHeight + 334, SystemParameters.PrimaryScreenHeight - 18)
                : _settings.BarHeight + 10;
        PositionWindow();
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

    private void UpdateFullscreenVisibility()
    {
        if (_keepVisibleForUiTests) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero) return;

        var shouldHide = FullscreenWindowDetector.IsForegroundFullscreen(handle);
        if (shouldHide == _hiddenForFullscreen) return;
        _hiddenForFullscreen = shouldHide;

        if (shouldHide)
        {
            _startMenu?.Hide();
            SetSettingsOpen(false);
            Hide();
            return;
        }

        ShowActivated = false;
        Show();
    }

    private void ApplySettings()
    {
        var premiumEffectReset = !_licenseService.IsPro && IsPremiumEffect(_settings.Effect);
        if (premiumEffectReset) _settings.Effect = "Rain";
        if (ColorConverter.ConvertFromString(_settings.Accent) is Color accent)
        {
            Resources["AccentBrush"] = new SolidColorBrush(accent);
            EffectsLayer.Accent = accent;
        }
        EffectsLayer.CustomEffect = _settings.CustomEffect;
        EffectsLayer.Mode = _settings.Effect;
        EffectsLayer.Intensity = _settings.EffectIntensity;
        if (Resources["GlassBackground"] is SolidColorBrush glass) glass.Opacity = _settings.Opacity;
        OpacitySlider.Value = _settings.Opacity;
        IntensitySlider.Value = _settings.EffectIntensity;
        WidthSlider.Maximum = Math.Max(720, SystemParameters.PrimaryScreenWidth - 24);
        WidthSlider.Value = Math.Min(_settings.BarWidth, WidthSlider.Maximum);
        WidthValueText.Text = $"{Math.Round(WidthSlider.Value)} px";
        HeightSlider.Value = _settings.BarHeight;
        CornerSlider.Value = _settings.CornerRadius;
        BarSurface.CornerRadius = new CornerRadius(_settings.CornerRadius);
        BarRow.Height = new GridLength(_settings.BarHeight);
        UseWindowsSearchCheck.IsChecked = _settings.UseWindowsSearch;
        HideNativeCheck.IsChecked = _settings.HideNativeTaskbar;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        TopOverlayCheck.IsChecked = _settings.TopOverlayEnabled;
        TopClockCheck.IsChecked = _settings.TopShowClock;
        TopPerformanceCheck.IsChecked = _settings.TopShowPerformance;
        TopConnectionCheck.IsChecked = _settings.TopShowConnection;
        TopPowerCheck.IsChecked = _settings.TopShowPower;
        TopFocusCheck.IsChecked = _settings.TopShowFocus;
        TopOverlayOptionsPanel.Visibility = _settings.TopOverlayEnabled ? Visibility.Visible : Visibility.Collapsed;
        _selectedTopSticker ??= _settings.TopStickers.FirstOrDefault();
        RefreshTopStickerPicker();
        if (_selectedTopSticker is not null) SetTopStickerSliders(_selectedTopSticker);
        TopStickerSizeSlider.IsEnabled = _settings.TopStickers.Count > 0;
        TopStickerOpacitySlider.IsEnabled = _settings.TopStickers.Count > 0;
        ApplyCustomEffectToControls();
        CustomEffectPanel.Visibility = _settings.Effect == "Custom" && _licenseService.IsPro
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateProUi();
        _selectedSticker ??= _settings.Stickers.FirstOrDefault();
        RefreshStickerPicker();
        if (_selectedSticker is not null) SetStickerSliders(_selectedSticker);
        StickerSizeSlider.IsEnabled = _settings.Stickers.Count > 0;
        StickerOpacitySlider.IsEnabled = _settings.Stickers.Count > 0;
        if (premiumEffectReset) SaveSettings();
    }

    private void SaveSettings() => _settingsService.Save(_settings);

    private void Start_Click(object sender, RoutedEventArgs e) => OpenStartMenu();
    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.UseWindowsSearch)
        {
            _startMenu?.Hide();
            SystemActions.Search();
            return;
        }

        OpenStartMenu();
    }
    private void TaskView_Click(object sender, RoutedEventArgs e) => SystemActions.TaskView();
    private void Explorer_Click(object sender, RoutedEventArgs e) => SystemActions.OpenExplorer();
    private void Terminal_Click(object sender, RoutedEventArgs e) => SystemActions.OpenTerminal();
    private void QuickSettings_Click(object sender, RoutedEventArgs e) => SystemActions.QuickSettings();
    private void Clock_Click(object sender, RoutedEventArgs e) => SystemActions.Notifications();

    private void BackgroundProcesses_Click(object sender, RoutedEventArgs e)
    {
        if (_backgroundProcessesOpen)
        {
            SetBackgroundProcessesOpen(false);
            return;
        }
        SetSettingsOpen(false);
        _startMenu?.Hide();
        SetBackgroundProcessesOpen(true);
    }

    private void RefreshBackgroundProcesses_Click(object sender, RoutedEventArgs e) => RefreshBackgroundProcesses();

    private void SetBackgroundProcessesOpen(bool open)
    {
        _backgroundProcessesOpen = open;
        BackgroundProcessesPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open) RefreshBackgroundProcesses();
        UpdateWindowHeight();
    }

    private void RefreshBackgroundProcesses()
    {
        var items = _windowService.GetBackgroundProcesses();
        _backgroundProcesses.Clear();
        foreach (var item in items) _backgroundProcesses.Add(item);
        BackgroundProcessCountText.Text = items.Count == 1 ? "1 active app" : $"{items.Count} active apps";
    }

    private void RunningApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AppItem app }) _windowService.Activate(app);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SetSettingsOpen(!_settingsOpen);
    private void CloseSettings_Click(object sender, RoutedEventArgs e) => SetSettingsOpen(false);

    private void WindowRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (_settingsOpen && !IsWithin(source, SettingsPanel) && !IsWithin(source, BarSurface))
            SetSettingsOpen(false);
        if (_backgroundProcessesOpen && !IsWithin(source, BackgroundProcessesPanel) && !IsWithin(source, BarSurface))
            SetBackgroundProcessesOpen(false);
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
        if (open && _backgroundProcessesOpen)
        {
            _backgroundProcessesOpen = false;
            BackgroundProcessesPanel.Visibility = Visibility.Collapsed;
        }
        _settingsOpen = open;
        SettingsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        StickerCanvas.IsHitTestVisible = open;
        UpdateWindowHeight();
        RenderStickers();
    }

    private void OpenStartMenu()
    {
        SetSettingsOpen(false);
        SetBackgroundProcessesOpen(false);
        _startMenu ??= new StartMenuWindow();
        _startMenu.ApplyAppearance(_settings);
        if (_startMenu.IsVisible) _startMenu.Hide(); else _startMenu.OpenNear(this);
    }

    private void Effect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string effect }) return;
        if (IsPremiumEffect(effect) && !EnsurePro(effect)) return;
        _settings.Effect = effect;
        EffectsLayer.Mode = effect;
        CustomEffectPanel.Visibility = effect == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        _topOverlay?.ApplyAppearance(_settings);
        SaveSettings();
    }

    private void ApplyCustomEffectToControls()
    {
        var previous = _initializing;
        _initializing = true;
        CustomEffectNameBox.Text = _settings.CustomEffect.Name;
        CustomDensitySlider.Value = _settings.CustomEffect.Density;
        CustomSpeedSlider.Value = _settings.CustomEffect.Speed;
        CustomSizeSlider.Value = _settings.CustomEffect.Size;
        CustomGlowSlider.Value = _settings.CustomEffect.Glow;
        CustomTrailSlider.Value = _settings.CustomEffect.Trail;
        CustomEffectSummary.Text = $"{_settings.CustomEffect.Shape} · {_settings.CustomEffect.Motion}";
        _initializing = previous;
    }

    private void ActivateCustomEffect(bool rebuildParticles = false)
    {
        if (!EnsurePro("Custom Effect Lab")) return;
        _settings.Effect = "Custom";
        EffectsLayer.CustomEffect = _settings.CustomEffect;
        EffectsLayer.Mode = "Custom";
        EffectsLayer.RefreshCustomEffect(rebuildParticles);
        _topOverlay?.ApplyAppearance(_settings);
        CustomEffectPanel.Visibility = Visibility.Visible;
        CustomEffectSummary.Text = $"{_settings.CustomEffect.Shape} · {_settings.CustomEffect.Motion}";
        SaveSettings();
    }

    private void CustomShape_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro("Custom Effect Lab")) return;
        if (sender is not Button { Tag: string shape }) return;
        _settings.CustomEffect.Shape = shape;
        ActivateCustomEffect();
    }

    private void CustomMotion_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro("Custom Effect Lab")) return;
        if (sender is not Button { Tag: string motion }) return;
        _settings.CustomEffect.Motion = motion;
        ActivateCustomEffect(rebuildParticles: true);
    }

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro("Custom Effect Lab")) return;
        if (sender is not Button { Tag: string color }) return;
        _settings.CustomEffect.SecondaryColor = color;
        ActivateCustomEffect();
    }

    private void CustomEffectNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing || _settings is null) return;
        if (!EnsurePro("Custom Effect Lab")) return;
        _settings.CustomEffect.Name = string.IsNullOrWhiteSpace(CustomEffectNameBox.Text) ? "My effect" : CustomEffectNameBox.Text.Trim();
        ActivateCustomEffect();
    }

    private void CustomDensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _settings is null) return;
        if (!EnsurePro("Custom Effect Lab")) return;
        _settings.CustomEffect.Density = e.NewValue;
        ActivateCustomEffect(rebuildParticles: true);
    }

    private void CustomSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _settings is null) return;
        if (!EnsurePro("Custom Effect Lab")) return;
        _settings.CustomEffect.Speed = e.NewValue;
        ActivateCustomEffect();
    }

    private void CustomSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _settings is null) return;
        if (!EnsurePro("Custom Effect Lab")) return;
        _settings.CustomEffect.Size = e.NewValue;
        ActivateCustomEffect();
    }

    private void CustomGlowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _settings is null) return;
        if (!EnsurePro("Custom Effect Lab")) return;
        _settings.CustomEffect.Glow = e.NewValue;
        ActivateCustomEffect();
    }

    private void CustomTrailSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _settings is null) return;
        if (!EnsurePro("Custom Effect Lab")) return;
        _settings.CustomEffect.Trail = e.NewValue;
        ActivateCustomEffect();
    }

    private void RandomizeEffect_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro("Custom Effect Lab")) return;
        var random = Random.Shared;
        var shapes = new[] { "Orb", "Spark", "Drop", "Diamond" };
        var motions = new[] { "Float", "Fall", "Rise", "Drift" };
        var colors = new[] { "#A78BFA", "#34D399", "#FB7185", "#FBBF24" };
        _settings.CustomEffect.Shape = shapes[random.Next(shapes.Length)];
        _settings.CustomEffect.Motion = motions[random.Next(motions.Length)];
        _settings.CustomEffect.SecondaryColor = colors[random.Next(colors.Length)];
        _settings.CustomEffect.Density = 0.3 + random.NextDouble() * 0.7;
        _settings.CustomEffect.Speed = 0.15 + random.NextDouble() * 0.85;
        _settings.CustomEffect.Size = 0.15 + random.NextDouble() * 0.85;
        _settings.CustomEffect.Glow = random.NextDouble();
        _settings.CustomEffect.Trail = random.NextDouble();
        ApplyCustomEffectToControls();
        ActivateCustomEffect(rebuildParticles: true);
    }

    private void ExportEffect_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro("Custom Effect Lab")) return;
        var safeName = string.Concat(_settings.CustomEffect.Name.Where(character => !Path.GetInvalidFileNameChars().Contains(character)));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export GlassBar effect",
            FileName = string.IsNullOrWhiteSpace(safeName) ? "GlassBar-effect" : safeName,
            DefaultExt = ".glassfx.json",
            Filter = "GlassBar effects (*.glassfx.json)|*.glassfx.json|JSON files (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_settings.CustomEffect, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ImportEffect_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro("Custom Effect Lab")) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import GlassBar effect",
            Filter = "GlassBar effects (*.glassfx.json;*.json)|*.glassfx.json;*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = JsonSerializer.Deserialize<CustomEffectConfig>(File.ReadAllText(dialog.FileName));
            if (imported is null) throw new InvalidDataException("That file does not contain an effect.");
            imported.Density = Math.Clamp(imported.Density, 0.2, 1);
            imported.Speed = Math.Clamp(imported.Speed, 0.1, 1);
            imported.Size = Math.Clamp(imported.Size, 0.1, 1);
            imported.Glow = Math.Clamp(imported.Glow, 0, 1);
            imported.Trail = Math.Clamp(imported.Trail, 0, 1);
            _settings.CustomEffect = imported;
            EffectsLayer.CustomEffect = imported;
            ApplyCustomEffectToControls();
            ActivateCustomEffect(rebuildParticles: true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"GlassBar could not import that effect.\n\n{exception.Message}", "Effect import failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BuyPro_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(LicenseService.PurchaseUrl) { UseShellExecute = true });
            LicenseMessageText.Text = "After payment is verified, your lifetime key is emailed to your PayPal email.";
        }
        catch
        {
            LicenseMessageText.Text = "GlassBar could not open PayPal. Visit the GlassBar website to purchase Pro.";
        }
    }

    private void ActivateLicense_Click(object sender, RoutedEventArgs e)
    {
        if (!_licenseService.TryActivate(LicenseKeyBox.Text, out var message))
        {
            LicenseMessageText.Text = message;
            return;
        }

        LicenseKeyBox.Clear();
        UpdateProUi();
        LicenseMessageText.Text = message;
    }

    private bool EnsurePro(string feature)
    {
        if (_licenseService.IsPro) return true;
        LicenseMessageText.Text = $"{feature} requires the $5 GlassBar Pro lifetime unlock.";
        ProPanel.BringIntoView();
        return false;
    }

    private void UpdateProUi()
    {
        var isPro = _licenseService.IsPro;
        ProStatusText.Text = isPro ? "UNLOCKED" : "LOCKED";
        ProStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            isPro ? "#A7F3D0" : "#BEEBFF"));
        ProEntryPanel.Visibility = isPro ? Visibility.Collapsed : Visibility.Visible;
        LicenseMessageText.Text = isPro
            ? $"Lifetime license active for {_licenseService.LicensedEmail}."
            : "Rain and Aurora are free. Pro effects unlock once for $5.";

        EffectSnowButton.Content = isPro ? "Snow" : "Snow  PRO";
        EffectFirefliesButton.Content = isPro ? "Fireflies" : "Fireflies  PRO";
        EffectPulseButton.Content = isPro ? "Pulse" : "Pulse  PRO";
        EffectCustomButton.Content = isPro ? "Custom" : "Custom  PRO";
        foreach (var button in new[] { EffectSnowButton, EffectFirefliesButton, EffectPulseButton, EffectCustomButton })
            button.Opacity = isPro ? 1 : 0.68;
    }

    private static bool IsPremiumEffect(string effect) => effect is "Snow" or "Fireflies" or "Pulse" or "Custom";

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BarSurface is null) return;
        if (Resources["GlassBackground"] is SolidColorBrush glass) glass.Opacity = e.NewValue;
        if (_initializing) return;
        _settings.Opacity = e.NewValue;
        _topOverlay?.ApplyAppearance(_settings);
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
        if (WidthValueText is not null)
            WidthValueText.Text = $"{Math.Round(e.NewValue)} px{(_widthDragActive ? " · release to apply" : "")}";
        if (_initializing) return;
        _settings.BarWidth = e.NewValue;
        if (_widthDragActive) return;
        PositionWindow();
        SaveSettings();
    }

    private void WidthSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _widthDragActive = true;
        WidthValueText.Text = $"{Math.Round(WidthSlider.Value)} px · release to apply";
    }

    private void WidthSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _widthDragActive = false;
        _settings.BarWidth = WidthSlider.Value;
        WidthValueText.Text = $"{Math.Round(WidthSlider.Value)} px";
        PositionWindow();
        SaveSettings();
    }

    private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        _settings.BarHeight = e.NewValue;
        BarRow.Height = new GridLength(e.NewValue);
        UpdateWindowHeight();
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
        _topOverlay?.ApplyAppearance(_settings);
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

    private void UseWindowsSearchCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settings.UseWindowsSearch = UseWindowsSearchCheck.IsChecked == true;
        SaveSettings();
    }

    private void StartWithWindowsCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settingsService.SetStartWithWindows(_settings.StartWithWindows);
        SaveSettings();
    }

    private void TopOverlayCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.TopOverlayEnabled = TopOverlayCheck.IsChecked == true;
        TopOverlayOptionsPanel.Visibility = _settings.TopOverlayEnabled ? Visibility.Visible : Visibility.Collapsed;
        ApplyTopOverlayState();
        SaveSettings();
    }

    private void TopWidgetCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string widget }) return;
        switch (widget)
        {
            case "Clock": _settings.TopShowClock = TopClockCheck.IsChecked == true; break;
            case "Performance": _settings.TopShowPerformance = TopPerformanceCheck.IsChecked == true; break;
            case "Connection": _settings.TopShowConnection = TopConnectionCheck.IsChecked == true; break;
            case "Power": _settings.TopShowPower = TopPowerCheck.IsChecked == true; break;
            case "Focus": _settings.TopShowFocus = TopFocusCheck.IsChecked == true; break;
        }
        _topOverlay?.ApplyConfiguration(_settings);
        SaveSettings();
    }

    private void FocusDuration_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } || !int.TryParse(value, out var minutes)) return;
        _settings.TopFocusMinutes = Math.Clamp(minutes, 1, 180);
        _topOverlay?.ApplyConfiguration(_settings, fitToWidgets: false);
        SaveSettings();
    }

    private void ApplyTopOverlayState()
    {
        if (_settings.TopOverlayEnabled)
        {
            if (_topOverlay is null)
            {
                _topOverlay = new TopOverlayWindow(_settings);
                _topOverlay.Closed += (_, _) => _topOverlay = null;
                _topOverlay.Show();
            }
            else
            {
                _topOverlay.ApplyConfiguration(_settings);
            }
            return;
        }

        _topOverlay?.Close();
        _topOverlay = null;
    }

    private void AddTopSticker_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a top bar GIF", Filter = "GIF images (*.gif)|*.gif", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var sticker = new StickerConfig { FilePath = StickerService.Import(dialog.FileName), DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName), X = 0.88, Y = 0.05, Size = 42 };
            _settings.TopStickers.Add(sticker);
            _selectedTopSticker = sticker;
            RefreshTopStickerPicker();
            SetTopStickerSliders(sticker);
            TopStickerSizeSlider.IsEnabled = TopStickerOpacitySlider.IsEnabled = true;
            _topOverlay?.ApplyConfiguration(_settings);
            SaveSettings();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"GlassBar could not add that top bar GIF.\n\n{exception.Message}", "Top GIF import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearTopStickers_Click(object sender, RoutedEventArgs e)
    {
        _settings.TopStickers.Clear();
        _selectedTopSticker = null;
        RefreshTopStickerPicker();
        TopStickerSizeSlider.IsEnabled = TopStickerOpacitySlider.IsEnabled = false;
        _topOverlay?.ApplyConfiguration(_settings);
        SaveSettings();
    }

    private void TopStickerPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || TopStickerPicker.SelectedItem is not StickerConfig sticker) return;
        _selectedTopSticker = sticker;
        SetTopStickerSliders(sticker);
    }

    private void RefreshTopStickerPicker()
    {
        if (TopStickerPicker is null) return;
        var previous = _initializing;
        _initializing = true;
        TopStickerPicker.ItemsSource = null;
        TopStickerPicker.ItemsSource = _settings.TopStickers;
        TopStickerPicker.SelectedItem = _selectedTopSticker;
        TopStickerPicker.Visibility = _settings.TopStickers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _initializing = previous;
    }

    private void SetTopStickerSliders(StickerConfig sticker)
    {
        var previous = _initializing;
        _initializing = true;
        TopStickerSizeSlider.Value = sticker.Size;
        TopStickerOpacitySlider.Value = sticker.Opacity;
        _initializing = previous;
    }

    private void TopStickerSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _selectedTopSticker is null) return;
        _selectedTopSticker.Size = e.NewValue;
        _topOverlay?.ApplyConfiguration(_settings);
        SaveSettings();
    }

    private void TopStickerOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _selectedTopSticker is null) return;
        _selectedTopSticker.Opacity = e.NewValue;
        _topOverlay?.ApplyConfiguration(_settings);
        SaveSettings();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
