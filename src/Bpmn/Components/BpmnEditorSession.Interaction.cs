using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The context menu, the inline label editor and the contextual actions (sections 4.5 and 7).
/// </summary>
/// <remarks>
/// All three are Blazor chrome drawn over the canvas, and all three read the same
/// <see cref="BpmnElementSnapshot"/>. That is the point of the snapshot: three surfaces that each
/// worked out for themselves what an element is, what may be done to it and where it sits would be
/// three chances to disagree with the document and with each other.
/// </remarks>
public sealed partial class BpmnEditorSession
{
    private ContextMenuRequested? _menu;
    private LabelEditRequested? _labelEdit;
    private Interop.Anchor? _selectionAnchor;
    private BpmnElementSnapshot? _selectionSnapshot;

    /// <summary>Whether the custom context menu is open.</summary>
    public bool IsContextMenuOpen => _menu is not null;

    /// <summary>The snapshot the menu is showing, or <see langword="null"/> for a canvas-level menu.</summary>
    public BpmnElementSnapshot? ContextMenuSnapshot { get; private set; }

    /// <summary>Whether a label is being edited in place.</summary>
    public bool IsEditingLabel => _labelEdit is not null;

    /// <summary>Replaces <see cref="Describe"/>; lets tests supply snapshots no document would produce.</summary>
    internal Func<string, BpmnElementSnapshot?>? DescribeOverride { get; set; }

    /// <summary>
    /// Describes an element the way section 7 requires, against the document now in effect.
    /// </summary>
    /// <param name="elementId">The element to describe.</param>
    /// <returns>The snapshot, or <see langword="null"/> if no document holds that id.</returns>
    public BpmnElementSnapshot? Describe(string elementId) => CachedDescribe(elementId);

    // --- the context menu ---------------------------------------------------------

    internal async Task HandleContextMenuRequestedAsync(ContextMenuRequested payload)
    {
        /*
         * A right-click on an unselected element selects it first: the menu's commands act on the
         * selection, and a Delete that removed something other than what was right-clicked would
         * be the worst kind of surprise. The selection change comes back through
         * TrackSelection, which clears the menu — so the menu is opened afterwards, not before.
         */
        if (payload.ElementId is { Length: > 0 } target
            && !_selection.Contains(target))
        {
            await SetSelectionAsync([target]).ConfigureAwait(true);
        }

        _menu = payload;
        ContextMenuSnapshot = payload.ElementId is { Length: > 0 } id ? Describe(id) : null;

        NotifyStateChanged();
    }

