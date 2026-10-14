namespace GlassBar.Models;

/// <summary>
/// One theme exactly as authored on disk (or the built-in Default): the
/// values it declares plus its own <c>[Variables]</c> table, before any
/// <c>BasedOn</c> inheritance or <c>$Variable$</c> substitution.
/// </summary>
/// <remarks>
/// This is the restyle model only. It deliberately has no concept of new
/// widget types, expressions, measures or meters: a theme can rearrange and
/// re-skin the widgets GlassBar already ships, and nothing more.
/// </remarks>
public sealed class Theme
{
    /// <summary>Display name from <c>[Theme] Name</c>, falling back to the folder name.</summary>
    public required string Name { get; init; }

    public string Author { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    /// <summary>The theme this one inherits from, or null for "inherit the built-in Default".</summary>
    public string? BasedOn { get; init; }

    /// <summary>Absolute path to the theme folder's theme.ini, or null for the built-in Default.</summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Declared values, keyed <c>"section/key"</c> in lower case (for example
    /// <c>"bar/height"</c> or <c>"widgets/clock.show"</c>). <c>[Theme]</c> and
    /// <c>[Variables]</c> are not stored here.
    /// </summary>
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Declared <c>$Variable$</c> definitions, before substitution.</summary>
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A theme after the <c>BasedOn</c> chain has been merged and every
/// <c>$Variable$</c> reference resolved to a fixed point. This is what
/// <see cref="Services.ThemeService.Apply"/> maps onto
/// <see cref="BarSettings"/>.
/// </summary>
public sealed class ResolvedTheme
{
    public required string Name { get; init; }

    public string Author { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string? BasedOn { get; init; }

    /// <summary>theme.ini this was read from, or null for the built-in Default.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Fully resolved values, keyed <c>"section/key"</c> in lower case.</summary>
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fully resolved <c>[Variables]</c> table, retained for diagnostics/tests.</summary>
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the theme folder itself no longer exists on disk (Default always resolves).</summary>
    public bool IsBuiltIn => SourcePath is null;

    public bool TryGet(string path, out string value) => Values.TryGetValue(path, out value!);
}
