using System.Threading;
using System.Diagnostics;
using System.Windows;
using GlassBar.Services;

namespace GlassBar;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;

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
        new MainWindow(e.Args.Contains("--safe", StringComparer.OrdinalIgnoreCase)).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
}
