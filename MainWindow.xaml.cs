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
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GlassBar.Controls;
using GlassBar.Interop;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class MainWindow : Window
{
    private const int EmergencyHotkeyId = 0xB411;
    private const int AppSearchHotkeyId = 0xB412;
    private const int WebSearchHotkeyId = 0xB413;
    private const int CombinedSearchHotkeyId = 0xB414;
    private readonly WindowService _windowService = new();
    private readonly SettingsService _settingsService = new();
    private readonly LicenseService _licenseService = new();
    private readonly CommunityDesignService _communityDesignService = new();
    private readonly ObservableCollection<AppItem> _apps = [];
    private readonly ObservableCollection<PinnedAppConfig> _pinnedApps = [];
    private readonly ObservableCollection<BackgroundProcessItem> _backgroundProcesses = [];
    private readonly Dictionary<int, HotkeyGesture> _registeredSearchHotkeys = [];
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _taskbarGuardTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly bool _keepVisibleForUiTests;
    private StartMenuWindow? _startMenu;
    private SearchPaletteWindow? _searchPalette;
    private TopOverlayWindow? _topOverlay;
    private AudioVisualizerWindow? _audioVisualizer;
    private IReadOnlyList<AppItem> _allApps = [];
    private int _appPage;
    private bool _pageAnimating;
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

    internal BarSettings CurrentSettings => _settings;

    public MainWindow(bool safeMode = false, bool keepVisibleForUiTests = false)
    {
        InitializeComponent();
        _keepVisibleForUiTests = keepVisibleForUiTests;
        WidthSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(WidthSlider_DragStarted));
        WidthSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(WidthSlider_DragCompleted));
        _settings = _settingsService.Load();
        if (safeMode) _settings.HideNativeTaskbar = false;
        RunningApps.ItemsSource = _apps;
        PinnedApps.ItemsSource = _pinnedApps;
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
        _fullscreenTimer.Tick += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (_searchPalette is { IsVisible: true } && handle != nint.Zero &&
                FullscreenWindowDetector.IsForegroundFullscreen(handle))
                _searchPalette.ClosePalette();
            UpdateFullscreenVisibility();
        };
        _fullscreenTimer.Start();
        RefreshBar();
        _initializing = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        RenderStickers();
        ApplyTopOverlayState();
        ApplyAudioVisualizerState();
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
        RegisterConfiguredSearchHotkeys(helper.Handle);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _taskbarGuardTimer.Stop();
        _fullscreenTimer.Stop();
        _startMenu?.Close();
        _searchPalette?.Close();
        _topOverlay?.Close();
        _audioVisualizer?.Close();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
        {
            NativeMethods.UnregisterHotKey(handle, EmergencyHotkeyId);
            NativeMethods.UnregisterHotKey(handle, AppSearchHotkeyId);
            NativeMethods.UnregisterHotKey(handle, WebSearchHotkeyId);
            NativeMethods.UnregisterHotKey(handle, CombinedSearchHotkeyId);
        }
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
        else if (msg == NativeMethods.WM_HOTKEY)
        {
            var mode = wParam.ToInt32() switch
            {
                AppSearchHotkeyId => SearchPaletteMode.Apps,
                WebSearchHotkeyId => SearchPaletteMode.Web,
                CombinedSearchHotkeyId => SearchPaletteMode.Combined,
                _ => (SearchPaletteMode?)null
            };
            if (mode is not null)
            {
                handled = true;
                OpenSearchPalette(mode.Value);
            }
        }
        return nint.Zero;
    }

    private void RegisterConfiguredSearchHotkeys(nint handle)
    {
        _registeredSearchHotkeys.Clear();
        var failures = new List<string>();
        foreach (var (id, label, configured, fallback) in new[]
                 {
                     (AppSearchHotkeyId, "Open applications", _settings.SearchAppsHotkey, "Ctrl + Alt + Space"),
                     (WebSearchHotkeyId, "Search the web", _settings.SearchWebHotkey, "Ctrl + Alt + W"),
                     (CombinedSearchHotkeyId, "Search everything", _settings.SearchCombinedHotkey, "Ctrl + Alt + A")
                 })
        {
            if (!HotkeyGesture.TryParse(configured, out var gesture) &&
                !HotkeyGesture.TryParse(fallback, out gesture)) continue;

            if (NativeMethods.RegisterHotKey(handle, id, gesture.RegistrationModifiers, gesture.VirtualKey))
                _registeredSearchHotkeys[id] = gesture;
            else
                failures.Add($"{label}: {gesture.DisplayText}");
        }

        if (failures.Count > 0)
            SetSearchHotkeyStatus($"Windows or another app is already using {string.Join(", ", failures)}. Choose a different shortcut.", isError: true);
    }

    private void SearchHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBox { Tag: string mode }) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            SetSearchHotkeyStatus("Shortcut unchanged.", isError: false);
            Keyboard.ClearFocus();
            return;
        }

        if (!HotkeyGesture.TryCreate(key, Keyboard.Modifiers, out var candidate, out var error))
        {
            SetSearchHotkeyStatus(error, isError: true);
            return;
        }

        if (HotkeyGesture.IsWindowsReserved(candidate))
        {
            SetSearchHotkeyStatus($"{candidate.DisplayText} is reserved by Windows. Choose another shortcut.", isError: true);
            return;
        }

        var id = mode switch
        {
            "Apps" => AppSearchHotkeyId,
            "Web" => WebSearchHotkeyId,
            "Combined" => CombinedSearchHotkeyId,
            _ => 0
        };
        if (id == 0) return;

        if (HotkeyGesture.TryParse("Ctrl + Alt + Shift + T", out var emergency) &&
            candidate.Modifiers == emergency.Modifiers && candidate.VirtualKey == emergency.VirtualKey)
        {
            SetSearchHotkeyStatus("That shortcut is GlassBar's emergency exit. Choose another shortcut.", isError: true);
            return;
        }

        var overlap = GetConfiguredSearchHotkeys()
            .FirstOrDefault(item => item.Id != id && item.Gesture.Modifiers == candidate.Modifiers &&
                                    item.Gesture.VirtualKey == candidate.VirtualKey);
        if (overlap.Id != 0)
        {
            SetSearchHotkeyStatus($"{candidate.DisplayText} already opens {overlap.Label}. Choose another shortcut.", isError: true);
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        _registeredSearchHotkeys.TryGetValue(id, out var previous);
        if (handle != nint.Zero) NativeMethods.UnregisterHotKey(handle, id);
        if (handle == nint.Zero || !NativeMethods.RegisterHotKey(handle, id, candidate.RegistrationModifiers, candidate.VirtualKey))
        {
            if (handle != nint.Zero && previous.VirtualKey != 0)
                NativeMethods.RegisterHotKey(handle, id, previous.RegistrationModifiers, previous.VirtualKey);
            SetSearchHotkeyStatus($"Windows or another app is already using {candidate.DisplayText}. Choose another shortcut.", isError: true);
            return;
        }

        _registeredSearchHotkeys[id] = candidate;
        switch (mode)
        {
            case "Apps":
                _settings.SearchAppsHotkey = candidate.DisplayText;
                SearchAppsHotkeyBox.Text = candidate.DisplayText;
                break;
            case "Web":
                _settings.SearchWebHotkey = candidate.DisplayText;
                SearchWebHotkeyBox.Text = candidate.DisplayText;
                break;
            case "Combined":
                _settings.SearchCombinedHotkey = candidate.DisplayText;
                SearchCombinedHotkeyBox.Text = candidate.DisplayText;
                break;
        }
        SaveSettings();
        SetSearchHotkeyStatus($"{mode} shortcut changed to {candidate.DisplayText}.", isError: false);
        Keyboard.ClearFocus();
    }

    private IEnumerable<(int Id, string Label, HotkeyGesture Gesture)> GetConfiguredSearchHotkeys()
    {
        foreach (var (id, label, value) in new[]
                 {
                     (AppSearchHotkeyId, "open applications", _settings.SearchAppsHotkey),
                     (WebSearchHotkeyId, "web search", _settings.SearchWebHotkey),
                     (CombinedSearchHotkeyId, "combined search", _settings.SearchCombinedHotkey)
                 })
            if (HotkeyGesture.TryParse(value, out var gesture)) yield return (id, label, gesture);
    }

    private void SetSearchHotkeyStatus(string message, bool isError)
    {
        SearchHotkeyStatus.Text = message;
        SearchHotkeyStatus.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(251, 113, 133))
            : new SolidColorBrush(Color.FromRgb(110, 231, 183));
    }

    private bool IsBarVertical => _settings.BarOrientation.Equals("Vertical", StringComparison.OrdinalIgnoreCase);

    private void PositionWindow()
    {
        ApplyBarOrientationLayout();
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var maxLength = Math.Max(520, (IsBarVertical ? screenHeight : screenWidth) - 24);
        var barLength = Math.Clamp(_settings.BarWidth, 520, maxLength);
        var barThickness = Math.Clamp(_settings.BarHeight, 60, 82) + 8;
        var popupSize = _settingsOpen
            ? Math.Min(650, (IsBarVertical ? screenWidth : screenHeight) - barThickness - 18)
            : _backgroundProcessesOpen ? 324 : 0;

        WindowRoot.RowDefinitions.Clear();
        WindowRoot.ColumnDefinitions.Clear();
        if (!IsBarVertical)
        {
            var barX = _settings.BarX >= 0 ? _settings.BarX : (screenWidth - barLength) / 2;
            var barY = _settings.BarY >= 0 ? _settings.BarY : screenHeight - barThickness - 8;
            barX = Math.Clamp(barX, 0, Math.Max(0, screenWidth - barLength));
            barY = Math.Clamp(barY, 0, Math.Max(0, screenHeight - barThickness));
            var popupAbove = popupSize == 0 || barY >= popupSize;
            WindowRoot.ColumnDefinitions.Add(new ColumnDefinition());
            if (popupSize > 0 && popupAbove) WindowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(popupSize) });
            WindowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(barThickness) });
            if (popupSize > 0 && !popupAbove) WindowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(popupSize) });
            Grid.SetColumn(BarSurface, 0);
            Grid.SetRow(BarSurface, popupSize > 0 && popupAbove ? 1 : 0);
            PlacePopupPanels(row: popupSize > 0 && popupAbove ? 0 : 1, column: 0, horizontal: true);
            Width = barLength;
            Height = barThickness + popupSize;
            Left = barX;
            Top = popupSize > 0 && popupAbove ? barY - popupSize : barY;
        }
        else
        {
            var barX = _settings.BarX >= 0 ? _settings.BarX : 12;
            var barY = _settings.BarY >= 0 ? _settings.BarY : (screenHeight - barLength) / 2;
            barX = Math.Clamp(barX, 0, Math.Max(0, screenWidth - barThickness));
            barY = Math.Clamp(barY, 0, Math.Max(0, screenHeight - barLength));
            var popupLeft = popupSize == 0 || barX >= popupSize;
            WindowRoot.RowDefinitions.Add(new RowDefinition());
            if (popupSize > 0 && popupLeft) WindowRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(popupSize) });
            WindowRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barThickness) });
            if (popupSize > 0 && !popupLeft) WindowRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(popupSize) });
            Grid.SetRow(BarSurface, 0);
            Grid.SetColumn(BarSurface, popupSize > 0 && popupLeft ? 1 : 0);
            PlacePopupPanels(row: 0, column: popupSize > 0 && popupLeft ? 0 : 1, horizontal: false);
            Width = barThickness + popupSize;
            Height = barLength;
            Left = popupSize > 0 && popupLeft ? barX - popupSize : barX;
            Top = barY;
        }
    }

    private void PlacePopupPanels(int row, int column, bool horizontal)
    {
        foreach (var panel in new[] { SettingsPanel, BackgroundProcessesPanel })
        {
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
        }
        SettingsPanel.HorizontalAlignment = horizontal ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        SettingsPanel.VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        SettingsPanel.Margin = horizontal ? new Thickness(0, 8, 8, 8) : new Thickness(8);
        BackgroundProcessesPanel.HorizontalAlignment = horizontal ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        BackgroundProcessesPanel.VerticalAlignment = horizontal ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        BackgroundProcessesPanel.Margin = new Thickness(8);
    }

    private void ApplyBarOrientationLayout()
    {
        BarContentGrid.ColumnDefinitions.Clear();
        BarContentGrid.RowDefinitions.Clear();
        var orientation = IsBarVertical ? Orientation.Vertical : Orientation.Horizontal;
        LaunchControls.Orientation = orientation;
        AppControls.Orientation = orientation;
        SystemControls.Orientation = orientation;
        AppPager.Orientation = orientation;
        SetItemsOrientation(RunningApps, orientation);
        SetItemsOrientation(PinnedApps, orientation);

        if (IsBarVertical)
        {
            BarContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            BarContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            BarContentGrid.RowDefinitions.Add(new RowDefinition());
            BarContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(LaunchControls, 0); Grid.SetColumn(LaunchControls, 0);
            Grid.SetRow(BarDivider, 1); Grid.SetColumn(BarDivider, 0);
            Grid.SetRow(AppControls, 2); Grid.SetColumn(AppControls, 0);
            Grid.SetRow(SystemControls, 3); Grid.SetColumn(SystemControls, 0);
            BarDivider.Width = 30; BarDivider.Height = 1; BarDivider.Margin = new Thickness(9, 7, 9, 7);
            AppControls.VerticalAlignment = VerticalAlignment.Center;
            AppControls.HorizontalAlignment = HorizontalAlignment.Center;
            BarDragHandle.Width = 42; BarDragHandle.Height = 18;
        }
        else
        {
            BarContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            BarContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            BarContentGrid.ColumnDefinitions.Add(new ColumnDefinition());
            BarContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(LaunchControls, 0); Grid.SetRow(LaunchControls, 0);
            Grid.SetColumn(BarDivider, 1); Grid.SetRow(BarDivider, 0);
            Grid.SetColumn(AppControls, 2); Grid.SetRow(AppControls, 0);
            Grid.SetColumn(SystemControls, 3); Grid.SetRow(SystemControls, 0);
            BarDivider.Width = 1; BarDivider.Height = double.NaN; BarDivider.Margin = new Thickness(7, 9, 10, 9);
            AppControls.VerticalAlignment = VerticalAlignment.Center;
            AppControls.HorizontalAlignment = HorizontalAlignment.Center;
            BarDragHandle.Width = 18; BarDragHandle.Height = double.NaN;
        }
    }

    private static void SetItemsOrientation(ItemsControl control, Orientation orientation)
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, orientation);
        control.ItemsPanel = new ItemsPanelTemplate(factory);
    }

    private void UpdateWindowHeight() => PositionWindow();

    private void RefreshBar()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("h:mm");
        DateText.Text = now.ToString("MMM d").ToUpperInvariant();

        _allApps = _windowService.GetOpenWindows();
        if (!_pageAnimating) PopulateAppPage();
    }

    private int AppPageSize => Math.Max(1, (int)((_settings.BarWidth - 510 - (_pinnedApps.Count * 44)) / 46));

    private void PopulateAppPage()
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_allApps.Count / (double)AppPageSize));
        _appPage = Math.Clamp(_appPage, 0, pageCount - 1);
        _apps.Clear();
        foreach (var app in _allApps.Skip(_appPage * AppPageSize).Take(AppPageSize)) _apps.Add(app);
        AppPager.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        AppPageText.Text = $"{_appPage + 1}/{pageCount}";
    }

    private void UpdateFullscreenVisibility()
    {
        if (_keepVisibleForUiTests) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero) return;

        var shouldHide = _settings.HideInFullscreenApps && FullscreenWindowDetector.IsForegroundFullscreen(handle);
        if (shouldHide == _hiddenForFullscreen) return;
        _hiddenForFullscreen = shouldHide;

        if (shouldHide)
        {
            _startMenu?.Hide();
            SetSettingsOpen(false);
            _topOverlay?.Hide();
            _audioVisualizer?.Hide();
            Hide();
            return;
        }

        ShowActivated = false;
        Show();
        if (_settings.TopOverlayEnabled) _topOverlay?.Show();
        if (_settings.AudioVisualizerEnabled) _audioVisualizer?.Show();
    }

    private void ApplySettings()
    {
        var importedCommunityAnimation = _settings.Effect == "Custom" && _settings.CommunityAnimationActive;
        var premiumEffectReset = !_licenseService.IsPro && IsPremiumEffect(_settings.Effect) && !importedCommunityAnimation;
        var premiumFeatureReset = false;
        if (premiumEffectReset) _settings.Effect = "Rain";
        if (!_licenseService.IsPro)
        {
            premiumFeatureReset = _settings.TopShowPerformance || _settings.TopShowPower || _settings.TopShowFocus || _settings.AudioVisualizerEnabled;
            _settings.TopShowPerformance = false;
            _settings.TopShowPower = false;
            _settings.TopShowFocus = false;
            _settings.AudioVisualizerEnabled = false;
        }
        if (ColorConverter.ConvertFromString(_settings.Accent) is Color accent)
        {
            Resources["AccentBrush"] = new SolidColorBrush(accent);
            EffectsLayer.Accent = accent;
        }
        EffectsLayer.CustomEffect = _settings.CustomEffect;
        EffectsLayer.Mode = _settings.Effect;
        EffectsLayer.Intensity = _settings.EffectIntensity;
        ApplyBackgroundVisibility();
        OpacitySlider.Value = _settings.Opacity;
        BackgroundVisibleCheck.IsChecked = _settings.BackgroundVisible;
        OpacitySlider.IsEnabled = _settings.BackgroundVisible;
        IntensitySlider.Value = _settings.EffectIntensity;
        WidthSlider.Minimum = 520;
        WidthSlider.Maximum = Math.Max(520, (IsBarVertical ? SystemParameters.PrimaryScreenHeight : SystemParameters.PrimaryScreenWidth) - 24);
        WidthSlider.Value = Math.Min(_settings.BarWidth, WidthSlider.Maximum);
        WidthValueText.Text = $"{Math.Round(WidthSlider.Value)} px";
        HeightSlider.Value = _settings.BarHeight;
        CornerSlider.Value = _settings.CornerRadius;
        BarSurface.CornerRadius = new CornerRadius(_settings.CornerRadius);
        UseWindowsSearchCheck.IsChecked = _settings.UseWindowsSearch;
        AltTabEnabledCheck.IsChecked = _settings.AltTabEnabled;
        AltTabOptionsPanel.Visibility = _settings.AltTabEnabled ? Visibility.Visible : Visibility.Collapsed;
        AltTabOpacitySlider.Value = _settings.AltTabOpacity;
        AltTabBackgroundImageText.Text = string.IsNullOrWhiteSpace(_settings.AltTabBackgroundImage)
            ? "No image selected"
            : $"Image: {Path.GetFileName(_settings.AltTabBackgroundImage)}";
        SearchPaletteWidthSlider.Value = Math.Clamp(_settings.SearchPaletteWidth, 480, 900);
        SearchPaletteHeightSlider.Value = Math.Clamp(_settings.SearchPaletteHeight, 300, 720);
        SearchPaletteWidthValueText.Text = $"{Math.Round(SearchPaletteWidthSlider.Value)} px";
        SearchPaletteHeightValueText.Text = $"{Math.Round(SearchPaletteHeightSlider.Value)} px";
        SearchAppsHotkeyBox.Text = _settings.SearchAppsHotkey;
        SearchWebHotkeyBox.Text = _settings.SearchWebHotkey;
        SearchCombinedHotkeyBox.Text = _settings.SearchCombinedHotkey;
        HideNativeCheck.IsChecked = _settings.HideNativeTaskbar;
        HideInFullscreenCheck.IsChecked = _settings.HideInFullscreenApps;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        TopOverlayCheck.IsChecked = _settings.TopOverlayEnabled;
        TopClockCheck.IsChecked = _settings.TopShowClock;
        TopPerformanceCheck.IsChecked = _settings.TopShowPerformance;
        TopConnectionCheck.IsChecked = _settings.TopShowConnection;
        TopPowerCheck.IsChecked = _settings.TopShowPower;
        TopFocusCheck.IsChecked = _settings.TopShowFocus;
        TopNewsCheck.IsChecked = _settings.TopShowNews;
        AudioVisualizerCheck.IsChecked = _settings.AudioVisualizerEnabled;
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
        RefreshPinnedApps();
        ApplyBarOrientationLayout();
        if (premiumEffectReset || premiumFeatureReset) SaveSettings();
    }

    private void SaveSettings() => _settingsService.Save(_settings);

    public async Task ImportCommunityDesignAsync(string command)
    {
        try
        {
            var result = await _communityDesignService.ImportAsync(command, _settings);
            if (result.Applied)
            {
                var previous = _initializing;
                _initializing = true;
                ApplySettings();
                _initializing = previous;
                _topOverlay?.ApplyConfiguration(_settings);
            }
            Activate();
            MessageBox.Show(this,
                result.Applied
                    ? $"{result.Name} is now active in GlassBar."
                    : $"{result.Name} was added to your GlassBar Community collection. Coded widgets stay sandboxed and are never executed in the desktop process.",
                "Community design imported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"GlassBar could not import that design.\n\n{exception.Message}",
                "Design import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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

    private void CloseRunningApp_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem<AppItem>(sender) is { } app) _windowService.CloseWindow(app);
    }

    private void PinRunningApp_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem<AppItem>(sender) is not { ExecutablePath: { Length: > 0 } path } app) return;
        AddPinnedApp(app.Title, path);
    }

    private void CloseBackgroundProcess_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem<BackgroundProcessItem>(sender) is not { } process) return;
        _windowService.CloseBackgroundProcesses(process);
        Dispatcher.BeginInvoke(RefreshBackgroundProcesses, DispatcherPriority.Background);
    }

    private static T? GetContextItem<T>(object sender) where T : class
    {
        if (sender is FrameworkElement { DataContext: T direct }) return direct;
        if (sender is MenuItem menuItem &&
            ItemsControl.ItemsControlFromItemContainer(menuItem) is ContextMenu
            { PlacementTarget: FrameworkElement { DataContext: T placed } }) return placed;
        return null;
    }

    private void PinnedApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PinnedAppConfig app }) return;
        try { Process.Start(new ProcessStartInfo(app.Target) { UseShellExecute = true }); } catch { }
    }

    private void UnpinApp_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem<PinnedAppConfig>(sender) is not { } app) return;
        _settings.PinnedApps.RemoveAll(item => item.Id == app.Id);
        RefreshPinnedApps();
        SaveSettings();
    }

    private void AddPinnedApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pin an app to GlassBar",
            Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) AddPinnedApp(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName);
    }

    private void AddPinnedApp(string displayName, string target)
    {
        if (_settings.PinnedApps.Any(item => item.Target.Equals(target, StringComparison.OrdinalIgnoreCase))) return;
        _settings.PinnedApps.Add(new PinnedAppConfig { DisplayName = displayName, Target = target });
        RefreshPinnedApps();
        SaveSettings();
    }

    private void ClearPinnedApps_Click(object sender, RoutedEventArgs e)
    {
        _settings.PinnedApps.Clear();
        RefreshPinnedApps();
        SaveSettings();
    }

    private void RefreshPinnedApps()
    {
        _pinnedApps.Clear();
        foreach (var app in _settings.PinnedApps)
        {
            app.Icon = ShellIconService.GetIcon(app.Target);
            _pinnedApps.Add(app);
        }
        PopulateAppPage();
    }

    private void PreviousAppsPage_Click(object sender, RoutedEventArgs e) => FlipAppsPage(-1);
    private void NextAppsPage_Click(object sender, RoutedEventArgs e) => FlipAppsPage(1);

    private void FlipAppsPage(int direction)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_allApps.Count / (double)AppPageSize));
        if (_pageAnimating || pageCount < 2) return;
        _pageAnimating = true;
        var transform = (ScaleTransform)RunningApps.RenderTransform;
        var foldOut = new DoubleAnimation(1, 0.04, TimeSpan.FromMilliseconds(115))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        foldOut.Completed += (_, _) =>
        {
            _appPage = (_appPage + direction + pageCount) % pageCount;
            PopulateAppPage();
            var foldIn = new DoubleAnimation(0.04, 1, TimeSpan.FromMilliseconds(155))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 } };
            foldIn.Completed += (_, _) => _pageAnimating = false;
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, foldIn);
        };
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, foldOut);
    }

    private void BarDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
            var barOffset = BarSurface.TranslatePoint(new Point(0, 0), this);
            _settings.BarX = Left + barOffset.X - BarSurface.Margin.Left;
            _settings.BarY = Top + barOffset.Y - BarSurface.Margin.Top;
            SaveSettings();
            e.Handled = true;
        }
        catch (InvalidOperationException) { }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SetSettingsOpen(!_settingsOpen);
    private void CloseSettings_Click(object sender, RoutedEventArgs e) => SetSettingsOpen(false);

    private void WindowRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (_settingsOpen && !IsWithin(source, SettingsPanel) && !IsWithin(source, SettingsButton)
            && !IsStickerInteraction(source))
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

    private static bool IsStickerInteraction(DependencyObject source)
    {
        for (var current = source; current is not null; current = GetParent(current))
            if (current is Border { Tag: StickerConfig }) return true;
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

    private void OpenSearchPalette(SearchPaletteMode mode)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero || FullscreenWindowDetector.IsForegroundFullscreen(handle))
        {
            _searchPalette?.ClosePalette();
            return;
        }

        SetSettingsOpen(false);
        SetBackgroundProcessesOpen(false);
        _startMenu?.Hide();
        _searchPalette ??= new SearchPaletteWindow();
        _searchPalette.Open(mode, _settings);
    }

    private void Effect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string effect }) return;
        if (IsPremiumEffect(effect) && !EnsurePro(effect)) return;
        _settings.Effect = effect;
        _settings.CommunityAnimationActive = false;
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
        _settings.CommunityAnimationActive = false;
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

    private void ApplyBackgroundVisibility()
    {
        if (_settings.BackgroundVisible &&
            ColorConverter.ConvertFromString(_settings.BackgroundStart) is Color start &&
            ColorConverter.ConvertFromString(_settings.BackgroundEnd) is Color end)
        {
            BarSurface.Background = new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 1))
            {
                Opacity = _settings.Opacity
            };
        }
        else
        {
            BarSurface.Background = Brushes.Transparent;
        }
        if (_settings.BackgroundVisible && ColorConverter.ConvertFromString(_settings.Border) is Color border)
            BarSurface.BorderBrush = new SolidColorBrush(border) { Opacity = 0.32 };
        else
            BarSurface.BorderBrush = Brushes.Transparent;
        BarHighlight.Opacity = _settings.BackgroundVisible ? 0.28 : 0;
        BarShadow.Opacity = _settings.BackgroundVisible ? 0.48 : 0;
        BarShadow.BlurRadius = _settings.BackgroundVisible ? _settings.Shadow : 0;
    }

    private void BackgroundVisibleCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settings.BackgroundVisible = BackgroundVisibleCheck.IsChecked == true;
        OpacitySlider.IsEnabled = _settings.BackgroundVisible;
        ApplyBackgroundVisibility();
        _topOverlay?.ApplyAppearance(_settings);
        SaveSettings();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BarSurface is null) return;
        if (_initializing) return;
        _settings.Opacity = e.NewValue;
        ApplyBackgroundVisibility();
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
        UpdateWindowHeight();
        SaveSettings();
    }

    private void BarOrientation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string orientation } || _settings.BarOrientation == orientation) return;
        _settings.BarOrientation = orientation;
        _settings.BarX = -1;
        _settings.BarY = -1;
        WidthSlider.Maximum = Math.Max(520, (IsBarVertical ? SystemParameters.PrimaryScreenHeight : SystemParameters.PrimaryScreenWidth) - 24);
        _settings.BarWidth = Math.Min(_settings.BarWidth, WidthSlider.Maximum);
        WidthSlider.Value = _settings.BarWidth;
        _appPage = 0;
        PositionWindow();
        PopulateAppPage();
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

    private void HideInFullscreenCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.HideInFullscreenApps = HideInFullscreenCheck.IsChecked == true;
        SaveSettings();
        UpdateFullscreenVisibility();
    }

    private void UseWindowsSearchCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settings.UseWindowsSearch = UseWindowsSearchCheck.IsChecked == true;
        SaveSettings();
    }

    private void AltTabEnabledCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.AltTabEnabled = AltTabEnabledCheck.IsChecked == true;
        AltTabOptionsPanel.Visibility = _settings.AltTabEnabled ? Visibility.Visible : Visibility.Collapsed;
        SaveSettings();
    }

    private void AltTabBackground_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string background } ||
            background is not ("Glass" or "Dark" or "Transparent")) return;
        _settings.AltTabBackground = background;
        SaveSettings();
    }

    private void AltTabLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string layout } || layout is not ("Compact" or "Fullscreen")) return;
        _settings.AltTabLayout = layout;
        SaveSettings();
    }

    private void ChooseAltTabBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an Alt + Tab background",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _settings.AltTabBackgroundImage = BackgroundImageService.Import(dialog.FileName);
            _settings.AltTabBackground = "Image";
            AltTabBackgroundImageText.Text = $"Image: {Path.GetFileName(_settings.AltTabBackgroundImage)}";
            SaveSettings();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"GlassBar could not use that image.\n\n{exception.Message}", "Background import failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AltTabOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        _settings.AltTabOpacity = e.NewValue;
        SaveSettings();
    }

    private void SearchPaletteSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || SearchPaletteWidthSlider is null || SearchPaletteHeightSlider is null) return;
        _settings.SearchPaletteWidth = SearchPaletteWidthSlider.Value;
        _settings.SearchPaletteHeight = SearchPaletteHeightSlider.Value;
        SearchPaletteWidthValueText.Text = $"{Math.Round(_settings.SearchPaletteWidth)} px";
        SearchPaletteHeightValueText.Text = $"{Math.Round(_settings.SearchPaletteHeight)} px";
        SaveSettings();
    }

    private void SearchPaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color, CommandParameter: string target } ||
            ColorConverter.ConvertFromString(color) is not Color) return;
        switch (target)
        {
            case "Background": _settings.SearchPaletteBackground = color; break;
            case "Text": _settings.SearchPaletteText = color; break;
            case "Accent": _settings.SearchPaletteAccent = color; break;
            default: return;
        }
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
        if (sender is not CheckBox { Tag: string widget } checkBox) return;
        if (checkBox.IsChecked == true && widget is "Performance" or "Power" or "Focus" && !EnsurePro($"{widget} widget"))
        {
            checkBox.IsChecked = false;
            return;
        }
        switch (widget)
        {
            case "Clock": _settings.TopShowClock = TopClockCheck.IsChecked == true; break;
            case "Performance": _settings.TopShowPerformance = TopPerformanceCheck.IsChecked == true; break;
            case "Connection": _settings.TopShowConnection = TopConnectionCheck.IsChecked == true; break;
            case "Power": _settings.TopShowPower = TopPowerCheck.IsChecked == true; break;
            case "Focus": _settings.TopShowFocus = TopFocusCheck.IsChecked == true; break;
            case "News": _settings.TopShowNews = TopNewsCheck.IsChecked == true; break;
        }
        _topOverlay?.ApplyConfiguration(_settings);
        SaveSettings();
    }

    private void FocusDuration_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro("Focus timer")) return;
        if (sender is not Button { Tag: string value } || !int.TryParse(value, out var minutes)) return;
        _settings.TopFocusMinutes = Math.Clamp(minutes, 1, 180);
        _topOverlay?.ApplyConfiguration(_settings, fitToWidgets: false);
        SaveSettings();
    }

    private void TopBarOrientation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string orientation } || _settings.TopBarOrientation == orientation) return;
        _settings.TopBarOrientation = orientation;
        _settings.TopBarWidth = orientation == "Vertical" ? 196 : 0;
        _settings.TopBarHeight = orientation == "Vertical" ? 360 : 58;
        _topOverlay?.ApplyConfiguration(_settings, fitToWidgets: true);
        SaveSettings();
    }

    private void AudioVisualizerCheck_Click(object sender, RoutedEventArgs e)
    {
        if (AudioVisualizerCheck.IsChecked == true && !EnsurePro("Audio visualizer"))
        {
            AudioVisualizerCheck.IsChecked = false;
            return;
        }
        _settings.AudioVisualizerEnabled = AudioVisualizerCheck.IsChecked == true;
        ApplyAudioVisualizerState();
        SaveSettings();
    }

    private void ApplyAudioVisualizerState()
    {
        if (_settings.AudioVisualizerEnabled && _licenseService.IsPro)
        {
            if (_audioVisualizer is null)
            {
                _audioVisualizer = new AudioVisualizerWindow(_settings);
                _audioVisualizer.Closed += (_, _) =>
                {
                    _audioVisualizer = null;
                    if (AudioVisualizerCheck is not null) AudioVisualizerCheck.IsChecked = _settings.AudioVisualizerEnabled;
                };
                _audioVisualizer.Show();
            }
            return;
        }
        _audioVisualizer?.Close();
        _audioVisualizer = null;
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
