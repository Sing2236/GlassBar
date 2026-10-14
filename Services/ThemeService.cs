using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media;
using GlassBar.Models;

namespace GlassBar.Services;

/// <summary>
/// Discovers file-defined themes, resolves their <c>BasedOn</c> chain and
/// <c>$Variable$</c> references, and maps the result onto
/// <see cref="BarSettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// The theme model is inspired by Rainmeter's skin architecture but is an
/// independent implementation: no Rainmeter source, headers, skins or
/// documentation were used. See the repository NOTICE.
/// </para>
/// <para>
/// Cost model: a theme's INI is parsed exactly once when it is resolved —
/// on an explicit selection, or on a debounced watcher event. It is never
/// read, parsed or mapped from a timer, a render path or a per-tick refresh,
/// and the resolved result is cached in <see cref="_appliedSignature"/> so
/// that re-applying an unchanged theme does no work at all.
/// </para>
/// </remarks>
public sealed class ThemeService : IDisposable
{
    public const string DefaultThemeName = "Default";

    /// <summary>Every theme folder holds exactly this file.</summary>
    public const string ThemeFileName = "theme.ini";

    // A cyclic $Variable$ definition (A = $B$, B = $A$) is resolved by running
    // substitution this many times and then stopping. The cap is what makes a
    // cycle terminate; it is not a depth limit for legitimate nesting.
    private const int MaxVariablePasses = 8;

    // A single INI save fires several watcher events. Coalescing them behind a
    // trailing-edge timer means one edit produces exactly one re-resolve.
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    // The order that reproduces the built-in widget bar, left to right.
    private static readonly string[] BuiltInWidgetOrder =
        ["clock", "performance", "connection", "power", "focus", "news"];

    private static readonly string BuiltInWidgetOrderValue = string.Join(',', BuiltInWidgetOrder);

    // Accepted spellings for each canonical widget, so a theme can say
    // "system.Show" or "performance.Show" and mean the same tile.
    private static readonly (string Canonical, string[] Spellings)[] WidgetSpellings =
    [
        ("clock", ["clock"]),
        ("performance", ["performance", "system"]),
        ("connection", ["connection"]),
        ("power", ["power"]),
        ("focus", ["focus"]),
        ("news", ["news"])
    ];

    private readonly Action<Action>? _post;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private bool _disposed;
    private string? _watchedThemeName;
    private string? _watchedThemePath;
    private string? _lastChangedPath;

    // Signature of the values last written into BarSettings by Apply. When a
    // re-resolved theme produces the same signature, Apply is a no-op.
    private string? _appliedSignature;

    /// <param name="themesRoot">
    /// Overrides the themes folder; used by the offline tests. Defaults to
    /// <c>%LOCALAPPDATA%\GlassBar\Themes</c>.
    /// </param>
    /// <param name="post">
    /// Marshals a callback onto the UI thread. Null (tests) runs inline.
    /// </param>
    public ThemeService(string? themesRoot = null, Action<Action>? post = null)
    {
        ThemesRoot = themesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBar", "Themes");
        _post = post;
    }

    /// <summary>Folder that holds one subfolder per theme.</summary>
    public string ThemesRoot { get; }

    /// <summary>Last fallback/parse message to surface to the user, if any.</summary>
    public string? StatusMessage { get; private set; }

    /// <summary>Raised (on the UI thread) after a debounced change under <see cref="ThemesRoot"/>.</summary>
    public event EventHandler? ThemesFolderChanged;

    /// <summary>
    /// Raised (on the UI thread) after the active theme's INI changed. A null
    /// theme means it no longer resolves; <c>error</c> then explains why.
    /// </summary>
    public event Action<ResolvedTheme?, string?>? ActiveThemeReloaded;

    /// <summary>Records a message for the settings panel to display.</summary>
    public void SetStatus(string? message) => StatusMessage = message;

