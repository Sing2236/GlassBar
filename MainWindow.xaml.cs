using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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

    private void SetSettingsOpen(bool open)
    {
        if (open) _startMenu?.Hide();
        _settingsOpen = open;
        SettingsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Height = open ? Math.Min(690, SystemParameters.PrimaryScreenHeight - 18) : _settings.BarHeight + 10;
        PositionWindow();
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