    /// <summary>Closes the context menu.</summary>
    public void CloseContextMenu()
    {
        if (_menu is null) return;

        _menu = null;
        ContextMenuSnapshot = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// What the menu offers.
    /// </summary>
    /// <remarks>
    /// Section 7 requires the canvas-level menu to open even when nothing target-specific applies. It
    /// then has no entries: the edit commands need a target, and viewport commands live in the toolbar.
    /// </remarks>
    private IReadOnlyList<BpmnMenuEntry> BuildMenuEntries
    {
        get
        {
            if (_menu is null) return [];

            var entries = new List<BpmnMenuEntry>();
            var hasTarget = ContextMenuSnapshot is not null;
            var capabilities = ContextMenuSnapshot?.Capabilities ?? BpmnElementCapabilities.None;

            if (hasTarget && capabilities.CanEditLabel)
            {
                entries.Add(MenuEntry(BpmnMenuCommand.EditLabel));
            }

            if (hasTarget)
            {
                // Copy survives Readonly: reading a diagram onto the clipboard changes nothing
                // about it, and section 7 only asks Readonly to lose the mutations.
                entries.Add(MenuEntry(BpmnMenuCommand.Copy));
            }

            if (hasTarget && !IsReadOnly)
            {
                entries.Add(MenuEntry(BpmnMenuCommand.Cut));
                entries.Add(MenuEntry(BpmnMenuCommand.Duplicate));
                entries.Add(MenuEntry(BpmnMenuCommand.Delete, capabilities.CanDelete));
            }

            return entries;
        }
    }

    private BpmnMenuEntry MenuEntry(BpmnMenuCommand command, bool isEnabled = true) =>
        new(command, DisplayText.MenuCommand(command), isEnabled);

    /// <summary>Runs a context-menu command against the menu target, closing the menu first.</summary>
    /// <param name="command">The chosen command.</param>
    /// <returns>A task that completes once the command has run.</returns>
    public async Task InvokeMenuCommandAsync(BpmnMenuCommand command)
    {
        var target = ContextMenuSnapshot?.ElementId;
        var acting = target is null ? _selection : (IReadOnlyList<string>)[target];

        CloseContextMenu();

        switch (command)
        {
            case BpmnMenuCommand.EditLabel when target is not null && _canvas is not null:
                await _canvas.EditLabelAsync(target).ConfigureAwait(true);
                break;

            case BpmnMenuCommand.Copy:
                CopyToClipboard(acting);
                break;

            case BpmnMenuCommand.Cut:
                await HandleEditRequestedAsync(new EditRequest("cut", ElementIds: acting)).ConfigureAwait(true);
                break;

            case BpmnMenuCommand.Duplicate:
                await HandleEditRequestedAsync(new EditRequest("duplicate", ElementIds: acting)).ConfigureAwait(true);
                break;

            case BpmnMenuCommand.Delete:
                await HandleEditRequestedAsync(new EditRequest("delete", ElementIds: acting)).ConfigureAwait(true);
                break;

            case BpmnMenuCommand.Paste:
                await HandleEditRequestedAsync(new EditRequest("paste")).ConfigureAwait(true);
                break;

            case BpmnMenuCommand.Undo:
                await UndoAsync().ConfigureAwait(true);
                break;

            case BpmnMenuCommand.Redo:
                await RedoAsync().ConfigureAwait(true);
                break;

            case BpmnMenuCommand.SelectAll when _canvas is not null:
                await _canvas.SetSelectionAsync(AllElementIds()).ConfigureAwait(true);
                break;

            case BpmnMenuCommand.FitToContent:
                await SetViewportAsync(ViewportCommand.FitToContent).ConfigureAwait(true);
                break;

            default:
                break;
        }
    }

    private IReadOnlyList<string> AllElementIds() => EditedDocument is not { } document
        ? []
        : [.. document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes.Select(shape => shape.BpmnElement)
                .Concat(diagram.Plane.Edges.Select(edge => edge.BpmnElement)))];

    // --- property editing and type switching --------------------------------------

    /// <summary>
    /// Applies a property edit and refreshes the snapshot the menu is bound to.
    /// </summary>
    /// <remarks>
    /// Section 7 asks for "an atomic result and updated snapshot/diagnostics". Rebuilding the
    /// snapshot from the document that came back is what makes the second half true: a refused
    /// edit leaves the old document, so re-describing it restores exactly what the menu was
    /// showing, and an accepted one re-describes the new state including whatever else moved with
    /// it — a default flow that changed hands, a trigger whose references were cleared.
    /// </remarks>
    /// <param name="command">The property edit submitted from the menu.</param>
    /// <returns>A task that completes once the edit is applied or refused.</returns>
    public async Task ApplyMenuPropertyAsync(UpdatePropertiesCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (IsReadOnly || EnsureHistory() is null) return;

        var result = await ApplyAsync(command).ConfigureAwait(true);
        await ReportRejectionsAsync(result).ConfigureAwait(true);

        ContextMenuSnapshot = Describe(command.ElementId);
        _selectionSnapshot = _selectionSnapshot is null ? null : Describe(_selectionSnapshot.ElementId);
        NotifyStateChanged();
    }

