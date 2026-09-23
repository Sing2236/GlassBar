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

    private async Task CheckForUpdateAsync(MainWindow window)
    {
        if (Interlocked.Exchange(ref _updateCheckRunning, 1) != 0) return;
        try
        {
            var update = _pendingUpdate ?? await UpdateService.CheckForUpdateAsync();
            if (update is null)
            {
                SetUpdateCheckInterval(TimeSpan.FromHours(4));
                return;
            }

            if (_dismissedUpdateVersion is not null && update.Version.Equals(_dismissedUpdateVersion))
            {
                _pendingUpdate = null;
                SetUpdateCheckInterval(TimeSpan.FromHours(4));
                return;
            }

            var handle = new WindowInteropHelper(window).Handle;
            if (handle != nint.Zero && FullscreenWindowDetector.IsForegroundFullscreen(handle))
            {
                _pendingUpdate = update;
                SetUpdateCheckInterval(TimeSpan.FromMinutes(5));
                return;
            }

            _pendingUpdate = null;
            SetUpdateCheckInterval(TimeSpan.FromHours(4));
            var prompt = new UpdatePromptWindow(update) { Owner = window };
            var result = prompt.ShowDialog();
            if (result == true && prompt.UpdateLaunched)
                Shutdown();
            else
                _dismissedUpdateVersion = update.Version;
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckRunning, 0);
        }
    }

    private void SetUpdateCheckInterval(TimeSpan interval)
    {
        if (_updateTimer is not null) _updateTimer.Interval = interval;
    }
}
