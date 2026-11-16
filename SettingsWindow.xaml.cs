using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassBar.Interop;

namespace GlassBar;

/// <summary>
/// Modal host for the settings panel. The panel itself is declared in
/// MainWindow.xaml because all of its event handlers and live-preview logic
/// live in MainWindow; this window supplies the dimmed backdrop, centering on
/// the monitor the bar is on, the entrance animation, and close gestures
/// (Escape, backdrop click, Alt+F4).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;

    public SettingsWindow(MainWindow owner)
    {
        _owner = owner;
        InitializeComponent();
        Owner = owner;
    }

    public void Open(UIElement panel)
    {
        // Adopt the panel from whatever host had it before (MainWindow the
        // first time, this window's host afterwards). A closed window can
        // still be the logical parent, so detach from it first.
        if (!ReferenceEquals(PanelHost.Content, panel))
        {
            if (panel is FrameworkElement element && element.Parent is ContentControl previous)
                previous.Content = null;
            PanelHost.Content = panel;
        }
        if (panel is FrameworkElement content) content.Visibility = Visibility.Visible;

        // The panel's accent-dependent DynamicResources resolve against this
        // window's dictionary once it is reparented here.
        if (_owner.Resources["AccentBrush"] is Brush accent)
            Resources["AccentBrush"] = accent;

        PositionOnOwnerMonitor();
        Show();
        Activate();
        PlayOpenAnimation();

        // Put keyboard focus on the first rail category (not the close button,
        // which is earlier in the tree) so Escape works and the panel is
        // immediately usable from the keyboard.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FindRailButton(PanelHost) is { } first) Keyboard.Focus(first);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static UIElement? FindRailButton(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button { IsEnabled: true, Focusable: true } button &&
                button.Visibility == Visibility.Visible &&
                System.Windows.Automation.AutomationProperties.GetAutomationId(button)
                    .StartsWith("SettingsNav", StringComparison.Ordinal))
                return button;
            if (FindRailButton(child) is { } found) return found;
        }
        return null;
    }

    private void PositionOnOwnerMonitor()
    {
        var handle = new WindowInteropHelper(_owner).Handle;
        var monitor = handle != nint.Zero
            ? NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST)
            : nint.Zero;

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left;
            Top = work.Top;
            Width = work.Width;
            Height = work.Height;
            return;
        }

        // The backdrop covers the whole monitor; the panel inside it is
        // centred by the layout. Clamp the panel so it still fits on small or
        // heavily scaled displays.
        Left = info.Monitor.Left;
        Top = info.Monitor.Top;
        Width = info.Monitor.Right - info.Monitor.Left;
        Height = info.Monitor.Bottom - info.Monitor.Top;

        if (PanelHost.Content is FrameworkElement panel)
        {
            panel.MaxWidth = Math.Max(560, info.WorkArea.Right - info.WorkArea.Left - 96);
            panel.MaxHeight = Math.Max(420, info.WorkArea.Bottom - info.WorkArea.Top - 96);
        }
    }

    private void PlayOpenAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Backdrop.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
        HostScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
        HostScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.965, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
        HostTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _owner.CloseSettings();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _owner.CloseSettings();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        // Alt+F4 (or any other path that actually closes the window) must not
        // leave MainWindow believing settings are still open.
        _owner.SettingsWindowClosed();
    }
}
