using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassBar.Models;

namespace GlassBar.Services;

public sealed class StartMenuService
{
    // Shared across every StartMenuService instance (not just per-instance)
    // so a proactive warm-up call made anywhere (see App.OnStartup) benefits
    // whichever instance later services a real search -- this is what makes
    // WarmUp() actually useful instead of just indexing twice.
    private static readonly object CacheLock = new();
    private static Task<IReadOnlyList<LaunchableApp>>? _cacheTask;

    /// <summary>
    /// Kicks off the (slow: enumerates every installed app via COM Shell,
    /// resolves every icon, scans Start Menu shortcuts) app index in the
    /// background immediately, instead of waiting for it to start lazily on
    /// the user's first search. Call once, as early as possible in app
    /// startup. Safe to call more than once -- only the first call actually
    /// starts the work.
    /// </summary>
    public static void WarmUp() => new StartMenuService().GetAppsAsync();

    public static IReadOnlyList<LaunchableApp> GetSystemApps() =>
    [
        SystemApp("Settings", "ms-settings:", "preferences options system"),
        SystemApp("File Explorer", "explorer.exe", "files folders this pc"),
        SystemApp("Terminal", "wt.exe", "command prompt powershell console"),
        SystemApp("Task Manager", "taskmgr.exe", "processes performance startup"),
        SystemApp("Control Panel", "control.exe", "system settings legacy"),
        SystemApp("Windows Security", "windowsdefender:", "defender antivirus firewall"),
        Setting("Bluetooth & devices", "ms-settings:bluetooth", "bluetooth printers mouse devices"),
        Setting("Display settings", "ms-settings:display", "monitor brightness resolution scale night light"),
        Setting("Sound settings", "ms-settings:sound", "audio volume microphone speakers"),
        Setting("Network & internet", "ms-settings:network", "wifi ethernet vpn hotspot"),
        Setting("Personalization", "ms-settings:personalization", "background colors themes lock screen taskbar"),
        Setting("Installed apps", "ms-settings:appsfeatures", "applications uninstall defaults optional features"),
        Setting("Accounts", "ms-settings:accounts", "user email sign in family backup"),
        Setting("Time & language", "ms-settings:dateandtime", "date time region language typing"),
        Setting("Gaming", "ms-settings:gaming-gamebar", "game bar captures game mode"),
        Setting("Accessibility", "ms-settings:easeofaccess", "vision hearing captions narrator magnifier"),
        Setting("Privacy & security", "ms-settings:privacy", "permissions security privacy"),
        Setting("Windows Update", "ms-settings:windowsupdate", "updates history restart advanced")
    ];

    public Task<IReadOnlyList<LaunchableApp>> GetAppsAsync()
    {
        if (_cacheTask is not null) return _cacheTask;
        lock (CacheLock)
        {
            return _cacheTask ??= Task.Run(LoadApps);
        }
    }

    public async Task<IReadOnlyList<LaunchableApp>> SearchAppsAsync(
        string query,
        CancellationToken cancellationToken,
        int maxResults = 18)
    {
        query = query.Trim();
        var apps = await GetAppsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Length == 0)
            return apps.OrderByDescending(app => app.IsSystemApp).ThenBy(app => app.Name).Take(maxResults).ToList();

        return apps
            .Select(app => (App: app, Score: MatchScore(app, query)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.App.Name)
            .Take(maxResults)
            .Select(match => match.App)
            .ToList();
    }

    public async Task<IReadOnlyList<LaunchableApp>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        var appMatches = await SearchAppsAsync(query, cancellationToken);

