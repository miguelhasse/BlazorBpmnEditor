using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnEditorSession
{
    internal BpmnEditorSession(BpmnOptions options, IServiceProvider services)
    {
        _options = Microsoft.Extensions.Options.Options.Create(options);
        _profileServices = services;
        RefreshState();
    }

    /// <summary>The latest immutable editor and widget state.</summary>
    public BpmnEditorState State { get; private set; } = new();

    /// <summary>Raised when the observable session state changes; unchanged notifications are suppressed.</summary>
    public event EventHandler? StateChanged;

    internal EditorConfig ConfigurationSnapshot => _snapshot;
    /// <summary>The element-template options in force.</summary>
    public BpmnTemplateOptions Templates => _options.Value.Templates;

    internal void AttachCanvas(BpmnCanvas canvas)
    {
        if (_canvas is not null && !ReferenceEquals(_canvas, canvas))
            throw new InvalidOperationException("An editor session supports only one BpmnEditorCanvas.");
        _canvas = canvas;
    }

    internal void DetachCanvas(BpmnCanvas canvas)
    {
        if (ReferenceEquals(_canvas, canvas)) _canvas = null;
    }

    /// <summary>Selects semantic elements, updating session state even without a canvas.</summary>
    /// <param name="elementIds">The semantic element ids to select.</param>
    public async Task SetSelectionAsync(IReadOnlyList<string> elementIds)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        var payload = new SelectionChanged([.. elementIds]);
        if (_canvas is null)
        {
            await HandleSelectionChangedAsync(payload).ConfigureAwait(true);
            return;
        }

        // Record locally first; the canvas bridge supplies the authoritative anchor and host event.
        _selection = payload.ElementIds;
        TrackSelection(payload);
        NotifyStateChanged();
        await _canvas.SetSelectionAsync(payload.ElementIds).ConfigureAwait(true);
    }

    private void NotifyStateChanged()
    {
        var previous = State;
        RefreshState();
        if (!ReferenceEquals(previous, State)) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshState()
    {
        _selectionSnapshot = _selection.Count == 1 ? Describe(_selection[0]) : null;
        _menuSnapshot = _menu?.ElementId is { Length: > 0 } id ? Describe(id) : null;
        var previous = State;
        _ = CallableDefinitions;
        _ = Diagrams;
        _ = AccessibleGraph;
        _ = MenuEntries;
        _ = ContextualActions;
        var overlays = previous.Overlays;
        var contextMenu = overlays.ContextMenu.IsOpen == IsContextMenuOpen
            && overlays.ContextMenu.Anchor == _menu?.Anchor && overlays.ContextMenu.Target == (_menu?.Target ?? "canvas")
            && ReferenceEquals(overlays.ContextMenu.Snapshot, _menuSnapshot) && overlays.ContextMenu.Commands == _menuCache.Items
            ? overlays.ContextMenu : new BpmnContextMenuState(IsContextMenuOpen, _menu?.Anchor, _menu?.Target ?? "canvas", _menuSnapshot, _menuCache.Items);
        var labelEdit = overlays.LabelEdit.IsOpen == IsEditingLabel && overlays.LabelEdit.Anchor == _labelEdit?.Anchor
            && overlays.LabelEdit.Request == _labelEdit
            ? overlays.LabelEdit : new BpmnLabelEditState(IsEditingLabel, _labelEdit?.Anchor, _labelEdit);
        var contextualActions = overlays.ContextualActions.IsOpen == ShowContextualActions
            && overlays.ContextualActions.Anchor == _selectionAnchor
            && ReferenceEquals(overlays.ContextualActions.Snapshot, _selectionSnapshot) && overlays.ContextualActions.Actions == _actionsCache.Items
            ? overlays.ContextualActions : new BpmnContextualActionsState(ShowContextualActions, _selectionAnchor, _selectionSnapshot, _actionsCache.Items);
        var accessibleTree = overlays.AccessibleTree.IsOpen == ShowAccessibleTree && overlays.AccessibleTree.Nodes == _graphCache.Items
            && overlays.AccessibleTree.Current == AccessibleCurrent && overlays.AccessibleTree.AriaLabel == AccessibleTreeLabel
            ? overlays.AccessibleTree : new BpmnAccessibleTreeState(ShowAccessibleTree, _graphCache.Items, AccessibleCurrent, AccessibleTreeLabel);
        var next = new BpmnEditorState
        {
            Document = EditedDocument,
            DrawnDocument = DrawnDocument,
            Configuration = PreserveRecord(previous.Configuration, _snapshot),
            IsReadOnly = IsReadOnly,
            CanUndo = CanUndo,
            CanRedo = CanRedo,
            Selection = PreserveArray(previous.Selection, _selection),
            Clipboard = Clipboard,
            CanPaste = CanPaste,
            ArmedTool = _armedTool,
            PendingImport = PendingImport,
            HasDocumentBinding = DocumentBinding is not null,
            IsDirty = DocumentBinding?.IsDirty ?? false,
            ExternalChange = DocumentBinding?.ExternalChange,
            PendingConfirmation = PendingConfirmation,
            IsPickingCalledElement = IsPickingCalledElement,
            CallableDefinitions = _callablesCache.Items,
            ShortcutHelpVisible = _shortcutHelpVisible,
            Validation = Validation,
            NavigationPath = PreserveArray(previous.NavigationPath, _path),
            Diagrams = _diagramsCache.Items,
            ActiveDiagramId = ActiveDiagramId,
            Expansions = ExpansionSnapshot(),
            Execution = ExecutionState,
            Zoom = _zoom,
            Overlays = ReferenceEquals(overlays.ContextMenu, contextMenu) && ReferenceEquals(overlays.LabelEdit, labelEdit)
                && ReferenceEquals(overlays.ContextualActions, contextualActions) && ReferenceEquals(overlays.AccessibleTree, accessibleTree)
                ? overlays : new BpmnEditorOverlayState(contextMenu, labelEdit, contextualActions, accessibleTree),
        };
        // Record equality compares ImmutableArray backing arrays, not their contents.
        State = PreserveRecord(previous, next);
        _stateDescriptionRevision = _descriptionRevision;
    }
}