    /// <summary>
    /// The selected elements, described for a property surface.
    /// </summary>
    /// <remarks>
    /// Built on demand from the current document rather than cached alongside the selection: an
    /// edit anywhere can change what a snapshot says, and a panel showing a snapshot taken when the
    /// element was selected is the stale standard field section 7 forbids.
    /// </remarks>
    public IReadOnlyList<BpmnElementSnapshot> SelectionSnapshots =>
        [.. _selection.Select(Describe).OfType<BpmnElementSnapshot>()];

    /// <summary>
    /// Every place the document names the selected element.
    /// </summary>
    /// <remarks>
    /// Only for a single selection. "What refers to this" is a question about one element, and the
    /// union across a selection would mix the answers together without saying which was whose.
    /// </remarks>
    public IReadOnlyList<BpmnReferenceSite> SelectionUsages => UsagesOverride ?? (_selection.Count == 1 && EditedDocument is { } document
        ? BpmnReferenceMap.UsagesOf(document, _selection[0])
        : []);

    /// <summary>Test seam: replaces the usages of the selection.</summary>
    internal IReadOnlyList<BpmnReferenceSite>? UsagesOverride { get; set; }

    /// <summary>Test seam: replaces the diagnostics of the selection.</summary>
    internal IReadOnlyList<Validation.BpmnDiagnostic>? DiagnosticsOverride { get; set; }

    /// <summary>Test seam: receives every property command instead of the edit history.</summary>
    internal Func<BpmnEditCommand, Task<BpmnEditResult?>>? PropertyCommandOverride { get; set; }

    /// <summary>Test seam: receives every followed usage instead of the canvas.</summary>
    internal Func<BpmnReferenceSite, Task>? GoToUsageOverride { get; set; }

    /// <summary>
    /// Selects the element that holds a reference, so a usage can be followed.
    /// </summary>
    /// <param name="usage">The usage that was chosen.</param>
    /// <returns>A task that completes once the owner is selected.</returns>
    public async Task GoToUsageAsync(BpmnReferenceSite usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (GoToUsageOverride is { } follow)
        {
            await follow(usage).ConfigureAwait(true);
            return;
        }

        if (_canvas is null) return;

        /*
         * The owner of a DI reference is a shape or an edge, which is not something a user can
         * select — so a DI usage goes to the element that shape draws, which is the thing they
         * meant. For everything else the owner is the answer.
         */
        var target = usage.DiagramId is null ? usage.OwnerId : usage.TargetId;

        await SetSelectionAsync([target]).ConfigureAwait(true);
    }

    /// <summary>The diagnostics raised against the current selection.</summary>
    public IReadOnlyList<Validation.BpmnDiagnostic> SelectionDiagnostics => DiagnosticsOverride ??
        [.. _selection.SelectMany(Validation.For)];

    /// <summary>
    /// Applies a property command from a surface that may be editing more than one element.
    /// </summary>
    /// <remarks>
    /// The panel decides how many elements an edit touches and wraps a bulk one in a
    /// <see cref="CompoundCommand"/>; the editor stays the only thing that applies commands and
    /// owns history, so a bulk edit is one undo step and is refused whole.
    /// </remarks>
    /// <param name="command">The command the surface produced.</param>
    /// <returns>
    /// What the editor did, so a caller can tell an applied edit from a refused one; or
    /// <see langword="null"/> when the editor was in no position to try.
    /// </returns>
    public async Task<BpmnEditResult?> ApplyPropertyCommandAsync(BpmnEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (IsReadOnly) return null;
        if (PropertyCommandOverride is { } intercept) return await intercept(command).ConfigureAwait(true);
        if (EnsureHistory() is null) return null;

        var result = await ApplyAsync(command).ConfigureAwait(true);
        await ReportRejectionsAsync(result).ConfigureAwait(true);

        /*
         * A rename moves the selection with it. The canvas is still holding the id the element had
         * a moment ago, which now belongs to nothing — so without this the element the user was
         * working on deselects itself the instant they rename it, and the panel they renamed it in
         * goes blank.
         */
        if (result.IsApplied && command is RenameElementCommand rename)
        {
            // Locally as well as on the canvas, the way a paste does it. The canvas reloads from
            // its Document parameter on the next render, so a selection pushed to it now names a
            // cell its graph has not been given yet; the local list is what the panel reads, and
            // it must be right immediately or the element deselects itself as it is renamed.
            var renamed = rename.NewId.Trim();

            if (_canvas is not null) await _canvas.SetSelectionAsync([renamed]).ConfigureAwait(true);
            _selection = [renamed];
        }

        ContextMenuSnapshot = ContextMenuSnapshot is null ? null : Describe(ContextMenuSnapshot.ElementId);
        _selectionSnapshot = _selectionSnapshot is null ? null : Describe(_selectionSnapshot.ElementId);
        NotifyStateChanged();

        return result;
    }