        var fileMatches = query.Length < 2
            ? []
            : await Task.Run(() => SearchIndexedFiles(query, cancellationToken), cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var results = appMatches.Concat(fileMatches).Take(47).ToList();
        results.Add(new LaunchableApp
        {
            Name = $"Search the web for \"{query}\"",
            Path = $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}",
            Subtitle = "Open in your default browser",
            Kind = "Web",
            SearchTerms = query,
            Icon = ShellIconService.GetIcon("msedge.exe")
        });
        return results;
    }

    public void Launch(LaunchableApp app)
    {
        if (app.Path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{app.Path}\"") { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
    }

    private static IReadOnlyList<LaunchableApp> LoadApps()
    {
        var appsFolder = LoadAppsFolder();
        var settingsIcon = appsFolder.FirstOrDefault(app =>
            app.Name.Equals("Settings", StringComparison.OrdinalIgnoreCase))?.Icon;
        var apps = GetSystemApps()
            .Select(app => app.Kind == "Setting" && settingsIcon is not null ? WithIcon(app, settingsIcon) : app)
            .ToList();
        apps.AddRange(appsFolder);

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
                    apps.Add(new LaunchableApp
                    {
                        Name = name!,
                        Path = path,
                        Subtitle = "App",
                        Kind = "App",
                        SearchTerms = path,
                        Icon = ShellIconService.GetIcon(path)
                    });
                }
            }
            catch { }
        }

        return apps
            .GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MergeDuplicateApps)
            .OrderByDescending(app => app.IsSystemApp)
            .ThenBy(app => app.Name)
            .ToList();
    }

    private static IReadOnlyList<LaunchableApp> LoadAppsFolder()
    {
        var apps = new List<LaunchableApp>();
        object? shellObject = null;
        object? folderObject = null;
        object? itemsObject = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return apps;
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return apps;

            dynamic shell = shellObject;
            folderObject = shell.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
            if (folderObject is null) return apps;
            dynamic folder = folderObject;
            itemsObject = folder.Items();
            if (itemsObject is null) return apps;

            foreach (var itemObject in (System.Collections.IEnumerable)itemsObject)
            {
                try
                {
                    dynamic item = itemObject;
                    var name = Convert.ToString(item.Name)?.Trim();
                    var appId = Convert.ToString(item.Path)?.Trim();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId)) continue;
                    var target = $"shell:AppsFolder\\{appId}";
                    apps.Add(new LaunchableApp
                    {
                        Name = name!,
                        Path = target,
                        Subtitle = "App",
                        Kind = "App",
                        SearchTerms = appId!,
                        Icon = GetAppsFolderIcon(target) ?? ShellIconService.GetIcon(target)
                    });
                }
                catch { }
                finally { ReleaseCom(itemObject); }
            }
        }
        catch { }
        finally
        {
            ReleaseCom(itemsObject);
            ReleaseCom(folderObject);
            ReleaseCom(shellObject);
        }

        return apps;
    }

    private static IReadOnlyList<LaunchableApp> SearchIndexedFiles(string query, CancellationToken cancellationToken)
    {
        var results = new List<LaunchableApp>();
        object? connectionObject = null;
        object? recordsetObject = null;

        try
        {
            var terms = Regex.Matches(query, @"[\p{L}\p{N}]+")
                .Select(match => match.Value)
                .Where(term => term.Length > 0)
                .Take(8)
                .ToArray();
            if (terms.Length == 0) return results;

            var nameQuery = string.Join(" AND ", terms.Select(term => $"System.ItemName LIKE '%{EscapeSql(term)}%'"));
            var freeText = EscapeSql(query);
            var userScope = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                .Replace('\\', '/').TrimEnd('/') + "/";
            var sql = $"SELECT TOP 100 System.ItemName, System.ItemPathDisplay, System.ItemTypeText, " +
                      $"System.Search.Rank FROM SystemIndex WHERE " +
                      $"(({nameQuery}) OR FREETEXT(*, '{freeText}')) " +
                      $"AND SCOPE='file:{EscapeSql(userScope)}' ORDER BY System.Search.Rank DESC";

            var connectionType = Type.GetTypeFromProgID("ADODB.Connection");
            if (connectionType is null) return results;
            connectionObject = Activator.CreateInstance(connectionType);
            if (connectionObject is null) return results;
            dynamic connection = connectionObject;
            connection.Open("Provider=Search.CollatorDSO.1;Extended Properties='Application=Windows';");
            recordsetObject = connection.Execute(sql);
            dynamic recordset = recordsetObject;

            while (!(bool)recordset.EOF && results.Count < 28)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Convert.ToString(recordset.Fields.Item("System.ItemName").Value)?.Trim();
                var path = Convert.ToString(recordset.Fields.Item("System.ItemPathDisplay").Value)?.Trim();
                var itemType = Convert.ToString(recordset.Fields.Item("System.ItemTypeText").Value)?.Trim();
                recordset.MoveNext();

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) ||
                    !IsUsefulPath(path) || (!File.Exists(path) && !Directory.Exists(path))) continue;

                var isFolder = Directory.Exists(path);
                results.Add(new LaunchableApp
                {
                    Name = name!,
                    Path = path!,
                    Subtitle = isFolder ? path! : $"{itemType ?? "File"}  \u00B7  {Path.GetDirectoryName(path)}",
                    Kind = isFolder ? "Folder" : "File",
                    SearchTerms = path!,
                    Icon = ShellIconService.GetIcon(path)
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        finally
        {
            try
            {
                if (recordsetObject is not null) ((dynamic)recordsetObject).Close();
                if (connectionObject is not null) ((dynamic)connectionObject).Close();
            }
            catch { }
            ReleaseCom(recordsetObject);
            ReleaseCom(connectionObject);
        }

        return results.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
    }

    private static int MatchScore(LaunchableApp app, string query)
    {
        if (app.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (app.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 850;
        if (app.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(query, StringComparison.OrdinalIgnoreCase))) return 700;
        if (app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 600;
        if (app.SearchTerms.Contains(query, StringComparison.OrdinalIgnoreCase)) return 400;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var searchable = $"{app.Name} {app.SearchTerms}";
        return terms.Length > 1 && terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase)) ? 300 : 0;
    }

    private static bool IsUsefulPath(string path)
    {
        var blockedSegments = new[] { "\\AppData\\", "\\.git\\", "\\.nuget\\", "\\node_modules\\", "\\bin\\", "\\obj\\" };
        return !blockedSegments.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static ImageSource? GetAppsFolderIcon(string target)
    {
        IShellItemImageFactory? factory = null;
        nint bitmap = 0;

        try
        {
            var interfaceId = typeof(IShellItemImageFactory).GUID;
            var result = SHCreateItemFromParsingName(target, 0, ref interfaceId, out factory);
            if (result != 0 || factory is null) return null;

            const uint iconOnly = 0x00000004;
            const uint scaleUp = 0x00000100;
            result = factory.GetImage(new NativeSize(32, 32), iconOnly | scaleUp, out bitmap);
            if (result != 0 || bitmap == 0) return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            ReleaseCom(factory);
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private static LaunchableApp MergeDuplicateApps(IGrouping<string, LaunchableApp> group)
    {
        var preferred = group.OrderByDescending(app => app.IsSystemApp).First();
        var icon = group.Select(app => app.Icon).FirstOrDefault(candidate => candidate is not null);
        return preferred.Icon is null && icon is not null ? WithIcon(preferred, icon) : preferred;
    }

    private static LaunchableApp WithIcon(LaunchableApp app, ImageSource icon) => new()
    {
        Name = app.Name,
        Path = app.Path,
        Subtitle = app.Subtitle,
        Kind = app.Kind,
        SearchTerms = app.SearchTerms,
        Icon = icon,
        IsSystemApp = app.IsSystemApp
    };

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(int Width, int Height);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, uint flags, out nint bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        nint bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    private static LaunchableApp SystemApp(string name, string path, string searchTerms) => new()
    {
        Name = name,
        Path = path,
        Subtitle = "App",
        Kind = "App",
        SearchTerms = searchTerms,
        Icon = ShellIconService.GetIcon(path),
        IsSystemApp = true
    };

    private static LaunchableApp Setting(string name, string path, string searchTerms) => new()
    {
        Name = name,
        Path = path,
        Subtitle = "System settings",
        Kind = "Setting",
        SearchTerms = searchTerms,
        Icon = ShellIconService.GetIcon("SystemSettings.exe"),
        IsSystemApp = true
    };
}
