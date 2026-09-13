using System.Diagnostics;
using System.IO;
using GlassBar.Models;

namespace GlassBar.Services;

public sealed class StartMenuService
{
    private IReadOnlyList<LaunchableApp>? _cache;

    public static IReadOnlyList<LaunchableApp> GetSystemApps() =>
    [
        SystemApp("Settings", "ms-settings:"),
        SystemApp("File Explorer", "explorer.exe"),
        SystemApp("Terminal", "wt.exe"),
        SystemApp("Task Manager", "taskmgr.exe"),
        SystemApp("Control Panel", "control.exe"),
        SystemApp("Windows Security", "windowsdefender:")
    ];

    public async Task<IReadOnlyList<LaunchableApp>> GetAppsAsync()
    {
        if (_cache is not null) return _cache;
        _cache = await Task.Run(LoadApps);
        return _cache;
    }

    public void Launch(LaunchableApp app)
    {
        Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
    }

    private static IReadOnlyList<LaunchableApp> LoadApps()
    {
        var apps = new List<LaunchableApp>(GetSystemApps());

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    apps.Add(new LaunchableApp { Name = name, Path = path, Icon = ShellIconService.GetIcon(path) });
                }
            }
            catch { }
        }

        return apps
            .GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(app => app.IsSystemApp)
            .ThenBy(app => app.Name)
            .ToList();
    }

    private static LaunchableApp SystemApp(string name, string path) => new()
    {
        Name = name,
        Path = path,
        Icon = ShellIconService.GetIcon(path),
        IsSystemApp = true
    };
}