    /// <summary>Switches an element's type from the context menu, keeping the menu open.</summary>
    /// <param name="command">The type switch to apply.</param>
    /// <returns>A task that completes once the switch is applied or refused.</returns>
    public async Task SwitchElementTypeAsync(SwitchElementTypeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (IsReadOnly || EnsureHistory() is null) return;

        var result = await ApplyAsync(command).ConfigureAwait(true);
        await ReportRejectionsAsync(result).ConfigureAwait(true);

        // Kept open on success as well as on refusal: a switch is a small correction, and closing
        // the menu on every one would make correcting two properties in a row a four-gesture job.
        ContextMenuSnapshot = Describe(command.ElementId);
        _selectionSnapshot = _selectionSnapshot is null ? null : Describe(_selectionSnapshot.ElementId);
        NotifyStateChanged();
    }

    private async Task ReportRejectionsAsync(BpmnEditResult result)
    {
        foreach (var rejection in result.Rejections)
        {
            await OnEditRejected
                .InvokeAsync(new EditRejected(rejection.Code, rejection.Message, rejection.ElementId ?? string.Empty))
                .ConfigureAwait(true);
        }
    }

    // --- inline label editing (section 4.5) ----------------------------------------

    internal Task HandleLabelEditRequestedAsync(LabelEditRequested payload)
    {
        if (IsReadOnly) return Task.CompletedTask;

        // The menu and the editor never share the screen: both are anchored on the same element,
        // and the menu is how the editor was most likely opened.
        _menu = null;
        ContextMenuSnapshot = null;
        _labelEdit = payload;

        NotifyStateChanged();
        return Task.CompletedTask;
    }

