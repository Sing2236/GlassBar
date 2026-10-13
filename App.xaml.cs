using System.Threading;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GlassBar.Models;
using GlassBar.Services;

namespace GlassBar;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private DispatcherTimer? _updateTimer;
    private int _updateCheckRunning;
    private AvailableUpdate? _pendingUpdate;
    private Version? _dismissedUpdateVersion;
    private CancellationTokenSource? _commandListenerCancellation;
    private AltTabService? _altTabService;
    private const string CommandPipe = "GlassBar.DesignCommands.v1";

    protected override void OnStartup(StartupEventArgs e)
    {
        if (TryRunWatchdog(e.Args)) return;

        _singleInstance = new Mutex(true, "GlassBar.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            var command = e.Args.FirstOrDefault(arg => arg.StartsWith("glassbar:", StringComparison.OrdinalIgnoreCase));
            if (command is not null) SendCommandToPrimary(command);
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
        // Start indexing installed apps/Start Menu shortcuts immediately, in
        // the background, instead of waiting for it to start lazily on the
        // user's first search. That lazy start is what caused searches to
        // appear to "not work" for 30s-60s right after launch -- the index
        // (which enumerates every installed app via COM Shell and resolves
        // every icon) hadn't even started yet, and the first search had to
        // wait for the whole thing before showing any results.
        StartMenuService.WarmUp();
        var safeMode = e.Args.Contains("--safe", StringComparer.OrdinalIgnoreCase);
        var keepVisibleForUiTests = e.Args.Contains("--qa-visible", StringComparer.OrdinalIgnoreCase);
        var window = new MainWindow(safeMode, keepVisibleForUiTests);
        window.Show();
        if (!safeMode) _altTabService = new AltTabService(Dispatcher, () => window.CurrentSettings);
        StartCommandListener(window);
        var initialCommand = e.Args.FirstOrDefault(arg => arg.StartsWith("glassbar:", StringComparison.OrdinalIgnoreCase));
        if (initialCommand is not null) Dispatcher.BeginInvoke(async () => await window.ImportCommunityDesignAsync(initialCommand));
        if (!safeMode) StartAutomaticUpdates(window);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateTimer?.Stop();
        _commandListenerCancellation?.Cancel();
        _altTabService?.Dispose();
        NativeTaskbar.Show();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void StartCommandListener(MainWindow window)
    {
        _commandListenerCancellation = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_commandListenerCancellation.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(CommandPipe, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(_commandListenerCancellation.Token);
                    using var reader = new StreamReader(pipe);
                    var command = await reader.ReadToEndAsync(_commandListenerCancellation.Token);
                    if (command.StartsWith("glassbar:", StringComparison.OrdinalIgnoreCase))
                        await Dispatcher.InvokeAsync(async () => await window.ImportCommunityDesignAsync(command)).Task.Unwrap();
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        });
    }

    private static void SendCommandToPrimary(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", CommandPipe, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.Write(command);
        }
        catch { }
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

    private void StartAutomaticUpdates(MainWindow window)
    {
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync(window);
        _updateTimer.Start();
        _ = CheckForUpdateAfterStartupAsync(window);
    }

    private async Task CheckForUpdateAfterStartupAsync(MainWindow window)
    {
        await Task.Delay(TimeSpan.FromSeconds(12));
        await CheckForUpdateAsync(window);
    }

    private async Task<(UpdateCheckResult Outcome, Version? Version)> CheckForUpdateAsync(
        MainWindow window, bool manual = false)
    {
        if (Interlocked.Exchange(ref _updateCheckRunning, 1) != 0)
            return (UpdateCheckResult.Busy, null);
        try
        {
            var update = _pendingUpdate ?? await UpdateService.CheckForUpdateAsync();
            if (update is null)
            {
                SetUpdateCheckInterval(TimeSpan.FromHours(4));
                return (UpdateCheckResult.UpToDate, null);
            }

            // A version you said "not now" to is only skipped by the automatic
            // check. Asking by hand means you want an answer, even if you declined
            // that same version the last time it came up.
            if (!manual && _dismissedUpdateVersion is not null
                && update.Version.Equals(_dismissedUpdateVersion))
            {
                _pendingUpdate = null;
                SetUpdateCheckInterval(TimeSpan.FromHours(4));
                return (UpdateCheckResult.UpToDate, update.Version);
            }

            var handle = new WindowInteropHelper(window).Handle;
            if (handle != nint.Zero && FullscreenWindowDetector.IsForegroundFullscreen(handle))
            {
                _pendingUpdate = update;
                SetUpdateCheckInterval(TimeSpan.FromMinutes(5));
                return (UpdateCheckResult.Deferred, update.Version);
            }

            _pendingUpdate = null;
            SetUpdateCheckInterval(TimeSpan.FromHours(4));
            var prompt = new UpdatePromptWindow(update) { Owner = window };
            var result = prompt.ShowDialog();
            if (result == true && prompt.UpdateLaunched)
                Shutdown();
            else
                _dismissedUpdateVersion = update.Version;
            return (result == true ? UpdateCheckResult.Updating : UpdateCheckResult.Dismissed,
                    update.Version);
        }
        catch (Exception exception)
        {
            UpdateService.LogFailure(exception);
            return (UpdateCheckResult.Failed, null);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckRunning, 0);
        }
    }

    /// <summary>
    /// The settings panel's "Check for updates". Runs the same check the timer does,
    /// but on demand and with something to say about the outcome, so the updater can
    /// be exercised without waiting four hours or restarting.
    /// </summary>
    internal async Task<string> CheckForUpdatesNowAsync(MainWindow window)
    {
        var (outcome, version) = await CheckForUpdateAsync(window, manual: true);
        return outcome switch
        {
            UpdateCheckResult.UpToDate => $"GlassBar {UpdateService.CurrentVersion.ToString(3)} is up to date.",
            UpdateCheckResult.Dismissed => $"Update to {version} is available.",
            UpdateCheckResult.Deferred =>
                "An update is available, but something is fullscreen. Try again in a moment.",
            UpdateCheckResult.Busy => "A check is already running.",
            UpdateCheckResult.Failed => "Could not reach the update server.",
            _ => string.Empty,
        };
    }

    private void SetUpdateCheckInterval(TimeSpan interval)
    {
        if (_updateTimer is not null) _updateTimer.Interval = interval;
    }
}

/// <summary>How a single update check ended.</summary>
internal enum UpdateCheckResult
{
    UpToDate,
    Updating,
    Dismissed,
    Deferred,
    Busy,
    Failed
}