    /// <summary>
    /// Lists every theme found under <see cref="ThemesRoot"/>, plus the
    /// built-in <c>Default</c> (always first). Never throws: an unreadable or
    /// malformed theme.ini is skipped and recorded in <see cref="StatusMessage"/>.
    /// </summary>
    public IReadOnlyList<Theme> Discover()
    {
        var found = new List<Theme>();
        string[] folders;
        try
        {
            folders = Directory.Exists(ThemesRoot) ? Directory.GetDirectories(ThemesRoot) : [];
        }
        catch
        {
            folders = [];
        }

        foreach (var folder in folders)
        {
            var path = Path.Combine(folder, ThemeFileName);
            if (!File.Exists(path)) continue;
            var theme = TryParseFile(path);
            if (theme is not null && !theme.Name.Equals(DefaultThemeName, StringComparison.OrdinalIgnoreCase))
                found.Add(theme);
        }

        found.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        // Default is always listed first, so "get back to Default" is the top row.
        found.Insert(0, BuiltInDefaultTheme());
        return found;
    }

    /// <summary>
    /// Resolves one theme by name (the built-in <c>Default</c> included) into
    /// its inherited, variable-substituted values. Returns null and sets
    /// <paramref name="error"/> on a missing theme, a broken inheritance chain
    /// or a <c>BasedOn</c> cycle.
    /// </summary>
    public ResolvedTheme? Resolve(string name, out string? error)
    {
        error = null;
        var target = FindTheme(name);
        if (target is null)
        {
            error = $"Theme '{name}' was not found.";
            return null;
        }

        // Walk BasedOn from the child up to the root, with cycle detection.
        var chain = new List<Theme>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = target;
        while (true)
        {
            if (!visited.Add(current.Name))
            {
                error = $"Theme '{name}' has a circular BasedOn chain: {string.Join(" → ", visited)} → {current.Name}.";
                return null;
            }

            chain.Add(current);
            if (string.IsNullOrWhiteSpace(current.BasedOn)) break;

            var parent = FindTheme(current.BasedOn);
            if (parent is null)
            {
                error = $"Theme '{current.Name}' is based on '{current.BasedOn}', which was not found.";
                return null;
            }

            current = parent;
        }

        // Merge root → child so a child overrides only the keys it declares.
        // The built-in Default is always the implicit base, which keeps every
        // resolved theme a complete description of the look.
        var values = new Dictionary<string, string>(BuiltInDefaultTheme().Values, StringComparer.OrdinalIgnoreCase);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            // Alias spellings ("system.Show", "[Widget:System]") collapse onto
            // the canonical widget key here, so a child's declaration overrides
            // an inherited one instead of being shadowed by a different spelling.
            foreach (var pair in chain[index].Values) values[CanonicalKey(pair.Key)] = pair.Value;
            foreach (var pair in chain[index].Variables) variables[pair.Key] = pair.Value;
        }

