using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class SearchPaletteWindow : Window
{
    private readonly WindowService _windowService = new();
    private readonly StartMenuService _startMenuService = new();
    private readonly BrowserSearchService _browserService = new();
    private readonly ObservableCollection<SearchPaletteItem> _results = [];
    private CancellationTokenSource? _searchCancellation;
    private SearchPaletteMode _mode;
    private BarSettings? _settings;
    private double _expandedHeight = 472;
    private const double CompactHeight = 116;

    public SearchPaletteWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
        Deactivated += (_, _) => ClosePalette();
    }

    public void Open(SearchPaletteMode mode, BarSettings settings)
    {
        _settings = settings;
        ApplyAppearance(settings);
        SetMode(mode, refresh: false);
        QueryBox.Clear();
        SetExpanded(false, animate: false);
        PositionOnWorkArea();
        Opacity = 0;
        PaletteScale.ScaleX = PaletteScale.ScaleY = 0.97;
        PaletteOffset.Y = 12;
        Show();
        Activate();
        QueryBox.Focus();
        PlayOpenAnimation();
        _ = RefreshResultsAsync(debounce: false);
    }

    public void ClosePalette()
    {
        _searchCancellation?.Cancel();
        Hide();
    }

    private void ApplyAppearance(BarSettings settings)
    {
        Width = Math.Clamp(settings.SearchPaletteWidth, 480, 900);
        _expandedHeight = Math.Clamp(settings.SearchPaletteHeight, 300, 720);
        if (ColorConverter.ConvertFromString(settings.SearchPaletteBackground) is Color background)
            PaletteSurface.Background = new SolidColorBrush(Color.FromArgb(246, background.R, background.G, background.B));
        if (ColorConverter.ConvertFromString(settings.SearchPaletteText) is Color text)
        {
            Resources["PaletteTextBrush"] = new SolidColorBrush(text);
            Resources["PaletteMutedBrush"] = new SolidColorBrush(Color.FromArgb(190, text.R, text.G, text.B));
        }
        if (ColorConverter.ConvertFromString(settings.SearchPaletteAccent) is Color accent)
        {
            Resources["PaletteAccentBrush"] = new SolidColorBrush(accent);
            Resources["PaletteAccentSurfaceBrush"] = new SolidColorBrush(Color.FromArgb(61, accent.R, accent.G, accent.B));
            PaletteSurface.BorderBrush = new SolidColorBrush(Color.FromArgb(105, accent.R, accent.G, accent.B));
        }
    }

    private void PositionOnWorkArea()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + Math.Max(36, work.Height * 0.18);
    }

    private void PlayOpenAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(145)) { EasingFunction = ease });
        PaletteScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        PaletteScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        PaletteOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    private async void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        QueryHint.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetExpanded(QueryBox.Text.Length > 0, animate: IsVisible);
        if (IsVisible) await RefreshResultsAsync(debounce: true);
    }

    private void SetExpanded(bool expanded, bool animate)
    {
        ModePanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ResultsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        FooterPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        var targetHeight = expanded ? _expandedHeight : CompactHeight;
        if (!animate)
        {
            BeginAnimation(HeightProperty, null);
            Height = targetHeight;
            return;
        }

        var animation = new DoubleAnimation(ActualHeight > 0 ? ActualHeight : Height, targetHeight,
            TimeSpan.FromMilliseconds(expanded ? 175 : 135))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(HeightProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private async Task RefreshResultsAsync(bool debounce)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var query = QueryBox.Text.Trim();

        try
        {
            if (debounce) await Task.Delay(100, cancellation.Token);
            if (query.Length == 0)
            {
                if (!cancellation.IsCancellationRequested && IsVisible) ShowResults([], query);
                return;
            }
            var results = _mode switch
            {
                SearchPaletteMode.Apps => await SearchApplicationsAsync(query, cancellation.Token),
                SearchPaletteMode.Web => await SearchWebAsync(query, cancellation.Token),
                _ => await SearchCombinedAsync(query, cancellation.Token)
            };
            if (!cancellation.IsCancellationRequested && IsVisible) ShowResults(results, query);
        }
        catch (OperationCanceledException) { }
    }

    private IReadOnlyList<SearchPaletteItem> SearchOpenWindows(string query) => _windowService.GetOpenWindows()
        .Where(item => query.Length == 0 || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       item.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(item => item.IsActive)
        .ThenBy(item => item.Title)
        .Take(10)
        .Select(item => new SearchPaletteItem
        {
            Name = item.Title,
            Subtitle = item.ProcessName,
            Kind = "Open app",
            Icon = item.Icon,
            Window = item
        }).ToList();

    private async Task<IReadOnlyList<SearchPaletteItem>> SearchApplicationsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var results = SearchOpenWindows(query).ToList();
        var installedApps = await _startMenuService.SearchAppsAsync(query, cancellationToken);
        results.AddRange(installedApps.Select(ToPaletteItem));

        return results
            .GroupBy(item => $"{item.Kind}|{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToList();
    }

    private async Task<IReadOnlyList<SearchPaletteItem>> SearchWebAsync(string query, CancellationToken cancellationToken)
    {
        var history = await _browserService.SearchHistoryAsync(query, cancellationToken);
        var results = history.Select(item => new SearchPaletteItem
        {
            Name = item.Title,
            Subtitle = item.Url,
            Kind = "History",
            Url = item.Url
        }).ToList();
        AddWebSearch(results, query);
        return results.Take(10).ToList();
    }

    private async Task<IReadOnlyList<SearchPaletteItem>> SearchCombinedAsync(string query, CancellationToken cancellationToken)
    {
        var results = SearchOpenWindows(query).ToList();
        var local = await _startMenuService.SearchAsync(query, cancellationToken);
        results.AddRange(local.Where(item => item.Kind != "Web").Select(ToPaletteItem));

        if (query.Length > 0)
        {
            var history = await _browserService.SearchHistoryAsync(query, cancellationToken);
            results.AddRange(history.Take(3).Select(item => new SearchPaletteItem
            {
                Name = item.Title,
                Subtitle = item.Url,
                Kind = "History",
                Url = item.Url
            }));
            AddWebSearch(results, query);
        }

        return results.GroupBy(item => $"{item.Kind}|{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).Take(10).ToList();
    }

    private static SearchPaletteItem ToPaletteItem(LaunchableApp item) => new()
    {
        Name = item.Name,
        Subtitle = item.Subtitle,
        Kind = item.Kind,
        Icon = item.Icon,
        Launchable = item
    };

    private void AddWebSearch(ICollection<SearchPaletteItem> results, string query)
    {
        if (query.Length == 0) return;
        results.Add(new SearchPaletteItem
        {
            Name = $"Search for “{query}”",
            Subtitle = "Open with your default browser and search engine",
            Kind = "Web",
            Url = _browserService.BuildSearchUrl(query)
        });
    }

    private void ShowResults(IReadOnlyList<SearchPaletteItem> results, string query)
    {
        _results.Clear();
        foreach (var result in results) _results.Add(result);
        ResultsList.SelectedIndex = results.Count > 0 ? 0 : -1;
        ResultCount.Text = query.Length == 0 ? string.Empty : results.Count == 1 ? "1 result" : $"{results.Count} results";
        EmptyState.Visibility = query.Length > 0 && results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string value } ||
            !Enum.TryParse<SearchPaletteMode>(value, out var mode)) return;
        SetMode(mode, refresh: true);
    }

    private void SetMode(SearchPaletteMode mode, bool refresh)
    {
        _mode = mode;
        AppsModeButton.IsChecked = mode == SearchPaletteMode.Apps;
        WebModeButton.IsChecked = mode == SearchPaletteMode.Web;
        CombinedModeButton.IsChecked = mode == SearchPaletteMode.Combined;
        QueryHint.Text = mode switch
        {
            SearchPaletteMode.Apps => "Search open and installed applications",
            SearchPaletteMode.Web => "Search browser history and the web",
            _ => "Search apps, files, settings, and the web"
        };
        ModeHotkeyText.Text = mode switch
        {
            SearchPaletteMode.Apps => _settings?.SearchAppsHotkey ?? "Ctrl + Alt + Space",
            SearchPaletteMode.Web => _settings?.SearchWebHotkey ?? "Ctrl + Alt + W",
            _ => _settings?.SearchCombinedHotkey ?? "Ctrl + Alt + A"
        };
        EmptyHint.Text = mode == SearchPaletteMode.Web ? "Type to search the web" : "Try a different search";
        if (refresh) _ = RefreshResultsAsync(debounce: false);
        QueryBox.Focus();
    }

    private void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp)
        {
            var step = e.Key switch { Key.Up => -1, Key.PageUp => -5, Key.PageDown => 5, _ => 1 };
            MoveSelection(step);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ResultsList.SelectedItem is SearchPaletteItem selected) Launch(selected);
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClosePalette();
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key is Key.D1 or Key.NumPad1) SetMode(SearchPaletteMode.Apps, refresh: true);
        else if (e.Key is Key.D2 or Key.NumPad2) SetMode(SearchPaletteMode.Web, refresh: true);
        else if (e.Key is Key.D3 or Key.NumPad3) SetMode(SearchPaletteMode.Combined, refresh: true);
        else return;
        e.Handled = true;
    }

    private void MoveSelection(int step)
    {
        if (_results.Count == 0) return;
        ResultsList.SelectedIndex = Math.Clamp(Math.Max(0, ResultsList.SelectedIndex) + step, 0, _results.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        for (var current = e.OriginalSource as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not ListBoxItem { DataContext: SearchPaletteItem item }) continue;
            Launch(item);
            e.Handled = true;
            return;
        }
    }

    private void Launch(SearchPaletteItem item)
    {
        try
        {
            if (item.Window is not null) _windowService.Activate(item.Window);
            else if (item.Launchable is not null) _startMenuService.Launch(item.Launchable);
            else if (!string.IsNullOrWhiteSpace(item.Url)) _browserService.Open(item.Url);
        }
        catch { }
        ClosePalette();
    }
}
