using System.Text.RegularExpressions;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

/// <summary>
/// The policy for which element-template icons are drawn. It mirrors <c>template-icon.ts</c> on the
/// canvas so the stencil, the property panel and the diagram agree on what is shown.
/// </summary>
/// <remarks>
/// Only inline raster or SVG data URIs are accepted. A remote URL would make rendering a diagram
/// fetch from wherever its author pointed, and an <c>&lt;img&gt;</c> source is the only place the
/// value is ever used, so a script-carrying SVG cannot execute.
/// </remarks>
public static partial class BpmnTemplateIcons
{
    /// <summary>Longest icon value accepted, in characters.</summary>
    public const int MaxLength = 64 * 1024;

    /// <summary>True when <paramref name="icon"/> is an inline image this library will draw.</summary>
    public static bool IsSafe(string? icon) =>
        icon is { Length: > 0 and <= MaxLength } && SafeDataUri().IsMatch(icon.Trim());

    /// <summary>The applied icon of an element, Camunda 8 first, or null when absent or refused.</summary>
    public static string? Of(BpmnElementExtras extras)
    {
        ArgumentNullException.ThrowIfNull(extras);
        foreach (var uri in (ReadOnlySpan<string>)[BpmnTemplateMarkers.ZeebeNamespaceUri, BpmnTemplateMarkers.CamundaNamespaceUri])
        {
            if (extras.ForeignAttributes.TryGetValue($"{{{uri}}}{BpmnTemplateMarkers.ModelerTemplateIcon}", out var icon) && IsSafe(icon))
                return icon.Trim();
        }
        return null;
    }

    [GeneratedRegex(@"^data:image/(png|jpeg|gif|webp|svg\+xml)[;,]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SafeDataUri();
}