        var (resolvedValues, resolvedVariables) = Substitute(values, variables);
        return new ResolvedTheme
        {
            Name = target.Name,
            Author = target.Author,
            Version = target.Version,
            BasedOn = target.BasedOn,
            SourcePath = target.SourcePath,
            Values = resolvedValues,
            Variables = resolvedVariables
        };
    }

    /// <summary>
    /// Writes a resolved theme's mapped values into <paramref name="settings"/>.
    /// Returns false, without touching anything, when the theme maps to exactly
    /// the values already applied — the guard that stops a watcher storm, or a
    /// settings save, from ever causing repeated redraw work.
    /// </summary>
    public bool Apply(ResolvedTheme theme, BarSettings settings)
    {
        var height = Number(theme, "bar/height", 68, 40, 200);
        var corner = Number(theme, "bar/cornerradius", 22, 0, 60);
        var opacity = Number(theme, "bar/opacity", 0.82, 0, 1);
        var background = ColorText(theme, "bar/background", "#111722");
        var border = ColorText(theme, "bar/border", "#FFFFFF");
        var accent = VariableColor(theme, "accent", "#7DD3FC");
        var valueSize = Number(theme, "typography/valuesize", 12, 6, 48);
        var labelSize = Number(theme, "typography/labelsize", 9, 6, 48);
        var order = WidgetOrder(theme);
        var clockWidth = Number(theme, "widget:clock/width", 92, 40, 400);
        var clockFormat = ClockFormat(theme);

        var signature = new StringBuilder(theme.Name)
            .Append('|').Append(height.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(corner.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(opacity.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(background)
            .Append('|').Append(border)
            .Append('|').Append(accent)
            .Append('|').Append(valueSize.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(labelSize.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(order ?? BuiltInWidgetOrderValue)
            .Append('|').Append(clockWidth.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(clockFormat);
        foreach (var (canonical, spellings) in WidgetSpellings)
            signature.Append('|').Append(canonical).Append('=').Append(WidgetShown(theme, spellings));

        var computed = signature.ToString();
        if (computed == _appliedSignature) return false;
        _appliedSignature = computed;

        settings.BarHeight = height;
        settings.CornerRadius = corner;
        settings.Opacity = opacity;
        settings.BackgroundStart = background;
        settings.BackgroundEnd = background;
        settings.Border = border;
        settings.Accent = accent;
        settings.TopValueFontSize = valueSize;
        settings.TopLabelFontSize = labelSize;
        settings.TopWidgetOrder = order;
        settings.TopClockWidth = clockWidth;
        settings.TopClockFormat = clockFormat;
        foreach (var (canonical, spellings) in WidgetSpellings)
            SetWidgetShown(settings, canonical, WidgetShown(theme, spellings));
        settings.ActiveTheme = theme.Name;
        return true;
    }

    /// <summary>Starts (once) the watcher that drives live reload.</summary>
    /// <param name="themeName">The active theme; the built-in Default needs no watcher.</param>
    /// <param name="sourcePath">The active theme.ini, when known, so a change to a different
    /// theme does not needlessly re-resolve this one.</param>
    public void WatchActive(string? themeName, string? sourcePath = null)
    {
        _watchedThemeName = string.IsNullOrWhiteSpace(themeName) ||
                            themeName.Equals(DefaultThemeName, StringComparison.OrdinalIgnoreCase)
            ? null
            : themeName;
        _watchedThemePath = _watchedThemeName is null ? null : sourcePath;
        if (_watchedThemeName is not null) EnsureWatcher();
    }

    /// <summary>
    /// Creates the themes watcher if the folder exists. Cheap and idempotent;
    /// event-driven, so it costs nothing while the folder is untouched.
    /// </summary>
    public void EnsureWatcher()
    {
        if (_disposed) return;
        lock (_gate)
        {
            if (_watcher is not null || !Directory.Exists(ThemesRoot)) return;
            try
            {
                var watcher = new FileSystemWatcher(ThemesRoot, ThemeFileName)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                watcher.Changed += OnWatchedThemeFileChanged;
                watcher.Created += OnWatchedThemeFileChanged;
                watcher.Deleted += OnWatchedThemeFileChanged;
                watcher.Renamed += OnWatchedThemeFileRenamed;
                _debounce ??= new System.Threading.Timer(OnDebounceElapsed, null,
                    Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch
            {
                _watcher = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            if (_watcher is not null)
            {
                try { _watcher.EnableRaisingEvents = false; } catch { }
                _watcher.Changed -= OnWatchedThemeFileChanged;
                _watcher.Created -= OnWatchedThemeFileChanged;
                _watcher.Deleted -= OnWatchedThemeFileChanged;
                _watcher.Renamed -= OnWatchedThemeFileRenamed;
                _watcher.Dispose();
                _watcher = null;
            }
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    // --- Watching ----------------------------------------------------------

    private void OnWatchedThemeFileChanged(object sender, FileSystemEventArgs e)
    {
        _lastChangedPath = e.FullPath;
        ScheduleDebounce();
    }

    private void OnWatchedThemeFileRenamed(object sender, RenamedEventArgs e)
    {
        _lastChangedPath = e.FullPath;
        ScheduleDebounce();
    }

    private void ScheduleDebounce()
    {
        if (_disposed) return;
        try
        {
            // Re-arming the one-shot timer on every event gives a trailing edge:
            // the burst a single save produces collapses into one callback.
            _debounce?.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed) return;
        var post = _post;
        if (post is null)
        {
            HandleDebounced();
            return;
        }

        try { post(HandleDebounced); }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private void HandleDebounced()
    {
        if (_disposed) return;
        ThemesFolderChanged?.Invoke(this, EventArgs.Empty);
        var name = _watchedThemeName;
        if (string.IsNullOrEmpty(name)) return;
        // Only the active theme's own INI needs a re-resolve. The path is unknown
        // for a theme picked up at startup, so that case still reloads.
        if (_watchedThemePath is not null && _lastChangedPath is not null &&
            !string.Equals(_watchedThemePath, _lastChangedPath, StringComparison.OrdinalIgnoreCase))
            return;
        var resolved = Resolve(name, out var error);
        ActiveThemeReloaded?.Invoke(resolved, error);
    }

    // --- Parsing -----------------------------------------------------------

    private Theme? FindTheme(string name)
    {
        if (name.Equals(DefaultThemeName, StringComparison.OrdinalIgnoreCase))
            return BuiltInDefaultTheme();

        foreach (var theme in Discover())
            if (theme.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return theme;
        return null;
    }

    private Theme? TryParseFile(string path)
    {
        try
        {
            return ParseTheme(Path.GetFileName(Path.GetDirectoryName(path)) ?? DefaultThemeName,
                File.ReadAllText(path), path);
        }
        catch
        {
            SetStatus($"Could not read {Path.GetFileName(path)}.");
            return null;
        }
    }

    /// <summary>
    /// Parses one INI. Unknown sections and keys are kept as ordinary values
    /// and simply never mapped, so a file written for a newer build degrades
    /// instead of failing.
    /// </summary>
    public static Theme ParseTheme(string fallbackName, string text, string? sourcePath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var name = fallbackName;
        var author = string.Empty;
        var version = string.Empty;
        string? basedOn = null;
        var section = string.Empty;

        foreach (var rawLine in text.Split('\n'))
        {
            // ';' starts a comment; inline too (the schema's BasedOn example uses
            // one). '#' is NOT a comment — it introduces a colour.
            var line = rawLine;
            var comment = line.IndexOf(';');
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length == 0) continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0) continue;

            if (section.Equals("Variables", StringComparison.OrdinalIgnoreCase))
            {
                variables[key] = value;
                continue;
            }

            if (section.Equals("Theme", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "name": if (value.Length > 0) name = value; break;
                    case "author": author = value; break;
                    case "version": version = value; break;
                    case "basedon": basedOn = value.Length > 0 ? value : null; break;
                }
                continue;
            }

            if (section.Length > 0) values[$"{section}/{key}".ToLowerInvariant()] = value;
        }

        return new Theme
        {
            Name = name,
            Author = author,
            Version = version,
            BasedOn = basedOn,
            SourcePath = sourcePath,
            Values = values,
            Variables = variables
        };
    }

    /// <summary>
    /// Expands every <c>$Name$</c> in values and variables to a fixed point.
    /// A cyclic definition simply stops changing and the loop exits at the cap.
    /// </summary>
    private static (Dictionary<string, string> Values, Dictionary<string, string> Variables) Substitute(
        Dictionary<string, string> values, Dictionary<string, string> variables)
    {
        for (var pass = 0; pass < MaxVariablePasses; pass++)
        {
            var changed = false;
            // Assigning an existing key does not invalidate the enumerator, so
            // no snapshot list (and no allocation) is needed here.
            foreach (var key in variables.Keys)
            {
                var next = Expand(variables[key], variables);
                if (next == variables[key]) continue;
                variables[key] = next;
                changed = true;
            }
            foreach (var key in values.Keys)
            {
                var next = Expand(values[key], variables);
                if (next == values[key]) continue;
                values[key] = next;
                changed = true;
            }
            if (!changed) break;
        }
        return (values, variables);
    }

    private static string Expand(string text, Dictionary<string, string> variables)
    {
        if (text.IndexOf('$') < 0) return text;
        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] != '$')
            {
                builder.Append(text[index++]);
                continue;
            }
            var close = text.IndexOf('$', index + 1);
            if (close < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }
            var name = text.Substring(index + 1, close - index - 1);
            if (name.Length > 0 && variables.TryGetValue(name, out var value))
            {
                builder.Append(value);
                index = close + 1;
            }
            else
            {
                // Unknown reference: keep the '$' literal and carry on.
                builder.Append('$');
                index++;
            }
        }
        return builder.ToString();
    }

    // --- Mapping helpers ---------------------------------------------------

    private static double Number(ResolvedTheme theme, string key, double fallback, double min, double max)
    {
        if (!theme.TryGet(key, out var text)) return fallback;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    private static string ColorText(ResolvedTheme theme, string key, string fallback)
        => theme.TryGet(key, out var text) && IsColor(text) ? text : fallback;

    private static string VariableColor(ResolvedTheme theme, string variable, string fallback)
        => theme.Variables.TryGetValue(variable, out var text) && IsColor(text) ? text : fallback;

    private static bool IsColor(string text)
    {
        try { return ColorConverter.ConvertFromString(text) is Color; }
        catch { return false; }
    }

    private static bool WidgetShown(ResolvedTheme theme, string[] spellings)
    {
        foreach (var spelling in spellings)
            if (theme.TryGet($"widgets/{spelling}.show", out var text) && TryBool(text, out var shown))
                return shown;
        return true;
    }

    private static void SetWidgetShown(BarSettings settings, string canonical, bool shown)
    {
        switch (canonical)
        {
            case "clock": settings.TopShowClock = shown; break;
            case "performance": settings.TopShowPerformance = shown; break;
            case "connection": settings.TopShowConnection = shown; break;
            case "power": settings.TopShowPower = shown; break;
            case "focus": settings.TopShowFocus = shown; break;
            case "news": settings.TopShowNews = shown; break;
        }
    }

    private static bool TryBool(string text, out bool value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on": value = true; return true;
            case "0": case "false": case "no": case "off": value = false; return true;
            default: value = false; return false;
        }
    }

    /// <summary>
    /// Normalizes <c>[Widgets] Order</c> into a canonical comma list. Unknown
    /// or repeated names are dropped; the built-in order normalizes to null so
    /// Default leaves the widget bar exactly as shipped.
    /// </summary>
    private static string? WidgetOrder(ResolvedTheme theme)
    {
        if (!theme.TryGet("widgets/order", out var text)) return null;
        var seen = new List<string>(BuiltInWidgetOrder.Length);
        foreach (var part in text.Split(','))
        {
            var canonical = CanonicalWidget(part.Trim());
            if (canonical is null || seen.Contains(canonical)) continue;
            seen.Add(canonical);
        }
        if (seen.Count == 0) return null;
        var joined = string.Join(',', seen);
        return joined.Equals(BuiltInWidgetOrderValue, StringComparison.OrdinalIgnoreCase) ? null : joined;
    }

    private static string? CanonicalWidget(string name)
    {
        foreach (var (canonical, spellings) in WidgetSpellings)
            foreach (var spelling in spellings)
                if (name.Equals(spelling, StringComparison.OrdinalIgnoreCase))
                    return canonical;
        return null;
    }

    /// <summary>Maps an alias widget key onto its canonical spelling; other keys are returned unchanged.</summary>
    private static string CanonicalKey(string key)
    {
        var slash = key.IndexOf('/');
        if (slash <= 0) return key;
        var section = key[..slash];
        var name = key[(slash + 1)..];
        if (section.Equals("widgets", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".show", StringComparison.OrdinalIgnoreCase))
        {
            var canonical = CanonicalWidget(name[..^5]);
            if (canonical is not null) return $"widgets/{canonical}.show";
        }
        if (section.StartsWith("widget:", StringComparison.OrdinalIgnoreCase))
        {
            var canonical = CanonicalWidget(section["widget:".Length..]);
            if (canonical is not null) return $"widget:{canonical}/{name}";
        }
        return key;
    }

    private static string ClockFormat(ResolvedTheme theme)
    {
        const string fallback = "h:mm";
        if (!theme.TryGet("widget:clock/format", out var text)) return fallback;
        if (text.Length is 0 or > 32) return fallback;
        try { _ = DateTime.Now.ToString(text, CultureInfo.InvariantCulture); return text; }
        catch (FormatException) { return fallback; }
    }

    // --- Built-in Default --------------------------------------------------

    /// <summary>The compiled-in theme a fresh install always falls back to.</summary>
    public static Theme BuiltInDefaultTheme() => ParseTheme(DefaultThemeName, DefaultIni, sourcePath: null);

    /// <summary>
    /// The built-in Default, written in the very same INI dialect a user theme
    /// uses. Its values are exactly <see cref="BarSettings"/>' own defaults, so
    /// applying Default reproduces the shipped look.
    /// </summary>
    private const string DefaultIni = """
        [Theme]
        Name = Default
        Author = GlassBar
        Version = 1.0

        [Variables]
        Accent = #7DD3FC

        [Bar]
        Height = 68
        CornerRadius = 22
        Opacity = 0.82
        Background = #111722
        Border = #FFFFFF

        [Typography]
        ValueSize = 12
        LabelSize = 9

        [Widgets]
        Order = clock, performance, connection, power, focus, news
        Clock.Show = 1
        Performance.Show = 1
        Connection.Show = 1
        Power.Show = 1
        Focus.Show = 1
        News.Show = 1

        [Widget:Clock]
        Width = 92
        Format = h:mm
        """;
}
