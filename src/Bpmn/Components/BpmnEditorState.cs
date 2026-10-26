using System.Collections.Immutable;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>The canvas-relative context menu.</summary>
/// <param name="IsOpen">Whether the menu is open.</param>
/// <param name="Anchor">The menu anchor.</param>
/// <param name="Target">The menu target kind.</param>
/// <param name="Snapshot">The target element.</param>
/// <param name="Commands">The commands offered.</param>
public sealed record BpmnContextMenuState(
    bool IsOpen,
    Anchor? Anchor,
    string Target,
    BpmnElementSnapshot? Snapshot,
    ImmutableArray<BpmnMenuEntry> Commands);

/// <summary>The canvas-relative inline label editor.</summary>
/// <param name="IsOpen">Whether a label is being edited.</param>
/// <param name="Anchor">The editor anchor.</param>
/// <param name="Request">The label and text metrics reported by the canvas.</param>
public sealed record BpmnLabelEditState(
    bool IsOpen,
    Anchor? Anchor,
    LabelEditRequested? Request);

/// <summary>The canvas-relative quick-action strip.</summary>
/// <param name="IsOpen">Whether the strip is visible.</param>
/// <param name="Anchor">The selected element anchor.</param>
/// <param name="Snapshot">The selected element.</param>
/// <param name="Actions">The actions offered.</param>
public sealed record BpmnContextualActionsState(
    bool IsOpen,
    Anchor? Anchor,
    BpmnElementSnapshot? Snapshot,
    ImmutableArray<BpmnContextualAction> Actions);

/// <summary>The accessible graph parallel to the drawn diagram.</summary>
/// <param name="IsOpen">Whether the accessible tree is rendered.</param>
/// <param name="Nodes">The graph in reading order.</param>
/// <param name="Current">The current graph node.</param>
/// <param name="AriaLabel">The tree accessible name.</param>
public sealed record BpmnAccessibleTreeState(
    bool IsOpen,
    ImmutableArray<BpmnGraphNode> Nodes,
    string? Current,
    string? AriaLabel);

/// <summary>Independent state slices for canvas widgets.</summary>
/// <param name="ContextMenu">The context menu slice.</param>
/// <param name="LabelEdit">The label editor slice.</param>
/// <param name="ContextualActions">The action strip slice.</param>
/// <param name="AccessibleTree">The accessible tree slice.</param>
public sealed record BpmnEditorOverlayState(
    BpmnContextMenuState ContextMenu,
    BpmnLabelEditState LabelEdit,
    BpmnContextualActionsState ContextualActions,
    BpmnAccessibleTreeState AccessibleTree);

/// <summary>The expanded size remembered for one Sub-Process.</summary>
/// <param name="Expanded">The last expanded bounds.</param>
/// <param name="Collapsed">The bounds after collapse.</param>
public sealed record BpmnExpansionState(
    Bounds Expanded,
    Bounds Collapsed);

/// <summary>An immutable snapshot of an editor session, replaced on every state change.</summary>
public sealed record BpmnEditorState
{
    internal BpmnEditorState() { }

    /// <summary>The document of record.</summary>
    public BpmnDocument? Document { get; init; }

    /// <summary>The document shown, including transient read-only inspection.</summary>
    public BpmnDocument? DrawnDocument { get; init; }

    /// <summary>The resolved canvas configuration.</summary>
    public EditorConfig Configuration { get; init; } = EditorConfig.CreateDefault();

    /// <summary>Whether document mutation is forbidden.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Whether an edit can be undone.</summary>
    public bool CanUndo { get; init; }

    /// <summary>Whether an edit can be replayed.</summary>
    public bool CanRedo { get; init; }

    /// <summary>The selected semantic element ids.</summary>
    public ImmutableArray<string> Selection { get; init; } = [];

    /// <summary>The per-session clipboard.</summary>
    public BpmnClipboardPayload Clipboard { get; init; } = BpmnClipboardPayload.Empty;

    /// <summary>Whether a fragment can be pasted.</summary>
    public bool CanPaste { get; init; }

    /// <summary>The stencil tool waiting for a drop.</summary>
    public BpmnStencilEntry? ArmedTool { get; init; }

    /// <summary>The file offered for import.</summary>
    public BpmnImportPreview? PendingImport { get; init; }

    /// <summary>Whether the document comes from a <see cref="Storage.BpmnDocumentBinding"/>.</summary>
    public bool HasDocumentBinding { get; init; }

    /// <summary>Whether the bound document has edits that are not stored; false without a binding.</summary>
    public bool IsDirty { get; init; }

    /// <summary>A change to the stored document by another party that the user has not resolved.</summary>
    public Storage.BpmnExternalChange? ExternalChange { get; init; }

    /// <summary>An agent's request awaiting the user's permission.</summary>
    public BpmnConfirmationRequest? PendingConfirmation { get; init; }

    /// <summary>Whether a Call Activity awaits a definition.</summary>
    public bool IsPickingCalledElement { get; init; }

    /// <summary>The document Message catalog and pending form.</summary>
    public BpmnMessageCatalogState MessageCatalog { get; init; } = new();

    internal string? MessageFocusOrigin { get; init; }

    /// <summary>The callable processes and global tasks.</summary>
    public ImmutableArray<(string Id, string? Name)> CallableDefinitions { get; init; } = [];

    /// <summary>Whether shortcut help is open.</summary>
    public bool ShortcutHelpVisible { get; init; }

    /// <summary>The findings for the document of record.</summary>
    public BpmnValidationReport Validation { get; init; } = BpmnValidationReport.Empty;

    /// <summary>The route to the active plane.</summary>
    public ImmutableArray<BpmnDiagramStep> NavigationPath { get; init; } = [];

    /// <summary>The document diagrams.</summary>
    public ImmutableArray<BpmnDiagramTarget> Diagrams { get; init; } = [];

    /// <summary>The active diagram id.</summary>
    public string? ActiveDiagramId { get; init; }

    /// <summary>The sizes remembered during this session.</summary>
    public ImmutableDictionary<string, BpmnExpansionState> Expansions { get; init; } = ImmutableDictionary<string, BpmnExpansionState>.Empty;

    /// <summary>The last observed engine state.</summary>
    public BpmnExecutionState? Execution { get; init; }

    /// <summary>The canvas zoom factor, where 1 is 100%.</summary>
    public double Zoom { get; init; } = 1d;

    /// <summary>The per-widget overlay slices.</summary>
    public BpmnEditorOverlayState Overlays { get; init; } = new(new(false, null, "canvas", null, []), new(false, null, null), new(false, null, null, []), new(true, [], null, null));

}