    /// <summary>Closes the inline label editor without changing anything.</summary>
    public void CancelLabelEdit()
    {
        if (_labelEdit is null) return;

        _labelEdit = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// Commits the inline editor's text.
    /// </summary>
    /// <remarks>
    /// Which property it is depends on the element: a Text Annotation's content is its
    /// <c>text</c>, and everything else's label is its <c>name</c>. The snapshot already knows —
    /// the descriptor list contains whichever one applies — so the answer comes from there rather
    /// than from a second type switch here.
    /// </remarks>
    /// <param name="text">The edited label text.</param>
    /// <returns>A task that completes once the label is applied or refused.</returns>
    public async Task CommitLabelEditAsync(string text)
    {
        var request = _labelEdit;
        _labelEdit = null;
        NotifyStateChanged();

        if (request is null || IsReadOnly) return;

        var snapshot = Describe(request.ElementId);
        var property = snapshot?.Properties.FirstOrDefault(candidate => candidate.Name is "name" or "text");

        if (property is null)
        {
            NotifyStateChanged();
            return;
        }

        // Unchanged text is not an edit. Committing it anyway would put an entry on the undo stack
        // for a double-click and an Escape-that-wasn't.
        if ((property.Value ?? string.Empty) == text)
        {
            NotifyStateChanged();
            return;
        }

        var result = await ApplyAsync(
            new UpdatePropertiesCommand(request.ElementId, property.Name, text)).ConfigureAwait(true);

        await ReportRejectionsAsync(result).ConfigureAwait(true);
        NotifyStateChanged();
    }

    // --- contextual actions ---------------------------------------------------------

    /// <summary>Whether the quick-action strip is showing.</summary>
    private bool ShowContextualActions => !IsReadOnly
        && _labelEdit is null
        && _menu is null
        && _selectionAnchor is not null
        && ContextualActions.Count > 0;

    private IReadOnlyList<BpmnContextualAction> ContextualActions =>
        CachedContextualActions();

    /// <summary>Records where the one selected element is, for the surfaces that sit beside it.</summary>
    private void TrackSelection(SelectionChanged payload)
    {
        _selectionAnchor = payload.Anchor;
        _selectionSnapshot = payload.ElementIds.Count == 1 ? Describe(payload.ElementIds[0]) : null;

        /*
         * Both overlays belong to one element, so they close when the selection moves off it —
         * but not when it moves *onto* it. Right-clicking an unselected shape selects it first,
         * and that selection change arrives while the menu for that very shape is opening;
         * treating every change as "the user moved on" would close the menu before it appeared.
         */
        var stillTheTarget = _menu?.ElementId is { Length: > 0 } menuTarget
            && payload.ElementIds.Contains(menuTarget);

        if (!stillTheTarget)
        {
            _menu = null;
            ContextMenuSnapshot = null;
        }

        if (_labelEdit is { } editing && !payload.ElementIds.Contains(editing.ElementId)) _labelEdit = null;
    }

    /// <summary>Moves the strip with its element, or hides it while a gesture is moving it.</summary>
    internal void TrackSelectionAnchor(SelectionAnchorChanged payload)
    {
        // A late report about an element that is no longer the selection must not move the strip.
        if (_selection.Count != 1 || _selection[0] != payload.ElementId) return;

        _selectionAnchor = payload.Anchor;
        NotifyStateChanged();
    }

    /// <summary>Runs a quick action against the single selected element.</summary>
    /// <param name="action">The chosen action.</param>
    /// <returns>A task that completes once the action has run.</returns>
    public async Task InvokeContextualActionAsync(BpmnContextualAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var snapshot = _selectionSnapshot;
        if (snapshot is null || _canvas is null) return;

        switch (action.Kind)
        {
            case BpmnActionKind.Append:
                await AppendAsync(snapshot, action).ConfigureAwait(true);
                break;

            case BpmnActionKind.AddTextAnnotation:
                await AddTextAnnotationAsync(snapshot).ConfigureAwait(true);
                break;

            case BpmnActionKind.EditLabel:
                await _canvas.EditLabelAsync(snapshot.ElementId).ConfigureAwait(true);
                break;

            case BpmnActionKind.Delete:
                await HandleEditRequestedAsync(
                    new EditRequest("delete", ElementIds: [snapshot.ElementId])).ConfigureAwait(true);
                break;

            case BpmnActionKind.OpenMenu:
                await _canvas.RequestContextMenuAsync(snapshot.ElementId, "keyboard").ConfigureAwait(true);
                break;

            default:
                break;
        }
    }

    private async Task AddTextAnnotationAsync(BpmnElementSnapshot source)
    {
        if (_canvas is null || IsReadOnly
            || !(source.Capabilities.CanConnect || source.ElementType == "lane" && source.Capabilities.CanEditLabel)
            || source.Bounds is null
            || EnsureHistory() is null) return;

        var diagramId = ActiveDiagramId;
        var target = await _canvas.ResolveAppendPointAsync(source.ElementId, "textAnnotation").ConfigureAwait(true);
        var containerId = source.ContainerId ?? ProcessForPool(source.PoolId) ?? EditedDocument?.Collaboration?.Id;
        if (target is null || containerId is null) return;

        var id = BpmnId.New("TextAnnotation");
        var result = await ApplyAsync(new CompoundCommand(
            [
                new CreateArtifactCommand(id, BpmnArtifactKind.TextAnnotation, containerId, target.Bounds, Text: string.Empty)
                { DiagramId = diagramId },
                new CreateConnectorCommand(BpmnId.New("Association"), BpmnConnectorKind.Association,
                    source.ElementId, id, WaypointsBetween(source, target.Bounds), containerId)
                { DiagramId = diagramId },
            ], "Add Text Annotation")).ConfigureAwait(true);
        await AfterEditAsync(result).ConfigureAwait(true);
        if (!result.IsApplied || diagramId != ActiveDiagramId) return;
        await _canvas.SetSelectionAsync([id]).ConfigureAwait(true);
        await _canvas.EditLabelAsync(id).ConfigureAwait(true);
    }

    /// <summary>
    /// Creates an element after another one and connects the two.
    /// </summary>
    /// <remarks>
    /// One compound command, not two. Section 9.3 makes creation a single transaction, and the
    /// same argument applies to the pair: a Task that appeared with no flow into it is not what the
    /// user asked for, so if the flow is refused the Task must not survive either. Undo takes back
    /// the whole step for the same reason.
    /// </remarks>
    private async Task AppendAsync(BpmnElementSnapshot source, BpmnContextualAction action)
    {
        if (_canvas is null || IsReadOnly || EnsureHistory() is null) return;

        var diagramId = ActiveDiagramId;
        var type = action.Type;
        var target = await _canvas
            .ResolveAppendPointAsync(source.ElementId, BpmnElementInspector.WireNameOf(type))
            .ConfigureAwait(true);

        if (target is null) return;

        // The appended node keeps the source's owner unless the geometry says otherwise: an append
        // that crossed into another Pool would put a Sequence Flow across a Pool boundary, which
        // SF-003 refuses.
        var containerId = target.ContainerId ?? ProcessForPool(target.PoolId) ?? source.ContainerId;
        if (containerId is null) return;

        var elementId = BpmnId.New(BpmnElementInspector.WireNameOf(type));

        var create = new CreateElementCommand(
            elementId,
            type,
            containerId,
            target.Bounds,
            EventDefinition: action.EventDefinition,
            PoolId: target.PoolId ?? source.PoolId)
        {
            DiagramId = diagramId,
        };

        var connect = new CreateConnectorCommand(
            BpmnId.New("Flow"),
            BpmnConnectorKind.SequenceFlow,
            source.ElementId,
            elementId,
            WaypointsBetween(source, target.Bounds),
            containerId)
        {
            DiagramId = diagramId,
        };

        var result = await ApplyAsync(
            new CompoundCommand([create, connect], $"Append {BpmnElementInspector.TypeLabel(type)}"))
            .ConfigureAwait(true);

        await AfterEditAsync(result).ConfigureAwait(true);

        if (result.IsApplied && diagramId == ActiveDiagramId)
            await _canvas.SetSelectionAsync([elementId]).ConfigureAwait(true);
    }

    /// <summary>
    /// A straight route from the source's right edge to the new element's left edge.
    /// </summary>
    /// <remarks>
    /// Deliberately plain. The appended element is placed directly to the right at the same
    /// vertical centre, so the two edge midpoints are the correct endpoints and there is nothing
    /// to route around; M5's router will improve this when it improves every other route.
    /// </remarks>
    private static IReadOnlyList<Model.Di.DiPoint> WaypointsBetween(
        BpmnElementSnapshot source,
        Model.Di.Bounds target)
    {
        var from = source.Bounds ?? new Model.Di.Bounds(target.X - 150, target.Y, 100, target.Height);

        return
        [
            new Model.Di.DiPoint(from.X + from.Width, from.Y + (from.Height / 2)),
            new Model.Di.DiPoint(target.X, target.Y + (target.Height / 2)),
        ];
    }
}
