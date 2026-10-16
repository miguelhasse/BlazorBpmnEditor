namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

/// <summary>
/// Foreign XML content attached to a BPMN element, preserved verbatim.
/// </summary>
/// <remarks>
/// <para>
/// Specification section 6.1 requires extensibility to be a first-class model layer rather than
/// dropped content or properties smuggled onto SVG nodes. This record keeps everything needed to
/// write the original XML back out unchanged: namespace, local name, attributes, ordered
/// children and text.
/// </para>
/// <para>
/// Child order is significant. BPMN's complex types are <c>xsd:sequence</c>, so re-emitting
/// retained children in a different order produces a document other modelling tools reject.
/// </para>
/// <para>
/// Nothing here is interpreted. A registered extension schema may later surface known fields as
/// typed property editors, but unknown content stays opaque and round-trips untouched.
/// </para>
/// <para>
/// Equality is structural, all the way down. The compiler-generated version would compare the
/// dictionary and list by reference, so two elements read from identical XML would count as
/// different — which is precisely the comparison a round-trip check needs to make.
/// </para>
/// </remarks>
/// <param name="Namespace">The namespace URI the element was declared in.</param>
/// <param name="LocalName">The element's local name.</param>
/// <param name="Attributes">Attributes, keyed by qualified name.</param>
/// <param name="Children">Child elements, in document order.</param>
/// <param name="Text">Element text content, when it has any.</param>
public sealed record BpmnExtensionElement(
    string Namespace,
    string LocalName,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<BpmnExtensionElement> Children,
    string? Text = null)
{
    /// <summary>Compares two preserved elements by their content.</summary>
    /// <param name="other">The element to compare with.</param>
    /// <returns><see langword="true"/> when both would serialize to the same XML.</returns>
    public bool Equals(BpmnExtensionElement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (!string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
            || !string.Equals(LocalName, other.LocalName, StringComparison.Ordinal)
            || !string.Equals(Text, other.Text, StringComparison.Ordinal)
            || Attributes.Count != other.Attributes.Count
            || Children.Count != other.Children.Count)
        {
            return false;
        }

        foreach (var (name, value) in Attributes)
        {
            if (!other.Attributes.TryGetValue(name, out var found)
                || !string.Equals(value, found, StringComparison.Ordinal))
            {
                return false;
            }
        }

        // Positional, not set-based: order is part of what makes the XML valid.
        return !Children.Where((child, index) => !child.Equals(other.Children[index])).Any();
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Namespace, StringComparer.Ordinal);
        hash.Add(LocalName, StringComparer.Ordinal);
        hash.Add(Text, StringComparer.Ordinal);
        hash.Add(Attributes.Count);
        hash.Add(Children.Count);

        return hash.ToHashCode();
    }
}
