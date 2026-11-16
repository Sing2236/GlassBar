using GlassBar.Models;
using Windows.UI.Notifications;
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
    // The notification list is fetched on a background continuation and scored
    // from a cached snapshot. The previous implementation did the WinRT fetch
    // with GetAwaiter().GetResult() on the WPF UI thread once per second, which
    // is a blocking cross-process call into the notification service: on
    // machines where that service is slow to answer (or holds a long list of
    // toasts) the whole bar froze for roughly a second per refresh, which is
    // exactly the "freezes every couple of seconds" symptom on slower devices.
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(5);

    private UserNotificationListener? _listener;
    private bool _accessRequested;
    private bool _accessGranted;
    private string[] _displayNames = [];
    private DateTime _snapshotAt = DateTime.MinValue;
    private int _fetching;

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
        RefreshSnapshotIfStale();
        return _displayNames.Count(displayName => MatchesName(displayName, app.ProcessName, app.Title));
    }

    /// <summary>
    /// Sets every app's NotificationCount from the cached notification
    /// snapshot. This never blocks: it scores the current snapshot (a plain
    /// string array) and, if that snapshot is old, starts a background
    /// refresh that lands on a later tick. The previous version made the
    /// blocking WinRT fetch inline here, on the UI thread, every second --
    /// see the comment on SnapshotLifetime.
    ///
    /// As with GetCount, counts are left at 0 when access hasn't been granted
    /// (or the listener isn't ready), and on any failure -- the caller's
    /// freshly built AppItems already default to 0.
    /// </summary>
    public void ApplyCounts(IReadOnlyList<AppItem> apps)
    {
        if (!_accessGranted || _listener is null) return;
        if (apps.Count == 0) return;

        RefreshSnapshotIfStale();
        var displayNames = _displayNames;
        if (displayNames.Length == 0) return;

        foreach (var app in apps)
            app.NotificationCount = displayNames.Count(name => MatchesName(name, app.ProcessName, app.Title));
    }

    private void RefreshSnapshotIfStale()
    {
        if (_listener is null) return;
        if (DateTime.UtcNow - _snapshotAt < SnapshotLifetime) return;
        if (Interlocked.Exchange(ref _fetching, 1) != 0) return;
        _ = RefreshSnapshotAsync();
    }

    /// <summary>
    /// Fire-and-forget. Awaits the WinRT call instead of blocking a thread,
    /// then publishes the display names as one immutable array so a reader
    /// on the UI thread never observes a half-built list.
    /// </summary>
    private async Task RefreshSnapshotAsync()
    {
        try
        {
            var listener = _listener;
            if (listener is null) return;
            var notifications = await listener
                .GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast).AsTask();
            _displayNames = notifications
                .Select(notification => notification.AppInfo?.DisplayInfo?.DisplayName)
                .Where(displayName => !string.IsNullOrWhiteSpace(displayName))
                .Select(displayName => displayName!)
                .ToArray();
            _snapshotAt = DateTime.UtcNow;
        }
        catch
        {
            // Back off instead of retrying on every tick, but do retry: a
            // transient failure should not disable badges until restart.
            _snapshotAt = DateTime.UtcNow - SnapshotLifetime + TimeSpan.FromSeconds(20);
        }
        finally
        {
            Interlocked.Exchange(ref _fetching, 0);
        }
    }

    private static bool MatchesName(string? displayName, string processName, string title)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return false;
        return displayName.Contains(processName, StringComparison.OrdinalIgnoreCase) ||
               processName.Contains(displayName, StringComparison.OrdinalIgnoreCase) ||
               title.Contains(displayName, StringComparison.OrdinalIgnoreCase);
    }
}
