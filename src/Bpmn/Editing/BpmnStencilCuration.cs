using System.Collections.Frozen;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// A host's adjustments to one stencil entry.
/// </summary>
/// <remarks>
/// Every member is optional and means "leave this as it is" when unset, so a host states only what
/// it wants changed rather than restating the whole palette to move one item.
/// </remarks>
/// <param name="IsVisible">Whether to offer it at all.</param>
/// <param name="Label">A different name for it.</param>
/// <param name="Description">A tooltip, for a palette whose labels are terse.</param>
/// <param name="IconId">A different glyph, named as the shared icon manifest names it.</param>
/// <param name="Order">Where it sits among its group's entries; unset keeps the catalog order.</param>
public sealed record BpmnStencilEntryOptions(
    bool IsVisible = true,
    string? Label = null,
    string? Description = null,
    string? IconId = null,
    int? Order = null);

/// <summary>
/// How a host curates the palette (specification sections 9.1 to 9.3).
/// </summary>
/// <remarks>
/// <para>
/// Not part of <c>EditorConfig</c>, deliberately. That contract is the canvas's, and the canvas
/// never renders the stencil — sending curation across it would ship bytes nothing reads, and worse,
/// every relabelled tooltip would advance the configuration revision and make the paper re-apply a
/// configuration that had not changed. A palette is chrome; it should not be able to disturb the
/// drawing.
/// </para>
/// <para>
/// Curation subtracts and renames. It cannot add an entry, and it cannot enable one the rules
/// disabled: section 9.3 makes availability a consequence of the container and the connection rules,
/// so a host that could switch a tool back on would be able to configure its way to an invalid
/// document. Hiding a tool the rules allow is fine — that is a product decision — and the reverse
/// never is.
/// </para>
/// </remarks>
/// <param name="Entries">Adjustments by entry key, as <see cref="BpmnStencilEntry.Key"/> spells it.</param>
/// <param name="GroupOrder">Group titles in the order they should appear; any not named keep their place after.</param>
/// <param name="HiddenGroups">Group titles to leave out entirely.</param>
public sealed record BpmnStencilOptions(
    IReadOnlyDictionary<string, BpmnStencilEntryOptions>? Entries = null,
    IReadOnlyList<string>? GroupOrder = null,
    IReadOnlyList<string>? HiddenGroups = null)
{
    /// <summary>No curation: the palette the rules produce.</summary>
    public static BpmnStencilOptions Default { get; } = new();

    /// <summary>Whether this asks for nothing, so the groups can be returned untouched.</summary>
    public bool IsEmpty => (Entries is null || Entries.Count == 0)
        && (GroupOrder is null || GroupOrder.Count == 0)
        && (HiddenGroups is null || HiddenGroups.Count == 0);
}

/// <summary>
/// Applies a host's curation to the palette the rules produced.
/// </summary>
/// <remarks>
/// After the rules, never instead of them. <see cref="BpmnStencil.GroupsFor(Model.BpmnDocument, string?, string?)"/>
/// answers what is possible in this container; this answers what the host wants offered out of that.
/// Running it second is what makes the section 9.3 guarantee hold whatever a host configures.
/// </remarks>
public static class BpmnStencilCuration
{
    /// <summary>Curates the groups.</summary>
    /// <param name="groups">The palette the rules produced.</param>
    /// <param name="options">What the host wants changed.</param>
    /// <returns>The curated palette; groups left empty by curation are dropped.</returns>
    public static IReadOnlyList<BpmnStencilGroup> Apply(
        IReadOnlyList<BpmnStencilGroup> groups,
        BpmnStencilOptions? options)
    {
        ArgumentNullException.ThrowIfNull(groups);

        if (options is null || options.IsEmpty) return groups;

        var entries = options.Entries ?? FrozenDictionary<string, BpmnStencilEntryOptions>.Empty;
        var hidden = (options.HiddenGroups ?? []).ToFrozenSet(StringComparer.Ordinal);

        var curated = new List<BpmnStencilGroup>();

        foreach (var group in groups)
        {
            if (hidden.Contains(group.Title)) continue;

            var kept = Curate(group.Entries, entries);

            // A group whose every tool was hidden is a heading with nothing under it.
            if (kept.Count == 0) continue;

            curated.Add(group with { Entries = kept });
        }

        return Order(curated, options.GroupOrder);
    }

    private static IReadOnlyList<BpmnStencilEntry> Curate(
        IReadOnlyList<BpmnStencilEntry> entries,
        IReadOnlyDictionary<string, BpmnStencilEntryOptions> overrides)
    {
        var kept = new List<(BpmnStencilEntry Entry, int Rank)>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            overrides.TryGetValue(entry.Key, out var adjusted);

            if (adjusted is { IsVisible: false }) continue;

            /*
             * An entry the host ordered sorts by that number; everything else sorts by its position
             * in the catalog, offset past every possible explicit rank. Ranking the unordered ones
             * by their bare index made them tie with an explicit `Order: 0` and the pin silently did
             * nothing — the sort is stable, so the catalog entry won.
             */
            var rank = adjusted?.Order ?? index + entries.Count;

            kept.Add((
                adjusted is null
                    ? entry
                    : entry with
                    {
                        Label = adjusted.Label ?? entry.Label,
                        HasCustomLabel = adjusted.Label is not null || entry.HasCustomLabel,
                        Description = adjusted.Description ?? entry.Description,
                        IconId = adjusted.IconId ?? entry.IconId,
                    },
                rank));
        }

        return [.. kept.OrderBy(pair => pair.Rank).Select(pair => pair.Entry)];
    }

    private static List<BpmnStencilGroup> Order(List<BpmnStencilGroup> groups, IReadOnlyList<string>? order)
    {
        if (order is null || order.Count == 0) return groups;

        var wanted = order
            .Select((title, index) => (title, index))
            .ToFrozenDictionary(pair => pair.title, pair => pair.index, StringComparer.Ordinal);

        // Named groups first in the order given; the rest follow in the order the rules produced.
        int RankOf(BpmnStencilGroup group, int index) =>
            wanted.TryGetValue(group.Title, out var at) ? at : order.Count + index;

        return [.. groups.Select((group, index) => (group, rank: RankOf(group, index)))
            .OrderBy(pair => pair.rank)
            .Select(pair => pair.group)];
    }
}
