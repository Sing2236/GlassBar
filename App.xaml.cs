using System.Threading;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using GlassBar.Services;

namespace GlassBar;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private DispatcherTimer? _updateTimer;
    private int _updateCheckRunning;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (TryRunWatchdog(e.Args)) return;

        _singleInstance = new Mutex(true, "GlassBar.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => NativeTaskbar.Show();
        DispatcherUnhandledException += (_, args) =>
        {
            NativeTaskbar.Show();
            MessageBox.Show(args.Exception.Message, "GlassBar recovered safely", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };

        base.OnStartup(e);
        StartWatchdog();
        var safeMode = e.Args.Contains("--safe", StringComparer.OrdinalIgnoreCase);
        var keepVisibleForUiTests = e.Args.Contains("--qa-visible", StringComparer.OrdinalIgnoreCase);
        new MainWindow(safeMode, keepVisibleForUiTests).Show();
        if (!safeMode) StartAutomaticUpdates();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateTimer?.Stop();
        NativeTaskbar.Show();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private bool TryRunWatchdog(string[] args)
    {
        if (args.Length != 2 || !args[0].Equals("--watchdog", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(args[1], out var processId)) return false;

        try { Process.GetProcessById(processId).WaitForExit(); } catch { }
        NativeTaskbar.ForceShow();
        Shutdown();
        return true;
    }

    private static void StartWatchdog()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return;
            Process.Start(new ProcessStartInfo(executable, $"--watchdog {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    private void StartAutomaticUpdates()
    {
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateTimer.Start();
        _ = CheckForUpdateAfterStartupAsync();
    }

    private async Task CheckForUpdateAfterStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(12));
        await CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        if (Interlocked.Exchange(ref _updateCheckRunning, 1) != 0) return;
        try
        {
            if (await UpdateService.TryLaunchUpdateAsync()) Shutdown();
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckRunning, 0);
        }
    }
}
