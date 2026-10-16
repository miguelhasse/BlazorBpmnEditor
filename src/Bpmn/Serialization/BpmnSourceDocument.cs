using System.Collections.Frozen;
using System.Xml.Linq;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// The XML a document was read from, retained so a later export can preserve it.
/// </summary>
/// <remarks>
/// <para>
/// Specification section 10.6 asks for XML comments, processing instructions, document order and
/// unknown attributes to survive a round trip. No semantic model can carry those — by the time
/// content has been interpreted into records, the formatting decisions that produced it are gone.
/// The only honest way to preserve them is not to throw the original away, so a read keeps it and
/// the fidelity writer edits that tree instead of building a new one.
/// </para>
/// <para>
/// Treated as opaque everywhere outside this namespace. It rides along on
/// <see cref="Model.BpmnDocument.Source"/> and is excluded from interop serialization: the canvas
/// receives the semantic model, never XML.
/// </para>
/// <para>
/// Every hand-out is a deep copy. A document is immutable and exporting it twice must give the
/// same answer both times, which would not hold if the writer could scribble on the shared tree.
/// </para>
/// </remarks>
public sealed class BpmnSourceDocument
{
    private readonly XDocument _document;
    private readonly FrozenDictionary<string, string> _prefixes;

    private BpmnSourceDocument(
        XDocument document,
        FrozenDictionary<string, string> prefixes,
        IReadOnlyList<BpmnImportDiagnostic> importLosses)
    {
        _document = document;
        _prefixes = prefixes;
        ImportLosses = importLosses;
    }

    /// <summary>Content the reader could not represent and which canonical export would omit.</summary>
    internal IReadOnlyList<BpmnImportDiagnostic> ImportLosses { get; }

    /// <summary>The namespace prefixes the source document declared on its root, keyed by prefix.</summary>
    public IReadOnlyDictionary<string, string> Prefixes => _prefixes;

    /// <summary>
    /// The source's XML declaration verbatim, or <see langword="null"/> when it had none.
    /// </summary>
    /// <remarks>
    /// Kept as written rather than rebuilt. A document that declared <c>encoding="UTF-8"</c> should
    /// come back saying that and not <c>utf-8</c>, and a document that had no declaration at all
    /// should not gain one — both are changes a reviewer would see on a save that changed nothing.
    /// </remarks>
    internal string? Declaration { get; private init; }

    /// <summary>Captures a parsed document, taking a private copy.</summary>
    /// <param name="document">The document as parsed.</param>
    /// <returns>A retained, immutable copy.</returns>
    internal static BpmnSourceDocument Capture(XDocument document)
    {
        var copy = new XDocument(document);
        var root = copy.Root;

        var prefixes = root is null
            ? FrozenDictionary<string, string>.Empty
            : root.Attributes()
                .Where(attribute => attribute.IsNamespaceDeclaration)
                .ToFrozenDictionary(
                    attribute => attribute.Name.LocalName == "xmlns" ? string.Empty : attribute.Name.LocalName,
                    attribute => attribute.Value,
                    StringComparer.Ordinal);

        return new BpmnSourceDocument(copy, prefixes, [])
        {
            Declaration = document.Declaration?.ToString(),
        };
    }

    /// <summary>Returns a source snapshot carrying the losses found while reading it.</summary>
    internal BpmnSourceDocument WithImportLosses(IReadOnlyList<BpmnImportDiagnostic> losses) =>
        new(_document, _prefixes, losses)
        {
            Declaration = Declaration,
        };

    /// <summary>A fresh mutable copy for one write.</summary>
    internal XDocument Clone() => new(_document);

    /// <summary>
    /// Finds the prefix this document bound to a namespace, so an export can reuse it.
    /// </summary>
    /// <param name="namespaceUri">The namespace URI to look up.</param>
    /// <returns>The prefix, an empty string for the default namespace, or <see langword="null"/>.</returns>
    internal string? PrefixFor(string namespaceUri)
    {
        foreach (var (prefix, uri) in _prefixes)
        {
            if (string.Equals(uri, namespaceUri, StringComparison.Ordinal)) return prefix;
        }

        return null;
    }
}
