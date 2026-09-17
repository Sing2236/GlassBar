using System.Threading;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
        var safeMode = e.Args.Contains("--safe", StringComparer.OrdinalIgnoreCase);
        var keepVisibleForUiTests = e.Args.Contains("--qa-visible", StringComparer.OrdinalIgnoreCase);
        var window = new MainWindow(safeMode, keepVisibleForUiTests);
        window.Show();
        if (!safeMode) _altTabService = new AltTabService(Dispatcher, () => window.CurrentSettings);
        StartCommandListener(window);
        var initialCommand = e.Args.FirstOrDefault(arg => arg.StartsWith("glassbar:", StringComparison.OrdinalIgnoreCase));
        if (initialCommand is not null) Dispatcher.BeginInvoke(async () => await window.ImportCommunityDesignAsync(initialCommand));
        if (!safeMode) StartAutomaticUpdates();
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
