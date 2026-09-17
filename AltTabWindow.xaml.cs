using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class AltTabWindow : Window
{
    private readonly WindowService _windowService = new();
    private readonly ObservableCollection<AppItem> _items = [];
    private bool _hiding;

    public AltTabWindow()
    {
        InitializeComponent();
        WindowList.ItemsSource = _items;
    }

    public bool Open(BarSettings settings, bool reverse)
    {
        _items.Clear();
        foreach (var item in _windowService.GetOpenWindows()) _items.Add(item);
        if (_items.Count == 0) return false;

        ApplyAppearance(settings);
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height * 0.82);
        var activeIndex = _items.Select((item, index) => (item, index)).FirstOrDefault(pair => pair.item.IsActive).index;
        if (!_items.Any(item => item.IsActive)) activeIndex = reverse ? 0 : _items.Count - 1;
        WindowList.SelectedIndex = Wrap(activeIndex + (reverse ? -1 : 1));

        _hiding = false;
        Opacity = 0;
        SurfaceScale.ScaleX = SurfaceScale.ScaleY = 0.96;
        SurfaceTranslate.Y = 18;
        Show();
        PositionOnWorkArea();
        WindowList.ScrollIntoView(WindowList.SelectedItem);
        AnimateOpen();
        return true;
    }

    public void Roll(bool reverse)
    {
        if (_items.Count == 0) return;
        WindowList.SelectedIndex = Wrap(WindowList.SelectedIndex + (reverse ? -1 : 1));
        WindowList.ScrollIntoView(WindowList.SelectedItem);

        ListTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From = reverse ? -16 : 16,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        WindowList.BeginAnimation(OpacityProperty, new DoubleAnimation(0.58, 1, TimeSpan.FromMilliseconds(135)));
    }

    public void CompleteSelection()
    {
        if (WindowList.SelectedItem is not AppItem selected)
        {
            Cancel();
            return;
        }
        HideAnimated(() => _windowService.Activate(selected));
    }

    public void Cancel() => HideAnimated(null);

    private int Wrap(int index) => (index % _items.Count + _items.Count) % _items.Count;

    private void ApplyAppearance(BarSettings settings)
    {
        var alpha = (byte)Math.Round(Math.Clamp(settings.AltTabOpacity, 0.2, 1) * 255);
        SwitcherSurface.Background = settings.AltTabBackground switch
        {
            "Dark" => new SolidColorBrush(Color.FromArgb(alpha, 5, 9, 16)),
            "Transparent" => new SolidColorBrush(Color.FromArgb((byte)Math.Min((int)alpha, 76), 10, 17, 27)),
            _ => new SolidColorBrush(Color.FromArgb(alpha, 17, 23, 34))
        };
        SwitcherSurface.BorderBrush = settings.AltTabBackground == "Transparent"
            ? new SolidColorBrush(Color.FromArgb(72, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(62, 255, 255, 255));
    }

    private void PositionOnWorkArea()
    {
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - ActualWidth) / 2;
        Top = work.Top + (work.Height - ActualHeight) / 2;
    }

    private void AnimateOpen()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(185)) { EasingFunction = ease });
        SurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
        SurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
        SurfaceTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
    }

    private void HideAnimated(Action? afterHide)
    {
        if (_hiding || !IsVisible) return;
        _hiding = true;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(125));
        fade.Completed += (_, _) =>
        {
            Hide();
            BeginAnimation(OpacityProperty, null);
            _hiding = false;
            afterHide?.Invoke();
        };
        BeginAnimation(OpacityProperty, fade);
        SurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.98, TimeSpan.FromMilliseconds(125)));
        SurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.98, TimeSpan.FromMilliseconds(125)));
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AppItem item }) return;
        e.Handled = true;
        _windowService.CloseWindow(item);
        var oldIndex = _items.IndexOf(item);
        _items.Remove(item);
        if (_items.Count == 0)
        {
            Cancel();
            return;
        }
        WindowList.SelectedIndex = Math.Min(Math.Max(0, oldIndex), _items.Count - 1);
        WindowList.ScrollIntoView(WindowList.SelectedItem);
    }

    private void WindowList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CompleteSelection();
    }
}
