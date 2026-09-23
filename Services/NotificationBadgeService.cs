using GlassBar.Models;
using Windows.UI.Notifications.Management;

namespace GlassBar.Services;

/// <summary>
/// Best-effort per-app notification counts, backed by Windows' own
/// UserNotificationListener (the same system the native Action Center
/// reads from). This only sees notifications that actually go through the
/// Windows toast/notification system -- apps that show their own custom
/// in-app badges without posting a Windows toast (some Electron apps in
/// certain configurations, for example) won't be reflected here. That's an
/// inherent limitation of using the OS-level API rather than something
/// this class gets wrong.
///
/// IMPORTANT: the first time this runs, Windows will prompt the user (via
/// a system notification / Settings link) to grant "let this app read your
/// notifications" access. Until that's granted, GetCount always returns 0
/// (a safe, silent no-op) rather than throwing or blocking anything --
/// badges just don't appear until access is granted. This needed a
/// Windows-versioned TargetFramework (see GlassBar.csproj) to compile, and
/// its actual runtime behavior (the permission prompt, real notifications
/// incrementing real counts) needs to be verified live on a real Windows
/// session with real notification-producing apps -- that's not something
/// that can be confirmed from a build/compile check alone.
/// </summary>
public sealed class NotificationBadgeService
{
    private UserNotificationListener? _listener;
    private bool _accessRequested;
    private bool _accessGranted;

    /// <summary>
    /// Fire-and-forget. Safe to call multiple times or before the listener
    /// is ready -- GetCount degrades to 0 until this completes successfully.
    /// </summary>
    public async void RequestAccessAsync()
    {
        if (_accessRequested) return;
        _accessRequested = true;

        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            _accessGranted = status == UserNotificationListenerAccessStatus.Allowed;
        }
        catch
        {
            // Notification listener APIs can be unavailable (older Windows
            // builds, restricted environments, group policy, etc.) -- treat
            // that identically to "access not granted" rather than crashing
            // the bar over an optional feature.
            _accessGranted = false;
        }
    }

    /// <summary>
    /// Best-effort count of current notifications attributed to the given
    /// window's app. Matches by comparing the notification's app display
    /// name against the window's process name / title (case-insensitive
    /// substring match) -- there's no fully reliable, generally-available
    /// way to map an arbitrary AppUserModelId back to a classic Win32
    /// process name for every app, so this is a heuristic, not exact.
    /// Returns 0 (no badge) on any failure or when access hasn't been
    /// granted yet.
    /// </summary>
    public int GetCount(AppItem app)
    {
        if (!_accessGranted || _listener is null) return 0;

        try
        {
            var notifications = _listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast)
                .AsTask().GetAwaiter().GetResult();

            var processName = app.ProcessName;
            var title = app.Title;

            return notifications.Count(notification =>
            {
                var displayName = notification.AppInfo?.DisplayInfo?.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName)) return false;
                return displayName.Contains(processName, StringComparison.OrdinalIgnoreCase) ||
                       processName.Contains(displayName, StringComparison.OrdinalIgnoreCase) ||
                       title.Contains(displayName, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch
        {
            return 0;
        }
    }
}
