using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class UpdatePromptWindow : Window
{
    private readonly AvailableUpdate _update;
    private bool _isUpdating;

    internal bool UpdateLaunched { get; private set; }

    internal UpdatePromptWindow(AvailableUpdate update)
    {
        _update = update;
        InitializeComponent();
        TitleText.Text = $"Update to GlassBar {update.Version}?";
        Closing += Window_Closing;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PromptSurface.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        PromptScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        PromptScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        UpdateButton.Focus();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _isUpdating) return;
        DialogResult = false;
        e.Handled = true;
    }

    private void NotNow_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUpdating) DialogResult = false;
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;
        SetDownloadingState();

        if (await UpdateService.DownloadAndLaunchAsync(_update))
        {
            UpdateLaunched = true;
            DialogResult = true;
            return;
        }

        _isUpdating = false;
        TitleText.Text = "Update could not be prepared";
        MessageText.Text = "Check your connection and try again. GlassBar will keep running normally.";
        DownloadProgress.Visibility = Visibility.Collapsed;
        UpdateButton.Content = "Try again";
        UpdateButton.IsEnabled = true;
        NotNowButton.IsEnabled = true;
        CloseButton.IsEnabled = true;
        UpdateButton.Focus();
    }

    private void SetDownloadingState()
    {
        _isUpdating = true;
        TitleText.Text = $"Downloading GlassBar {_update.Version}";
        MessageText.Text = "Verifying the signed installer before anything is changed.";
        DownloadProgress.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = false;
        NotNowButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isUpdating && !UpdateLaunched) e.Cancel = true;
    }
}
