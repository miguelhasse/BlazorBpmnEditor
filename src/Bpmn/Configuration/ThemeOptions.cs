namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>Which palette the editor paints with.</summary>
public enum BpmnThemeMode
{
    /// <summary>Follow the operating system, which is what a host that says nothing gets.</summary>
    System,

    /// <summary>Light, whatever the system prefers.</summary>
    Light,

    /// <summary>Dark, whatever the system prefers.</summary>
    Dark,
}

/// <summary>How much contrast the palette carries.</summary>
public enum BpmnContrast
{
    /// <summary>The ordinary palette.</summary>
    Normal,

    /// <summary>
    /// Raised contrast: pure black on white, heavier strokes, no tinted fills.
    /// </summary>
    /// <remarks>
    /// A separate axis from the mode, because they answer different questions. Dark mode is a
    /// preference; high contrast is an accessibility need, and a user can want either, both or
    /// neither.
    /// </remarks>
    High,
}

/// <summary>
/// How the editor is coloured (implementation plan M6).
/// </summary>
/// <remarks>
/// <para>
/// Notation geometry is never themable — a Gateway is a diamond in every theme, and section 4's
/// measurements are not a matter of taste. What a host may change is colour and typography, and it
/// changes them by redefining the custom properties every shape, marker and label already reads.
/// </para>
/// <para>
/// <see cref="Variables"/> is the escape hatch, and it is deliberately a map of raw custom
/// properties rather than a fixed set of named slots. A host branding the canvas wants its own
/// accent and nothing else; enumerating every token as a parameter would be a long list that still
/// missed the one it wanted.
/// </para>
/// </remarks>
/// <param name="Mode">Which palette to use.</param>
/// <param name="Contrast">How much contrast it carries.</param>
/// <param name="Variables">
/// Custom properties to override, named as the stylesheet names them — with or without the leading
/// <c>--</c>. Applied last, so a host's value wins over the mode and the contrast.
/// </param>
public sealed record ThemeOptions(
    BpmnThemeMode Mode = BpmnThemeMode.System,
    BpmnContrast Contrast = BpmnContrast.Normal,
    IReadOnlyDictionary<string, string>? Variables = null)
{
    /// <summary>Follow the system, ordinary contrast, nothing overridden.</summary>
    public static ThemeOptions Default { get; } = new();

    /// <summary>The value for the root's <c>data-bpmn-theme</c>, or null to follow the system.</summary>
    /// <remarks>
    /// Absent rather than "system", so the stylesheet's own media query is what decides and there
    /// is no third state for it to have to agree with.
    /// </remarks>
    public string? ThemeAttribute => Mode switch
    {
        BpmnThemeMode.Light => "light",
        BpmnThemeMode.Dark => "dark",
        _ => null,
    };

    /// <summary>The value for the root's <c>data-bpmn-contrast</c>, or null for the ordinary palette.</summary>
    public string? ContrastAttribute => Contrast == BpmnContrast.High ? "high" : null;

    /// <summary>
    /// The overrides as an inline style, or <see langword="null"/> when there are none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inline because that is the one place a value beats every stylesheet rule without needing to
    /// out-specify them, which is what an override is for.
    /// </para>
    /// <para>
    /// The editor applies this to the canvas as well as to its own root. Specificity is not what
    /// decides a custom property on a descendant: the stylesheet declares the base tokens on
    /// <c>.bpmn-canvas</c> itself, and any declaration on an element beats a value inherited from
    /// an ancestor, however that value was set. Set only on the root, every override here reached
    /// the chrome and stopped at the diagram.
    /// </para>
    /// </remarks>
    public string? Style
    {
        get
        {
            if (Variables is not { Count: > 0 }) return null;

            return string.Concat(Variables.Select(pair =>
            {
                var name = pair.Key.StartsWith("--", StringComparison.Ordinal) ? pair.Key : $"--{pair.Key}";

                return $"{name}:{pair.Value};";
            }));
        }
    }
}
