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
    private CancellationTokenSource? _searchCancellation;
    private bool _effectsEnabled = true;

    public StartMenuWindow()
    {
        InitializeComponent();
        AppResults.ItemsSource = _visibleApps;
        Deactivated += (_, _) =>
        {
            _searchCancellation?.Cancel();
            Hide();
        };
        ShowResults(StartMenuService.GetSystemApps().Take(24).ToList(), "");
    }

    public async void OpenNear(Window taskbar)
    {
        Owner = taskbar;
        Left = Math.Max(12, Math.Min(taskbar.Left, SystemParameters.PrimaryScreenWidth - Width - 12));
        Top = Math.Max(12, taskbar.Top - Height - 10);
        Show();
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
        PlayOpenAnimation();
        await RefreshResultsAsync(debounce: false);
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
            new DoubleAnimation(0, _effectsEnabled ? 0.78 : 0, TimeSpan.FromMilliseconds(360))
            { BeginTime = TimeSpan.FromMilliseconds(70), EasingFunction = ease });
    }

    public void ApplyAppearance(BarSettings settings)
    {
        _effectsEnabled = !settings.Effect.Equals("Off", StringComparison.OrdinalIgnoreCase);
        MenuEffectsLayer.Visibility = _effectsEnabled ? Visibility.Visible : Visibility.Collapsed;
        MenuEffectsLayer.Mode = settings.Effect;
        MenuEffectsLayer.Intensity = Math.Clamp(settings.EffectIntensity * 0.82, 0.05, 0.82);
        MenuEffectsLayer.CustomEffect = settings.CustomEffect;
        if (ColorConverter.ConvertFromString(settings.Accent) is Color accent)
            MenuEffectsLayer.Accent = accent;
        MenuEffectsLayer.RefreshCustomEffect(settings.Effect == "Custom");
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        await RefreshResultsAsync(debounce: true);
    }

    private async Task RefreshResultsAsync(bool debounce)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var query = SearchBox.Text.Trim();
        CountText.Text = query.Length == 0 ? "Loading apps…" : "Searching Windows…";

        try
        {
            if (debounce) await Task.Delay(130, cancellation.Token);
            var matches = await _service.SearchAsync(query, cancellation.Token);
            if (!cancellation.IsCancellationRequested && IsVisible) ShowResults(matches, query);
        }
        catch (OperationCanceledException) { }
    }

    private void ShowResults(IReadOnlyList<LaunchableApp> matches, string query)
    {
        _visibleApps.Clear();
        foreach (var app in matches) _visibleApps.Add(app);
        AppResults.SelectedIndex = matches.Count > 0 ? 0 : -1;
        SectionTitle.Text = query.Length == 0 ? "TOP APPS" : "BEST MATCHES";
        CountText.Text = query.Length == 0 ? $"{matches.Count} apps" : $"{matches.Count} results";
        EmptyState.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp)
        {
            var step = e.Key switch { Key.Up => -1, Key.PageUp => -6, Key.PageDown => 6, _ => 1 };
            MoveSelection(step);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && (AppResults.SelectedItem as LaunchableApp ?? _visibleApps.FirstOrDefault()) is { } selected)
        {
            Launch(selected);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (SearchBox.Text.Length > 0) SearchBox.Clear(); else Hide();
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
    }

    private void MoveSelection(int step)
    {
        if (_visibleApps.Count == 0) return;
        var current = Math.Max(0, AppResults.SelectedIndex);
        AppResults.SelectedIndex = Math.Clamp(current + step, 0, _visibleApps.Count - 1);
        AppResults.ScrollIntoView(AppResults.SelectedItem);
    }

    private void AppResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var item = FindParent<ListBoxItem>(source);
        if (item?.DataContext is LaunchableApp app) Launch(app);
    }

    private static T? FindParent<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
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
