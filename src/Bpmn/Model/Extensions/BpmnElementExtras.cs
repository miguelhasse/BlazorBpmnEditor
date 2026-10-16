using System.Collections.Frozen;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

/// <summary>
/// The content an element carries that the editor preserves without interpreting.
/// </summary>
/// <remarks>
/// <para>
/// One carrier on every element rather than three parameters on each, so the writer has a single
/// uniform thing to emit and adding a fourth kind of preserved content later touches one type.
/// Every element defaults it to <see cref="None"/>, which means the model stays exactly as easy to
/// construct by hand as it was before preservation existed.
/// </para>
/// <para>
/// Specification section 10.5 requires all three to survive import → edit → export: unknown vendor
/// XML, namespace-qualified attributes on otherwise-known elements, and human documentation. The
/// second is the one tools most often lose, because it hides on elements the reader thinks it
/// fully understands — <c>zeebe:modelerTemplate</c> on a plain <c>serviceTask</c>, for instance.
/// </para>
/// </remarks>
public sealed record BpmnElementExtras
{
    // Declared before None, which reads it: static fields initialise in declaration order, so the
    // other way round leaves every default-constructed instance with a null dictionary.
    private static readonly FrozenDictionary<string, string> NoAttributes =
        FrozenDictionary<string, string>.Empty;

    /// <summary>Nothing preserved: the shared instance every element defaults to.</summary>
    public static readonly BpmnElementExtras None = new();

    /// <summary>The element's <c>documentation</c> text, when it declares any.</summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// Attributes from a namespace this reader does not own, keyed by <c>{uri}localName</c>.
    /// </summary>
    /// <remarks>
    /// Keyed by namespace URI rather than by prefix, because a prefix is arbitrary and a document
    /// may rebind it (section 10.6). The writer re-declares whatever prefix it needs.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ForeignAttributes { get; init; } = NoAttributes;

    /// <summary>The element's <c>extensionElements</c> children, preserved verbatim.</summary>
    public IReadOnlyList<BpmnExtensionElement> Extensions { get; init; } = [];

    /// <summary>Compares preserved content structurally, including ordered extension children.</summary>
    /// <param name="other">The extras to compare with.</param>
    /// <returns>Whether both carriers contain the same documentation, attributes and extensions.</returns>
    public bool Equals(BpmnElementExtras? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (!string.Equals(Documentation, other.Documentation, StringComparison.Ordinal)
            || ForeignAttributes.Count != other.ForeignAttributes.Count
            || Extensions.Count != other.Extensions.Count)
        {
            return false;
        }

        foreach (var (name, value) in ForeignAttributes)
        {
            if (!other.ForeignAttributes.TryGetValue(name, out var found)
                || !string.Equals(value, found, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return Extensions.SequenceEqual(other.Extensions);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Documentation, StringComparer.Ordinal);
        foreach (var (name, value) in ForeignAttributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(name, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }
        foreach (var extension in Extensions) hash.Add(extension);
        return hash.ToHashCode();
    }

    /// <summary>True when the element carried none of this.</summary>
    public bool IsEmpty =>
        Documentation is null && ForeignAttributes.Count == 0 && Extensions.Count == 0;
}
