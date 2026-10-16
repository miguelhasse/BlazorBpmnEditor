using Hasseware.AspNetCore.Components.Bpmn.Editing;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>An entry with guarded write callbacks and the refused-edit reset revision.</summary>
/// <param name="Entry">The property metadata.</param>
/// <param name="OnValueChanged">Submits a scalar value.</param>
/// <param name="OnRowsChanged">Submits the entire collection.</param>
/// <param name="IsReadOnly">Whether edits are blocked.</param>
/// <param name="Revision">Revision used to rebuild controls after a refused edit.</param>
public sealed record BpmnPropertyEntryContext(BpmnPropertyEntry Entry, EventCallback<string?> OnValueChanged,
    EventCallback<IReadOnlyList<IReadOnlyDictionary<string, string?>>> OnRowsChanged, bool IsReadOnly, int Revision)
{
    /// <summary>The selected element, or null for a multiple selection.</summary>
    public string? ElementId { get; init; }
    /// <summary>The element id linking an editor control to its rendered error text.</summary>
    public string? ErrorId { get; init; }
    /// <summary>Variable hints available to expression editors.</summary>
    public IReadOnlyList<BpmnFeelVariable> Variables { get; init; } = [];
}

/// <summary>A group with its visible entries and the resolved entry renderer.</summary>
/// <param name="Group">Group metadata.</param>
/// <param name="Entries">Visible entries.</param>
/// <param name="EntryTemplate">Renderer; callbacks are supplied by the panel for the given entry.</param>
/// <param name="IsReadOnly">Whether the panel is inspecting.</param>
public sealed record BpmnPropertyGroupContext(BpmnPropertyGroup Group, IReadOnlyList<BpmnPropertyEntry> Entries,
    RenderFragment<BpmnPropertyEntryContext> EntryTemplate, bool IsReadOnly);

/// <summary>A bound edit routed to the host rather than a legacy property command.</summary>
/// <param name="ElementId">The selected element.</param>
/// <param name="Entry">Entry including its binding token.</param>
/// <param name="Value">Scalar value, or null for a collection edit.</param>
/// <param name="Rows">The replacement collection, or null for a scalar edit.</param>
public sealed record BpmnPropertyEntryEdit(string ElementId, BpmnPropertyEntry Entry, string? Value,
    IReadOnlyList<IReadOnlyDictionary<string, string?>>? Rows);
