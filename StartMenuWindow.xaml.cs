using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class StartMenuWindow : Window
{
    private readonly StartMenuService _service = new();
    private readonly ObservableCollection<LaunchableApp> _visibleApps = [];
    private IReadOnlyList<LaunchableApp> _allApps = StartMenuService.GetSystemApps();
    private bool _loadedAllApps;

    public StartMenuWindow()
    {
        InitializeComponent();
        AppResults.ItemsSource = _visibleApps;
        Deactivated += (_, _) => Hide();
        ApplyFilter();
    }

    public async void OpenNear(Window taskbar)
    {
        Owner = taskbar;
        Left = Math.Max(12, Math.Min(taskbar.Left, SystemParameters.PrimaryScreenWidth - Width - 12));
        Top = Math.Max(12, taskbar.Top - Height - 10);
        Show();
        Activate();
        SearchBox.Focus();
        PlayOpenAnimation();

        if (!_loadedAllApps)
        {
            CountText.Text = "Loading more…";
            _allApps = await _service.GetAppsAsync();
            _loadedAllApps = true;
            ApplyFilter();
        }
    }

    private void PlayOpenAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        MenuSurface.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

        var contentDelay = TimeSpan.FromMilliseconds(45);
        MenuContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { BeginTime = contentDelay, EasingFunction = ease });
        MenuContentOffset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240)) { BeginTime = contentDelay, EasingFunction = ease });
        MenuEffectsLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 0.62, TimeSpan.FromMilliseconds(360))
            { BeginTime = TimeSpan.FromMilliseconds(70), EasingFunction = ease });
    }

    public void ApplyAppearance(BarSettings settings)
    {
        MenuEffectsLayer.Mode = settings.Effect;
        MenuEffectsLayer.Intensity = Math.Clamp(settings.EffectIntensity * 0.82, 0.05, 0.82);
        MenuEffectsLayer.CustomEffect = settings.CustomEffect;
        if (ColorConverter.ConvertFromString(settings.Accent) is Color accent)
            MenuEffectsLayer.Accent = accent;
        MenuEffectsLayer.RefreshCustomEffect(settings.Effect == "Custom");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var matches = _allApps
            .Where(app => query.Length == 0 || app.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(app => query.Length > 0 && app.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(app => app.IsSystemApp)
            .ThenBy(app => app.Name)
            .Take(query.Length == 0 ? 24 : 48)
            .ToList();

        _visibleApps.Clear();
        foreach (var app in matches) _visibleApps.Add(app);
        SectionTitle.Text = query.Length == 0 ? "APPS" : "SEARCH RESULTS";
        CountText.Text = $"{matches.Count} shown";
        EmptyState.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _visibleApps.FirstOrDefault() is { } first)
        {
            Launch(first);
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
    }

    private void App_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LaunchableApp app }) Launch(app);
    }

    private void Launch(LaunchableApp app)
    {
        try { _service.Launch(app); } catch { }
        Hide();
    }

    private void Documents_Click(object sender, RoutedEventArgs e) => LaunchPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    private void Settings_Click(object sender, RoutedEventArgs e) => LaunchPath("ms-settings:");

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SystemActions.LockComputer();
    }

    private void LaunchPath(string path)
    {
        try { SystemActions.Start(path); } catch { }
        Hide();
    }
}
