using System.Diagnostics.CodeAnalysis;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>Something in preserved content that must not be handed on (specification section 5.5).</summary>
/// <param name="ElementId">The element carrying it.</param>
/// <param name="Path">Where inside that element's extension content it sits.</param>
/// <param name="Reason">What is wrong with it, in words a reader can act on.</param>
public sealed record BpmnUnsafeContent(string ElementId, string Path, string Reason)
{
    internal BpmnDiagnosticText? LocalizableDetail { get; init; }

    /// <summary>Compares unsafe findings independently of presentation metadata.</summary>
    public bool Equals(BpmnUnsafeContent? other) => other is not null
        && ElementId == other.ElementId && Path == other.Path && Reason == other.Reason;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ElementId, Path, Reason);
}

/// <summary>
/// Finding active markup hidden in preserved extension content (implementation plan M7).
/// </summary>
/// <remarks>
/// <para>
/// Section 6.1 requires unknown vendor XML to round-trip untouched, and section 5.5 requires unsafe
/// external content to be quarantined. Both, at once: the content is still preserved exactly — it
/// is never rewritten or stripped, because an editor that silently edited what it did not
/// understand would be worse than one that refused — but a document carrying it is reported as
/// non-exportable rather than passed on as though it were ordinary.
/// </para>
/// <para>
/// What counts as unsafe is deliberately narrow, because a false positive here makes a legitimate
/// document unusable. A vendor's <c>script</c> element is not the danger: Zeebe, Camunda and BPMN
/// itself all have one, none of them is executable, and flagging by local name would quarantine
/// half the documents in the wild. The danger is markup a host might put into a page — content in
/// the HTML or SVG namespaces, which is exactly what a renderer would treat as live — and URLs
/// that execute wherever they are used.
/// </para>
/// </remarks>
public static class BpmnExtensionSafety
{
    /// <summary>Namespaces whose content a browser executes rather than merely displays.</summary>
    private static readonly string[] ActiveNamespaces =
    [
        "http://www.w3.org/1999/xhtml",
        "http://www.w3.org/2000/svg",
    ];

    /// <summary>Elements in those namespaces that run something or load something.</summary>
    private static readonly string[] ActiveElements =
    [
        "script", "iframe", "object", "embed", "foreignObject", "frame", "frameset", "applet",
    ];

    /// <summary>URL schemes that execute rather than fetch.</summary>
    private static readonly string[] ExecutableSchemes = ["javascript:", "vbscript:", "data:text/html"];

    /// <summary>Everything unsafe the document is carrying, in document order.</summary>
    /// <param name="document">The document to scan.</param>
    public static IReadOnlyList<BpmnUnsafeContent> Scan(BpmnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var found = new List<BpmnUnsafeContent>();

        foreach (var (elementId, extras) in BpmnValidator.Extended(document))
        {
            foreach (var extension in extras.Extensions)
            {
                Walk(elementId, extension, extension.LocalName, found);
            }

            foreach (var (name, value) in extras.ForeignAttributes)
            {
                if (ExecutableUrl(value) is { } scheme)
                {
                    found.Add(new BpmnUnsafeContent(elementId, name, $"the attribute value is a '{scheme}' URL")
                    {
                        LocalizableDetail = BpmnDiagnosticText.Create("EXT-010.Detail.Url",
                            "{0}: the attribute value is a '{1}' URL.", name, scheme),
                    });
                }
            }
        }

        return found;
    }

    private static void Walk(string elementId, BpmnExtensionElement element, string path, List<BpmnUnsafeContent> found)
    {
        var active = Array.Exists(ActiveNamespaces, ns => string.Equals(ns, element.Namespace, StringComparison.Ordinal));

        if (active && Array.Exists(ActiveElements, name => string.Equals(name, element.LocalName, StringComparison.Ordinal)))
        {
            found.Add(new BpmnUnsafeContent(
                elementId,
                path,
                $"<{element.LocalName}> in the {Short(element.Namespace)} namespace is active markup")
            {
                LocalizableDetail = BpmnDiagnosticText.Create("EXT-010.Detail.Markup",
                    "{0}: <{1}> in the {2} namespace is active markup.", path, element.LocalName, Short(element.Namespace)),
            });
        }

        foreach (var (name, value) in element.Attributes)
        {
            /*
             * Event handlers only where they would actually run. An attribute called `onboarding`
             * in a vendor's own namespace is a field name, not a handler, and quarantining a
             * document for having one would be exactly the false positive this is written to avoid.
             */
            if (active && IsEventHandler(name))
            {
                found.Add(new BpmnUnsafeContent(elementId, $"{path}/@{name}", $"'{name}' is an event handler")
                {
                    LocalizableDetail = BpmnDiagnosticText.Create("EXT-010.Detail.Handler",
                        "{0}/@{1}: '{1}' is an event handler.", path, name),
                });
            }

            if (ExecutableUrl(value) is { } scheme)
            {
                found.Add(new BpmnUnsafeContent(elementId, $"{path}/@{name}", $"the attribute value is a '{scheme}' URL")
                {
                    LocalizableDetail = BpmnDiagnosticText.Create("EXT-010.Detail.Url",
                        "{0}: the attribute value is a '{1}' URL.", $"{path}/@{name}", scheme),
                });
            }
        }

        foreach (var child in element.Children)
        {
            Walk(elementId, child, $"{path}/{child.LocalName}", found);
        }
    }

    /// <summary>Whether an attribute name is a DOM event handler.</summary>
    /// <remarks>
    /// Matched as "on" followed by letters only. Deliberately not a list of the known handlers:
    /// that list grows, and an unknown <c>on*</c> attribute inside HTML or SVG has no innocent
    /// reading anyway.
    /// </remarks>
    private static bool IsEventHandler(string name)
    {
        var local = name[(name.LastIndexOf('}') + 1)..];

        return local.Length > 2
            && local.StartsWith("on", StringComparison.OrdinalIgnoreCase)
            && local.All(char.IsAsciiLetter);
    }

    /// <summary>The executable scheme an attribute value uses, or null when it uses none.</summary>
    /// <remarks>
    /// Whitespace and control characters are stripped first, because <c>java\nscript:</c> is a URL
    /// a browser honours and a naive comparison does not.
    /// </remarks>
    private static string? ExecutableUrl(string value)
    {
        if (value.Length == 0) return null;

        Span<char> squeezed = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character) && !char.IsControl(character))
            {
                squeezed[length++] = char.ToLowerInvariant(character);
            }
        }

        var normalised = squeezed[..length];

        foreach (var scheme in ExecutableSchemes)
        {
            if (normalised.StartsWith(scheme, StringComparison.Ordinal)) return scheme;
        }

        return null;
    }

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "A namespace URI is compared and shown as written, and URIs are lowercase by convention.")]
    private static string Short(string uri) => uri.EndsWith("svg", StringComparison.OrdinalIgnoreCase) ? "SVG" : "HTML";
}
