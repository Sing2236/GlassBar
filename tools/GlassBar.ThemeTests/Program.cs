using System.IO;
using System.Threading;
using GlassBar.Models;
using GlassBar.Services;

// Offline regression tests for the theme system: parsing, BasedOn
// inheritance, $Variable$ substitution (including cycles), cycle detection in
// the inheritance chain, unknown-key tolerance, and the built-in Default.
//
// Run with:  dotnet run --project tools\GlassBar.ThemeTests
// Follows the same tiny assert-harness convention as GlassBar.FullscreenTests.

var root = Path.Combine(Path.GetTempPath(), "GlassBarThemeTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    WriteTheme(root, "Base", """
        [Theme]
        Name = Base
        Author = Base Author
        Version = 2.1

        [Variables]
        Accent = #FF0000

        [Bar]
        Background = #101010
        Border = #303030

        [Widgets]
        Order = news, clock
        Connection.Show = 1
        """);

    WriteTheme(root, "Midnight", """
        [Theme]
        Name = Midnight
        Author = Ethan
        Version = 1.0
        BasedOn = Base

        [Variables]
        Accent = $Primary$
        Primary = #00FF00

        [Bar]
        Opacity = 0.5
        Border = $Accent$

        [Widgets]
        system.Show = 0

        [Widget:Clock]
        Format = h:mm

        [Nonsense]
        Whatever = ignored
        """);

    WriteTheme(root, "CycA", """
        [Theme]
        Name = CycA
        BasedOn = CycB
        """);
    WriteTheme(root, "CycB", """
        [Theme]
        Name = CycB
        BasedOn = CycA
        """);

    WriteTheme(root, "VarCycle", """
        [Theme]
        Name = VarCycle
        [Variables]
        X = $Y$
        Y = $X$
        [Bar]
        Border = $X$
        """);

    WriteTheme(root, "Chained", """
        [Theme]
        Name = Chained
        [Variables]
        A = $B$
        B = $C$
        C = #123456
        [Bar]
        Border = $A$
        """);

    var service = new ThemeService(root);
    var settings = new BarSettings();

    // 1. Built-in Default maps exactly onto the shipped BarSettings defaults.
    var defaults = new BarSettings();
    var builtIn = service.Resolve("Default", out var defaultError);
    AssertTrue(builtIn is not null, $"The built-in Default must resolve. {defaultError}");
    AssertTrue(service.Apply(builtIn!, settings), "The first Apply of Default must report a change.");
    AssertTrue(settings.ActiveTheme == "Default", "Default sets ActiveTheme=Default.");
    AssertTrue(settings.BarHeight == defaults.BarHeight, "Default preserves BarHeight.");
    AssertTrue(settings.CornerRadius == defaults.CornerRadius, "Default preserves CornerRadius.");
    AssertTrue(Math.Abs(settings.Opacity - defaults.Opacity) < 1e-9, "Default preserves Opacity.");
    AssertTrue(settings.BackgroundStart == defaults.BackgroundStart && settings.BackgroundEnd == defaults.BackgroundEnd,
        "Default preserves the background colour.");
    AssertTrue(settings.Border == defaults.Border, "Default preserves the border colour.");
    AssertTrue(settings.Accent == defaults.Accent, "Default preserves the accent colour.");
    AssertTrue(settings.TopValueFontSize == defaults.TopValueFontSize, "Default preserves ValueSize.");
    AssertTrue(settings.TopLabelFontSize == defaults.TopLabelFontSize, "Default preserves LabelSize.");
    AssertTrue(settings.TopWidgetOrder is null, "Default uses the built-in widget order (null).");
    AssertTrue(settings.TopClockWidth == defaults.TopClockWidth, "Default preserves clock width.");
    AssertTrue(settings.TopClockFormat == defaults.TopClockFormat, "Default preserves clock format.");
    AssertTrue(settings.TopShowClock && settings.TopShowPerformance && settings.TopShowConnection &&
               settings.TopShowPower && settings.TopShowFocus && settings.TopShowNews,
        "Default shows every widget.");

    // 2. Re-applying an unchanged theme does no work at all.
    AssertFalse(service.Apply(builtIn!, settings), "Re-applying an unchanged theme must be a no-op.");

    // 3. BasedOn inheritance: the child overrides only what it declares, and its
    //    own [Variables] win for substitution.
    var midnight = service.Resolve("Midnight", out var midnightError);
    AssertTrue(midnight is not null, $"Midnight must resolve. {midnightError}");
    AssertTrue(service.Apply(midnight!, settings), "Applying Midnight must report a change.");
    AssertTrue(settings.BackgroundStart == "#101010", "Midnight inherits Background from Base.");
    AssertTrue(settings.Border == "#00FF00", "The child's $Primary$ variable is used for its own key.");
    AssertTrue(Math.Abs(settings.Opacity - 0.5) < 1e-9, "Midnight overrides Opacity.");
    AssertTrue(settings.TopWidgetOrder == "news,clock", "Midnight inherits Base's widget order.");
    AssertFalse(settings.TopShowPerformance, "system.Show = 0 hides the performance tile (alias).");
    AssertTrue(settings.TopShowConnection, "Connection stays visible.");
    AssertTrue(settings.ActiveTheme == "Midnight", "Applying Midnight records the active theme.");
    AssertTrue(midnight!.Author == "Ethan", "Theme metadata is read from [Theme].");

    // 4. A BasedOn cycle is refused with an error instead of recursing forever.
    var cyclic = service.Resolve("CycA", out var cycleError);
    AssertTrue(cyclic is null, "A BasedOn cycle must not resolve.");
    AssertTrue(cycleError is not null && cycleError.Contains("circular", StringComparison.OrdinalIgnoreCase),
        "A BasedOn cycle reports a clear error.");

    // 5. A cyclic $Variable$ definition terminates (fixed-point cap).
    var varCycle = service.Resolve("VarCycle", out var varCycleError);
    AssertTrue(varCycle is not null, $"A cyclic variable must not hang or fail. {varCycleError}");

    // 6. A missing theme resolves to an error, never a crash.
    var missing = service.Resolve("No Such Theme", out var missingError);
    AssertTrue(missing is null && missingError is not null, "An unknown theme reports an error.");

    // 7. Unknown sections/keys are parsed harmlessly and never mapped.
    AssertTrue(midnight.Values.ContainsKey("nonsense/whatever"), "Unknown sections are tolerated.");

    // 8. Nested $Variables$ resolve to a fixed point.
    var chained = service.Resolve("Chained", out var chainedError);
    AssertTrue(chained is not null, $"Chained theme must resolve. {chainedError}");
    AssertTrue(chained!.Values["bar/border"] == "#123456", "Nested $Variables$ resolve to a fixed point.");

    // 9. An unresolvable $Variable$ is left literal rather than dropped.
    WriteTheme(root, "Literal", "[Bar]\nBorder = $Missing$\n");
    var literal = service.Resolve("Literal", out var literalError);
    AssertTrue(literal is not null, $"Literal theme must resolve. {literalError}");
    AssertTrue(literal!.Values["bar/border"] == "$Missing$", "An unknown reference stays literal.");

    // 10. Watcher: a burst of writes from one edit coalesces into exactly one
    //     debounced reload, and the reload sees the edited value.
    var reloads = 0;
    ResolvedTheme? reloaded = null;
    using (var done = new ManualResetEventSlim(false))
    {
        service.ActiveThemeReloaded += (theme, _) =>
        {
            Interlocked.Increment(ref reloads);
            reloaded = theme;
            done.Set();
        };
        var chainedPath = Path.Combine(root, "Chained", "theme.ini");
        service.WatchActive("Chained", chainedPath);
        for (var write = 0; write < 4; write++)
        {
            File.WriteAllText(chainedPath, "[Theme]\nName = Chained\n[Bar]\nBorder = #ABCDEF\nOpacity = 0.9\n");
            Thread.Sleep(30);
        }
        AssertTrue(done.Wait(TimeSpan.FromSeconds(5)), "The watcher must reload the active theme after an edit.");
        Thread.Sleep(700); // longer than the debounce window: catch any duplicate reload
    }
    AssertTrue(reloads == 1, $"One edit burst must coalesce into one reload; saw {reloads}.");
    AssertTrue(reloaded is not null && reloaded.Values["bar/border"] == "#ABCDEF",
        "The reloaded theme carries the edited value.");
    service.Dispose();

    Console.WriteLine("Theme system regression tests passed.");
    return 0;
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void WriteTheme(string root, string folder, string ini)
{
    var directory = Path.Combine(root, folder);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "theme.ini"), ini);
}

static void AssertTrue(bool condition, string message)
{
    if (condition) return;
    Console.Error.WriteLine($"FAILED: {message}");
    Environment.Exit(1);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
