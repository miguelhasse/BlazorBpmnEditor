using System.Collections.ObjectModel;

namespace Hasseware.AspNetCore.Components.Bpmn.Interop;

/// <summary>
/// Resource-backed display strings sent with the canvas configuration. Model names, rule codes
/// and diagnostic messages remain unchanged.
/// </summary>
public sealed record CanvasStrings
{
    /// <summary>Creates an immutable, ordinal snapshot of localized display text.</summary>
    /// <param name="values">Resource keys and their localized display text.</param>
    public CanvasStrings(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = new ReadOnlyDictionary<string, string>(
            values.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
    }

    private CanvasStrings(ReadOnlyDictionary<string, string> adopted) => Values = adopted;

    /// <summary>
    /// Wraps a dictionary the caller has just built and will not touch again, without the second copy the public constructor makes.
    /// </summary>
    internal static CanvasStrings Adopt(Dictionary<string, string> values) =>
        new(new ReadOnlyDictionary<string, string>(values));

    /// <summary>Resource keys and their localized display text.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>Compares display text, independent of dictionary identity and insertion order.</summary>
    public bool Equals(CanvasStrings? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && Values.Count == other.Values.Count
        && Values.All(entry => other.Values.TryGetValue(entry.Key, out var value)
            && StringComparer.Ordinal.Equals(entry.Value, value));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = Values.Count;
        foreach (var entry in Values)
        {
            hash ^= HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(entry.Key),
                StringComparer.Ordinal.GetHashCode(entry.Value));
        }

        return hash;
    }
}
