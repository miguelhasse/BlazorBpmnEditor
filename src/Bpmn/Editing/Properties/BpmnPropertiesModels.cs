using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>The editor used for a property entry.</summary>
public enum BpmnPropertyEntryKind
{
    /// <summary>Single-line text.</summary>
    Text,
    /// <summary>Multiline text.</summary>
    TextArea,
    /// <summary>A Boolean value.</summary>
    Checkbox,
    /// <summary>A numeric value.</summary>
    Number,
    /// <summary>A fixed set of choices.</summary>
    Select,
    /// <summary>A collection of rows.</summary>
    List,
    /// <summary>A value displayed without an editor.</summary>
    ReadOnly,
    /// <summary>An entry that is never rendered.</summary>
    Hidden,
}

/// <summary>Expression metadata for host-provided FEEL editors.</summary>
public enum BpmnFeelMode
{
    /// <summary>No expression support.</summary>
    None,
    /// <summary>An expression or a literal.</summary>
    Optional,
    /// <summary>An expression is required.</summary>
    Required,
    /// <summary>Only a literal is allowed.</summary>
    Static,
}

/// <summary>A named column in a collection entry.</summary>
/// <param name="Name">The row dictionary key.</param>
/// <param name="Label">The displayed column heading.</param>
public sealed record BpmnPropertyColumn(string Name, string Label)
{
    /// <summary>Expression metadata for this collection cell.</summary>
    public BpmnFeelMode Feel { get; init; }
    /// <summary>Expression syntax; None falls back to FEEL mode when enabled.</summary>
    public BpmnExpressionLanguage ExpressionLanguage { get; init; }
}

/// <summary>Validation metadata, interpreted by the binding or validation layer.</summary>
/// <param name="NotEmpty">Whether a value is required.</param>
/// <param name="MinLength">Minimum length.</param>
/// <param name="MaxLength">Maximum length.</param>
/// <param name="Pattern">A regular expression.</param>
/// <param name="PatternMessage">The message for a pattern mismatch.</param>
public sealed record BpmnPropertyConstraints(bool NotEmpty = false, int? MinLength = null,
    int? MaxLength = null, string? Pattern = null, string? PatternMessage = null);

/// <summary>An opaque routing token for the host's binding layer.</summary>
/// <param name="Type">The binding handler identifier.</param>
/// <param name="Parameters">Handler-specific parameters.</param>
public sealed record BpmnPropertyBinding(string Type, IReadOnlyDictionary<string, string> Parameters);

/// <summary>A snapshot-derived property and its editor metadata.</summary>
/// <param name="Id">Stable id; a legacy descriptor uses its property name.</param>
/// <param name="Label">Displayed label.</param>
/// <param name="Kind">Editor kind.</param>
/// <param name="Value">String value, or null when absent.</param>
public sealed record BpmnPropertyEntry(string Id, string Label, BpmnPropertyEntryKind Kind, string? Value = null)
{
    /// <summary>The choices for a select editor.</summary>
    public IReadOnlyList<BpmnPropertyChoice> Choices { get; init; } = [];
    /// <summary>The columns for a list editor.</summary>
    public IReadOnlyList<BpmnPropertyColumn>? Columns { get; init; }
    /// <summary>The authoritative rows for a list editor. Extension collection rows carry reserved
    /// <c>$index</c> metadata identifying their original item; editors preserve it when moving or editing
    /// rows, omit it for new rows, and never display it as a column.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string?>>? Rows { get; init; }
    /// <summary>Write routing; null retains the legacy property-name command path.</summary>
    public BpmnPropertyBinding? Binding { get; init; }
    /// <summary>Help text displayed below the editor.</summary>
    public string? Description { get; init; }
    /// <summary>Help text displayed on hover.</summary>
    public string? Tooltip { get; init; }
    /// <summary>Text displayed in an empty editor.</summary>
    public string? Placeholder { get; init; }
    /// <summary>Expression metadata for custom editors.</summary>
    public BpmnFeelMode Feel { get; init; }
    /// <summary>Expression syntax; None falls back to FEEL mode when enabled.</summary>
    public BpmnExpressionLanguage ExpressionLanguage { get; init; }
    /// <summary>Validation metadata.</summary>
    public BpmnPropertyConstraints? Constraints { get; init; }
    /// <summary>Whether this entry may only be inspected.</summary>
    public bool IsReadOnly { get; init; }
    /// <summary>An explicit editor registry key.</summary>
    public string? EditorKey { get; init; }
    /// <summary>The containing group's id.</summary>
    public string? GroupId { get; init; }
    /// <summary>A validation error displayed below the editor.</summary>
    public string? Error { get; init; }
}

/// <summary>A group of snapshot-derived properties.</summary>
/// <param name="Id">Stable group identifier.</param>
/// <param name="Label">Displayed heading.</param>
/// <param name="Entries">Entries in display order.</param>
public sealed record BpmnPropertyGroup(string Id, string? Label, IReadOnlyList<BpmnPropertyEntry> Entries)
{
    internal bool HasBuiltInLabels { get; init; }

    /// <summary>Help text displayed on the heading.</summary>
    public string? Tooltip { get; init; }
    /// <summary>Whether the group is initially expanded.</summary>
    public bool OpenByDefault { get; init; } = true;
    /// <summary>Vendor namespace; null identifies a standard property group.</summary>
    public string? Namespace { get; init; }
}
