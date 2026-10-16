using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// Headless document editing, navigation and interaction state shared by an editor composition.
/// </summary>
/// <remarks>
/// <para>
/// Receives the immutable, versioned configuration resolved by <see cref="BpmnEditorScope"/>.
/// The canvas sends it before any JointJS object exists (implementation plan section 2).
/// </para>
/// <para>
/// Holds no <see cref="Microsoft.JSInterop.IJSRuntime"/>, no module reference and no
/// <see cref="Microsoft.JSInterop.DotNetObjectReference{T}"/>: everything reaches the canvas
/// through the child component's .NET API.
/// </para>
/// </remarks>
public sealed partial class BpmnEditorSession
{
    private BpmnCanvas? _canvas;
    private EditorConfig _snapshot = EditorConfig.CreateDefault();
    private bool _shortcutHelpVisible;
    private BpmnEditHistory? _history;
    private BpmnDocument? _historySource;

    /// <summary>
    /// The document this editor last handed to <see cref="OnDocumentChanged"/>.
    /// </summary>
    /// <remarks>
    /// A host that follows that event and passes the result back in as <c>Document</c> is doing
    /// exactly the right thing, and without remembering what we published it looks identical to
    /// the host opening a different file — which would clear the undo history on every single
    /// edit.
    /// </remarks>
    private BpmnDocument? _lastPublished;
    private BpmnStencilEntry? _armedTool;
    private IReadOnlyList<string> _selection = [];
    private BpmnClipboardPayload _clipboard = BpmnClipboardPayload.Empty;

    private readonly IOptions<BpmnOptions> _options;
    private BpmnKitDefaults? _kitDefaults;
    private BpmnPropertyEditorRegistry? _editors;

    /// <summary>The composition's own part and editor defaults, outranked by the host's choices.</summary>
    public BpmnKitDefaults? KitDefaults
    {
        get => _kitDefaults;
        internal set
        {
            if (ReferenceEquals(_kitDefaults, value)) return;
            _kitDefaults = value;
            _editors = null;
        }
    }

    /// <summary>The property editor registry this editor resolves entries through.</summary>
    public BpmnPropertyEditorRegistry Editors =>
        _editors ??= _kitDefaults is null
            ? _options.Value.Properties.Editors
            : _options.Value.Properties.Editors.WithKitDefaults(_kitDefaults.Editors);

    /// <summary>Explicit configuration, overriding the DI-registered options.</summary>
    internal EditorConfig? Configuration { get; set; }

    /// <summary>The document to display.</summary>
    internal BpmnDocument? Document { get; set; }

    /// <summary>Which diagram to show; the first declared one when omitted.</summary>
    internal string? DiagramId { get; set; }

    /// <summary>
    /// Whether the editor forbids mutation. Selection, inspection, navigation and export stay
    /// available; only mutating surfaces are disabled.
    /// </summary>
    internal bool IsReadOnly { get; set; }

    /// <summary>
    /// Whether traversed Sequence Flows animate a travelling token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nullable so that leaving it alone means "whatever the application configured" rather than
    /// "Disabled". A non-nullable parameter defaulting to the same value as the option would
    /// silently overrule a host that had set it in <see cref="BpmnOptions"/>, and the override
    /// would be invisible at the call site precisely because nothing was written there.
    /// </para>
    /// <para>
    /// Answered on the root as a data attribute rather than in the configuration snapshot, so that
    /// changing it takes effect. The snapshot reaches the canvas once and no later revision is
    /// applied to a live paper.
    /// </para>
    /// </remarks>
    internal BpmnExecutionAnimation? ExecutionAnimation { get; set; }

    /// <summary>The value for the root's <c>data-bpmn-animation</c>, or null when nothing moves.</summary>
    /// <remarks>
    /// Absent rather than "disabled", matching the theme attributes: the stylesheet animates on
    /// the presence of the opt-in and needs no second state to agree with.
    /// </remarks>
    public string? AnimationAttribute =>
        (ExecutionAnimation ?? _options.Value.Execution.Animation) == BpmnExecutionAnimation.Enabled
            ? "tokens"
            : null;

    /// <summary>Accessible name of the canvas region.</summary>
    internal string AriaLabel { get; set; } = "BPMN diagram canvas";

    /// <summary>
    /// The document as edited, which is what the canvas renders.
    /// </summary>
    /// <remarks>
    /// Distinct from the <c>Document</c> parameter, which is what the host supplied. Edits produce
    /// new documents, and a host that wants to follow them binds <see cref="OnDocumentChanged"/>
    /// rather than having its own reference silently replaced.
    /// </remarks>
    public BpmnDocument? EditedDocument => _history?.Document ?? Document;

    /// <summary>
    /// The document as it is drawn, which in read-only may include a Sub-Process opened for a look.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="EditedDocument"/> deliberately. That one is the document of record
    /// — what a host would save, and what undo and validation are about — and a read-only
    /// inspection must not reach it (section 1.2.1: it "must not dirty, serialize, or enter
    /// document history"). This one is what is on screen, which is the canvas's question and the
    /// accessible tree's.
    /// </remarks>
    public BpmnDocument? DrawnDocument => _inspection ?? EditedDocument;

    /// <summary>Whether there is an edit to undo.</summary>
    public bool CanUndo => _history?.CanUndo ?? false;

    /// <summary>Whether there is an edit to redo.</summary>
    public bool CanRedo => _history?.CanRedo ?? false;

    /// <summary>Who asked for the edit <see cref="UndoAsync"/> would reverse, or null when there is none.</summary>
    public BpmnEditOrigin? UndoOrigin => _history?.UndoOrigin;

    /// <summary>Who asked for the edit <see cref="RedoAsync"/> would reapply, or null when there is none.</summary>
    public BpmnEditOrigin? RedoOrigin => _history?.RedoOrigin;

    /// <summary>Steps back one edit.</summary>
    public Task UndoAsync() => ReplayAsync(history => history.Undo());

    /// <summary>Steps forward one edit.</summary>
    public Task RedoAsync() => ReplayAsync(history => history.Redo());

    /// <summary>Applies an edit command, recording it in the history.</summary>
    /// <param name="command">The edit to apply.</param>
    /// <returns>The outcome, whose document is always the one now in effect.</returns>
    public async Task<BpmnEditResult> ApplyAsync(BpmnEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var history = EnsureHistory();
        if (history is null)
        {
            throw new InvalidOperationException("There is no document open to edit.");
        }

        if (IsReadOnly)
        {
            throw new InvalidOperationException("This editor is read-only; no edit can be applied.");
        }

        var result = history.Apply(command);
        if (result.IsApplied) await PushDocumentAsync(result.Document).ConfigureAwait(true);

        return result;
    }

    /// <summary>
    /// Opens the shortcut help overlay.
    /// </summary>
    /// <remarks>
    /// Public so a host's own toolbar button can open it, not only the bound key. The overlay is
    /// the editor's because it describes the editor's configuration, and a host that had to build
    /// its own would have to read the binding list and format it identically.
    /// </remarks>
    public void ShowShortcutHelp()
    {
        _shortcutHelpVisible = true;
        NotifyStateChanged();
    }

    /// <summary>Closes the shortcut help overlay.</summary>
    public void HideShortcutHelp()
    {
        _shortcutHelpVisible = false;
        NotifyStateChanged();
    }

    /// <summary>
    /// Raised after an edit produced a new document.
    /// </summary>
    /// <remarks>
    /// The editor does not write back to the <c>Document</c> parameter. A host that wants to keep
    /// its own reference current handles this; one that does not can still read
    /// <see cref="EditedDocument"/>.
    /// </remarks>
    internal EventCallback<BpmnDocument> OnDocumentChanged { get; set; }

    /// <summary>Raised (coalesced) whenever the canvas selection changes.</summary>
    internal EventCallback<SelectionChanged> OnSelectionChanged { get; set; }

    /// <summary>
    /// Records the selection on the way past, then hands it to the host.
    /// </summary>
    /// <remarks>
    /// Intercepted rather than merely forwarded because the stencil follows the selection: which
    /// container the user is creating into is what decides the palette, and on a Collaboration
    /// plane nothing at all may be created until a Pool is chosen.
    /// </remarks>
    internal async Task HandleSelectionChangedAsync(SelectionChanged payload)
    {
        _selection = System.Collections.Immutable.ImmutableArray.CreateRange(payload.ElementIds);
        TrackSelection(payload);
        NotifyStateChanged();

        await OnSelectionChanged.InvokeAsync(payload).ConfigureAwait(true);
    }

    /// <summary>Raised when an edit was refused, by either the canvas or the document editor.</summary>
    internal EventCallback<EditRejected> OnEditRejected { get; set; }

    /// <summary>Raised once the canvas is ready for calls.</summary>
    internal EventCallback<CanvasInitialized> OnCanvasInitialized { get; set; }

    /// <summary>Raised after a document has been projected onto the canvas.</summary>
    internal EventCallback<DocumentLoaded> OnDocumentLoaded { get; set; }

    /// <summary>Raised when the canvas refuses a configuration snapshot or patch.</summary>
    internal EventCallback<ConfigRejected> OnConfigRejected { get; set; }

    /// <summary>Raised (coalesced) whenever the canvas transform changes.</summary>
    internal EventCallback<ViewportChanged> OnViewportChanged { get; set; }

    private double _zoom = 1d;

    /// <summary>Records the zoom for <see cref="BpmnEditorState.Zoom"/>, then hands the change to the host.</summary>
    internal Task HandleViewportChangedAsync(ViewportChanged payload)
    {
        _zoom = payload.Zoom;
        NotifyStateChanged();
        return OnViewportChanged.InvokeAsync(payload);
    }

    /// <summary>
    /// Raised when a bound key fires a command the canvas does not handle itself.
    /// </summary>
    /// <remarks>
    /// Forwarded after the editor has acted on it, so a host can observe <c>showShortcuts</c>
    /// without having to reimplement the overlay, or handle a command a newer canvas reports that
    /// this assembly does not know.
    /// </remarks>
    internal EventCallback<ShortcutInvoked> OnShortcutInvoked { get; set; }

    internal void UpdateParameters(EditorConfig snapshot)
    {
        if (ParametersUnchanged(snapshot)) return;
        _snapshot = snapshot;
        /*
         * A new document from the host starts a new session, and that has to be noticed here rather
         * than on the next edit. The canvas renders EditedDocument, which is the history's document
         * from the moment anything has been edited — so while the reset was lazy, loading a second
         * file left the first one on screen and only the parts of the UI reading the parameter
         * directly described the new one. It looked like the canvas had stopped refreshing, and it
         * only happened once the user had touched something.
         */
        StartSessionIfNewDocument();
        NotifyStateChanged();
    }

    private IReadOnlyList<IBpmnPasteTransform> TemplatePasteTransforms(EditingOptions editing) =>
        editing.PasteTransforms.Any(transform => transform is BpmnTemplatePasteTransform) ? editing.PasteTransforms :
        [.. editing.PasteTransforms, new BpmnTemplatePasteTransform(_options.Value.Templates.Registry)];

    private EditingOptions HistoryOptions() => _snapshot.Editing with { PasteTransforms = TemplatePasteTransforms(_snapshot.Editing) };

    /// <summary>
    /// Handles an edit the canvas asked for.
    /// </summary>
    /// <remarks>
    /// A move has already been applied to the paper, so the new document is handed back to the
    /// canvas as "already rendered" rather than reloaded — reloading mid-gesture would discard the
    /// optimistic update. A delete, undo or redo has nothing on screen yet, so the ordinary
    /// parameter change is what makes the canvas pick it up.
    /// </remarks>
    internal async Task HandleEditRequestedAsync(EditRequest request)
    {
        var history = EnsureHistory();
        if (history is null) return;
        var diagramId = request.DiagramId ?? ActiveDiagramId;

        /*
         * Copy first, because it is not a mutation. Section 7 asks read-only to keep inspection
         * and lose every mutation surface; refusing to let someone copy a diagram they are allowed
         * to read would be a restriction the specification never asks for.
         */
        if (request.Kind == "copy")
        {
            CopyToClipboard(request.ElementIds ?? []);
            return;
        }

        /*
         * Expanding is not always a mutation.
         *
         * Section 1.2.1 asks read-only for "a transient local inspection toggle", and looking
         * inside a closed container is a reading activity: a reviewer should not need write access
         * to find out what a Sub-Process contains. It is handled before the refusal below because
         * it is not the kind of thing that refusal is protecting.
         */
        if (IsReadOnly && request.Kind is "expand" or "collapse" && request.ElementId is { } inspecting)
        {
            await InspectExpansionAsync(inspecting, request.Kind == "expand").ConfigureAwait(true);
            return;
        }

        // Checked here as well as on the canvas, for the same reason collisions are: the canvas
        // closes the doors it owns, and this is the document of record, which must refuse a
        // mutation however it arrived.
        if (IsReadOnly)
        {
            await OnEditRejected
                .InvokeAsync(new EditRejected("EDIT-002", "This editor is read-only.", string.Empty))
                .ConfigureAwait(true);
            return;
        }

        switch (request.Kind)
        {
            case "move" when request.Moves is { Count: > 0 } moves:
                var command = new MoveShapesCommand(
                    [.. moves.Select(move => new ShapeGeometry(move.ShapeId, move.Bounds))],
                    [.. (request.Routes ?? []).Select(route => new ConnectorRoute(route.ElementId, route.Waypoints))])
                {
                    DiagramId = diagramId,
                };

                var result = history.Apply(command);

                /*
                 * Accepted before the parameter change is seen, so the canvas recognises the new
                 * reference as what it is already showing — but only when the result contains
                 * nothing the canvas has not drawn. An auto-expanded container is exactly that:
                 * the canvas dragged the element and knows nothing about the Pool that grew
                 * around it, so claiming the document is already on screen freezes the Pool at
                 * its old size.
                 *
                 * Re-squared connectors are the same kind of thing. The canvas computed them, but
                 * it computed them from the route it had just drawn — it has not drawn the squared
                 * one, and telling it otherwise leaves every moved connector diagonal on screen
                 * while the document holds the right geometry.
                 */
                var alreadyDrawn = !GrewAContainer(result) && command.Routes is not { Count: > 0 };

                if (result.IsApplied && alreadyDrawn && diagramId == ActiveDiagramId)
                    _canvas?.AcceptRenderedDocument(result.Document);
                await AfterEditAsync(result).ConfigureAwait(true);
                break;

            case "connect" when request is { SourceRef: { } source, TargetRef: { } target }:
                var kind = request.ConnectorKind switch
                {
                    "messageFlow" => BpmnConnectorKind.MessageFlow,
                    "association" => BpmnConnectorKind.Association,
                    "dataAssociation" => BpmnConnectorKind.DataAssociation,
                    _ => BpmnConnectorKind.SequenceFlow,
                };

                await AfterEditAsync(history.Apply(new CreateConnectorCommand(
                    BpmnId.New(ConnectorPrefix(kind)),
                    kind,
                    source,
                    target,
                    request.Waypoints ?? [])
                { DiagramId = diagramId })).ConfigureAwait(true);
                break;

            case "delete" when request.ElementIds is { Count: > 0 } ids:
                await AfterEditAsync(history.Apply(new DeleteElementsCommand(ids))).ConfigureAwait(true);
                break;

            case "reconnect" when request is { ElementId: { } reconnected, SourceRef: { } newSource, TargetRef: { } newTarget }:
                await AfterEditAsync(history.Apply(new ReconnectConnectorCommand(
                    reconnected,
                    newSource,
                    newTarget,
                    request.Waypoints ?? [])
                { DiagramId = diagramId })).ConfigureAwait(true);
                break;

            case "reroute" when request is { ElementId: { } rerouted, Waypoints: { Count: > 0 } route }:
                // Geometry only, and the canvas has already drawn it, so the result is accepted
                // as rendered the way a move is.
                var reroute = history.Apply(new UpdateWaypointsCommand(rerouted, route) { DiagramId = diagramId });
                if (reroute.IsApplied && diagramId == ActiveDiagramId) _canvas?.AcceptRenderedDocument(reroute.Document);
                await AfterEditAsync(reroute).ConfigureAwait(true);
                break;

            case "reparent" when request is { ElementId: { } movedId, ContainerId: { } newOwner, Bounds: { } where }:
                /*
                 * Not accepted as already rendered. The canvas moved the element, but ownership,
                 * Lane membership and Pool scope are the document's to change — and a refusal has
                 * to put the element back where it came from, which only a re-render can do.
                 */
                await AfterEditAsync(history.Apply(
                    new ReparentElementCommand(movedId, newOwner, request.LaneId, where))).ConfigureAwait(true);
                break;

            case "expand" when request is { ElementId: { } opening }:
                /*
                 * Never accepted as already rendered: expanding puts shapes on the plane that the
                 * canvas has never drawn, and collapsing takes away some it has. Only a re-render
                 * is honest about either.
                 */
                await AfterEditAsync(history.Apply(
                    new ExpandSubProcessCommand(opening, RememberedSizeOf(opening), ActiveDiagramId))).ConfigureAwait(true);
                break;

            case "collapse" when request is { ElementId: { } closing }:
                RememberSizeOf(closing);

                await AfterEditAsync(history.Apply(
                    new CollapseSubProcessCommand(closing, HostDiagramId: ActiveDiagramId))).ConfigureAwait(true);
                break;

            case "resize" when request is { ElementId: { } resizedId, Bounds: { } bounds }:
                // The canvas reports the semantic element; the command names the DI shape, which is
                // what actually carries geometry (section 10.1).
                if (ShapeIdOf(resizedId, diagramId) is not { } shapeId)
                {
                    await OnEditRejected.InvokeAsync(new EditRejected("EDIT-011",
                        $"No shape draws '{resizedId}' on diagram '{diagramId}'.", resizedId))
                        .ConfigureAwait(true);
                    break;
                }

                /*
                 * Deliberately not accepted as already rendered, the way a move is. A move has
                 * happened on the canvas before it is reported, so re-rendering it would be
                 * wasted work; a resize has only drawn a preview, so marking the document as
                 * already drawn would leave the shape at its old size for ever.
                 */
                await AfterEditAsync(history.Apply(new ResizeShapeCommand(
                    shapeId,
                    bounds,
                    [.. (request.Routes ?? []).Select(route => new ConnectorRoute(route.ElementId, route.Waypoints))])
                {
                    DiagramId = diagramId,
                }))
                    .ConfigureAwait(true);
                break;

            case "laneSplitter" when request is { ElementId: { } upperLaneId, Position: { } position }:
                // Like a resize, only a preview has been drawn, so the answer must be re-rendered.
                await AfterEditAsync(history.Apply(new MoveLaneSplitterCommand(upperLaneId, position)))
                    .ConfigureAwait(true);
                break;

            case "cut" when request.ElementIds is { Count: > 0 } cutIds:
                CopyToClipboard(cutIds);

                // One history entry, labelled for what the user did rather than for the half of it
                // that touched the document.
                await AfterEditAsync(history.Apply(
                    new CompoundCommand([new DeleteElementsCommand(cutIds)], "Cut"))).ConfigureAwait(true);
                break;

            case "paste":
                await PasteAsync(history, _clipboard).ConfigureAwait(true);
                break;

            case "duplicate" when request.ElementIds is { Count: > 0 } duplicateIds:
                // Deliberately not through the clipboard. Duplicating something should not throw
                // away what the user copied ten seconds ago.
                await PasteAsync(history, Copy(duplicateIds), "Duplicate").ConfigureAwait(true);
                break;

            case "undo":
                await UndoAsync().ConfigureAwait(true);
                break;

            case "redo":
                await RedoAsync().ConfigureAwait(true);
                break;

            default:
                break;
        }
    }

    /// <summary>Whether an applied move changed more than the geometry the canvas already drew.</summary>
    /// <remarks>A grown container, a new Lane, or content and connectors moved to another Pool.</remarks>
    private static bool GrewAContainer(BpmnEditResult result) =>
        result.Diagnostics.Any(diagnostic => diagnostic.Code is "EDIT-053" or "EDIT-055" or "EDIT-056");

    /// <summary>The DI shape that draws an element, which is what carries its geometry.</summary>
    private string? ShapeIdOf(string elementId, string? diagramId) =>
        EditedDocument?.Diagrams
            .Where(diagram => diagramId is null || diagram.Id == diagramId)
            .SelectMany(diagram => diagram.Plane.Shapes)
            .FirstOrDefault(shape => shape.BpmnElement == elementId)?.Id;

    /// <summary>What is currently on the editor's clipboard.</summary>
    /// <remarks>
    /// Per editor, not per application. Two editors on one page are two documents, and a fragment
    /// copied from one carries container ids that mean nothing in the other.
    /// </remarks>
    public BpmnClipboardPayload Clipboard => _clipboard;

    /// <summary>Whether a paste would have anything to add.</summary>
    public bool CanPaste => !_clipboard.IsEmpty && !IsReadOnly;

    /// <summary>Puts a selection on the clipboard, replacing whatever was there.</summary>
    public void CopyToClipboard(IReadOnlyList<string> elementIds)
    {
        ArgumentNullException.ThrowIfNull(elementIds);

        var copied = Copy(elementIds);

        // An empty result means nothing in the selection was copyable — a Pool, a Lane, a bare
        // connector. Keeping the previous clipboard is friendlier than silently emptying it.
        if (!copied.IsEmpty)
        {
            _clipboard = copied;
            NotifyStateChanged();
        }
    }

    private BpmnClipboardPayload Copy(IReadOnlyList<string> elementIds) =>
        EditedDocument is null ? BpmnClipboardPayload.Empty : BpmnClipboard.Copy(EditedDocument, elementIds);

    /// <summary>
    /// Adds a fragment near where it was copied from, then selects what arrived.
    /// </summary>
    /// <remarks>
    /// Selecting the result is what makes a second paste land somewhere sensible rather than on
    /// top of the first, and it is what the user expects to be holding afterwards.
    /// </remarks>
    private async Task PasteAsync(BpmnEditHistory history, BpmnClipboardPayload payload, string? label = null)
    {
        if (payload.IsEmpty || PasteContainerId() is not { } containerId) return;

        var paste = new PasteCommand(
            payload,
            containerId,
            new Model.Di.DiPoint(PasteOffset, PasteOffset),
            PoolOf(containerId));

        var result = history.Apply(label is null ? paste : new CompoundCommand([paste], label));
        await AfterEditAsync(result).ConfigureAwait(true);

        if (result is { IsApplied: true, CreatedIds.Count: > 0 } && _canvas is not null)
        {
            await _canvas.SetSelectionAsync(result.CreatedIds).ConfigureAwait(true);
            _selection = result.CreatedIds;
            NotifyStateChanged();
        }
    }

    /// <summary>How far a paste starts from where it was copied, before any collision stepping.</summary>
    private const double PasteOffset = 20;

    /// <summary>
    /// The flow-elements container a paste lands in.
    /// </summary>
    /// <remarks>
    /// A Participant is not one: it references a Process, and that Process is what owns Flow
    /// Elements (section 2.6). Pasting into the Participant itself would produce nodes whose
    /// <c>containerId</c> names something that cannot hold them.
    /// </remarks>
    private string? PasteContainerId()
    {
        if (EditedDocument is not { } document) return null;

        var candidate = EffectiveContainerId;

        if (candidate is not null && document.Find(candidate) is BpmnParticipant participant)
        {
            return participant.ProcessRef ?? FirstProcessId();
        }

        return candidate ?? FirstProcessId();
    }

    /// <summary>The Pool that draws a container, so pasted roots keep the right Pool scope.</summary>
    private string? PoolOf(string containerId) =>
        EditedDocument?.Collaboration?.Participants
            .FirstOrDefault(participant => participant.ProcessRef == containerId)?.Id;

    private async Task AfterEditAsync(BpmnEditResult result)
    {
        foreach (var rejection in result.Rejections)
        {
            await OnEditRejected
                .InvokeAsync(new EditRejected(rejection.Code, rejection.Message, rejection.ElementId ?? string.Empty))
                .ConfigureAwait(true);
        }

        if (!result.IsApplied)
        {
            // A drag or a re-routed connector is shown on the canvas before the document hears of it. A refusal leaves the document as it was, so its
            // parameter does not change and nothing would put the shape back: draw it again.
            if (result.Rejections.Any() && _canvas is { } canvas) await canvas.ResyncAsync().ConfigureAwait(true);
            return;
        }

        await PublishAsync(result.Document).ConfigureAwait(true);
        NotifyStateChanged();
    }

    private async Task ReplayAsync(Func<BpmnEditHistory, BpmnDocument> step)
    {
        var history = EnsureHistory();
        if (history is null || IsReadOnly) return;

        await PushDocumentAsync(step(history)).ConfigureAwait(true);
    }

    /// <summary>
    /// Publishes a document the canvas is not already showing, and tells the host.
    /// </summary>
    /// <remarks>
    /// No call into the canvas: it loads from its <c>Document</c> parameter, so re-rendering with
    /// the new reference is what makes it reload. Calling in as well would load it twice.
    /// </remarks>
    private async Task PushDocumentAsync(BpmnDocument document)
    {
        await PublishAsync(document).ConfigureAwait(true);
        NotifyStateChanged();
    }

    /// <summary>Records what we produced, then tells the host about it.</summary>
    private Task PublishAsync(BpmnDocument document)
    {
        _lastPublished = document;

        // Before the host hears of it, so a host that re-renders on the event already hands back the
        // binding's document, and the echo is recognised as this editor's own edit.
        DocumentBinding?.Track(document);
        return OnDocumentChanged.InvokeAsync(document);
    }

    /// <summary>
    /// The history for the document currently open, created or reset as the parameter changes.
    /// </summary>
    /// <remarks>
    /// Keyed on the document the host supplied rather than on the edited one: a new document from
    /// the host is a new file, and offering to undo into the previous one would silently discard it.
    /// </remarks>
    private BpmnEditHistory? EnsureHistory()
    {
        if (Document is null)
        {
            _history?.UpdateOptions(HistoryOptions());
            return _history;
        }

        StartSessionIfNewDocument();

        // Still null only on the first edit of a document the editor has not been rendered with.
        _history ??= NewHistory();
        _history.UpdateOptions(HistoryOptions());

        return _history;
    }

    /// <summary>
    /// Starts a fresh edit session when the host supplies a document this editor has not seen.
    /// </summary>
    /// <remarks>
    /// Keyed on the document the host supplied rather than on the edited one: a new document from
    /// the host is a new file, and offering to undo into the previous one would silently discard it.
    /// Anything this editor published and the host echoed back is the same file, still mid-edit, and
    /// must not reset anything.
    /// </remarks>
    private void StartSessionIfNewDocument()
    {
        if (Document is null) return;
        if (ReferenceEquals(_lastPublished, Document))
        {
            _historySource = Document;
            return;
        }
        if (ReferenceEquals(_historySource, Document)) return;

        _history = NewHistory();
        ForgetExpansions();
        _path.Clear();
        _activeDiagramId = null;
        _accessibleCurrent = null;
        _selectionAnchor = null;
        _labelEdit = null;
        _pendingCall = null;
        _armedTool = null;
        _import = null;

        // The selection belonged to the file that is no longer open. Carrying ids across would
        // leave the panel describing elements of a document nobody is looking at.
        _selection = [];
        _selectionSnapshot = null;
        _menu = null;
        _menuSnapshot = null;
    }

    private BpmnEditHistory NewHistory()
    {
        _historySource = Document;
        _lastPublished = null;

        return new BpmnEditHistory(Document!, HistoryOptions());
    }

    /// <summary>
    /// The stencil for the container the user is creating into.
    /// </summary>
    /// <remarks>
    /// Recomputed on every render rather than cached: the available tools change the moment a
    /// Process is assigned to a Participant or the active container changes, and a stale palette
    /// offers creations the editor would then refuse.
    /// </remarks>
    public IReadOnlyList<BpmnStencilGroup> StencilGroups => BpmnStencilCuration.Apply(RuleStencilGroups, Stencil);

    /// <summary>
    /// How the editor is coloured.
    /// </summary>
    /// <remarks>
    /// Never null when read, so the markup does not have to ask twice: a host that says nothing
    /// gets the system's preference and ordinary contrast, which is what the stylesheet does on its
    /// own anyway.
    /// </remarks>
    internal ThemeOptions? Theming { get; set; }

    /// <summary>The theme in force, defaulted.</summary>
    public ThemeOptions Theme => Theming ?? ThemeOptions.Default;

    /// <summary>
    /// How the host curates the palette (sections 9.1 to 9.3).
    /// </summary>
    /// <remarks>
    /// Its own parameter rather than a section of <see cref="Configuration"/>, because that
    /// contract is the canvas's and the canvas never renders the stencil. Putting curation in it
    /// would send bytes nothing reads and, worse, make every relabelled tooltip advance the
    /// configuration revision and re-apply a configuration to the paper that had not changed.
    /// </remarks>
    internal BpmnStencilOptions? Stencil { get; set; }

    /// <summary>The rule-derived stencil groups for the effective container, before host curation.</summary>
    public IReadOnlyList<BpmnStencilGroup> RuleStencilGroups =>
        EditedDocument is null ? [] : BpmnStencil.GroupsFor(EditedDocument, EffectiveContainerId, ActiveDiagramId);

    /// <summary>
    /// The container the stencil is computed for.
    /// </summary>
    /// <remarks>
    /// A host that pins <see cref="ActiveContainerId"/> means it; otherwise the selection decides,
    /// falling back to the Process or embedded Sub-Process drawn by the active plane.
    /// Without this the palette on a Collaboration plane is permanently empty — section 9.1 is
    /// right that a Collaboration holds no Flow Nodes, but the answer is to create into a Pool,
    /// not to make collaboration diagrams uneditable.
    /// </remarks>
    public string? EffectiveContainerId => ActiveContainerId ?? ContainerFromSelection() ?? PlaneFlowContainerId(ActiveDiagramId);

    /// <summary>
    /// The container implied by a single selected element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Participant is itself the container to create into, and so is an expanded Sub-Process that
    /// owns content. Everything else stands for whatever holds it, so selecting a Task, a Lane, a
    /// Sequence Flow or a <em>collapsed</em> Sub-Process offers the palette of whatever contains it.
    /// </para>
    /// <para>
    /// A Lane matters here in particular. It covers most of a Pool's area, so on a lane diagram it
    /// is the easiest thing to click; leaving it unrecognised emptied the palette on exactly the
    /// diagrams where it is hardest to hit anything else. It resolves through its Lane Set, because
    /// a Lane classifies nodes without owning them (section 2.8) — its Process is the container.
    /// </para>
    /// <para>
    /// A multi-selection says nothing unambiguous, so it says nothing.
    /// </para>
    /// </remarks>
    private string? ContainerFromSelection()
    {
        if (_selection.Count != 1 || EditedDocument is not { } document) return null;

        return document.Find(_selection[0]) switch
        {
            BpmnParticipant participant => participant.Id,
            BpmnFlowNode node => BpmnStencil.IsOpenContainer(document, node, ActiveDiagramId) ? node.Id : node.ContainerId,
            BpmnLane lane => ContainerOfLaneSet(document, lane.LaneSetId),
            BpmnSequenceFlow flow => flow.ContainerId,
            BpmnAssociation association => association.ContainerId,
            BpmnTextAnnotation annotation => annotation.ContainerId,
            BpmnGroup group => group.ContainerId,
            BpmnDataObjectReference reference => reference.ContainerId,
            BpmnDataStoreReference reference => reference.ContainerId,
            _ => null,
        };
    }

    /// <summary>The Process or Sub-Process a Lane Set belongs to.</summary>
    private static string? ContainerOfLaneSet(BpmnDocument document, string laneSetId) =>
        document.LaneSets.FirstOrDefault(set => set.Id == laneSetId)?.ContainerId;

    /// <summary>
    /// The container new elements are created into, or <see langword="null"/> for the plane.
    /// </summary>
    internal string? ActiveContainerId { get; set; }

    /// <summary>
    /// Creates the armed stencil tool at a dropped position.
    /// </summary>
    /// <remarks>
    /// The drop position is resolved by the canvas and the element is created by the document
    /// editor, so a drop that collides is refused with DI-011 and leaves nothing behind — section
    /// 9.3's requirement that an invalid stencil drop produce no partial object.
    /// </remarks>
    /// <param name="entry">The tool that was armed.</param>
    /// <param name="clientX">Pointer X, in client coordinates.</param>
    /// <param name="clientY">Pointer Y, in client coordinates.</param>
    /// <returns>The outcome, or <see langword="null"/> if nothing could be resolved.</returns>
    public Task<BpmnEditResult?> CreateAtAsync(BpmnStencilEntry entry, double clientX, double clientY)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return CreateAsync(
            entry,
            canvas => canvas.ResolveDropAsync(clientX, clientY, DropTypeNameOf(entry)),
            isDrop: true);
    }

    /// <summary>
    /// Creates a stencil tool without a pointer position, for a tool armed from the keyboard.
    /// </summary>
    /// <remarks>
    /// The canvas picks the visible centre and steps clear of what is already there, so repeated
    /// presses do not stack elements on one spot and get refused by DI-011.
    /// </remarks>
    /// <param name="entry">The tool that was activated.</param>
    /// <returns>The outcome, or <see langword="null"/> if nothing could be resolved.</returns>
    public Task<BpmnEditResult?> CreateAsync(BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return CreateAsync(entry, canvas => canvas.ResolveCreationPointAsync(DropTypeNameOf(entry)));
    }

    /// <summary>
    /// The one creation path: ask the canvas where, then apply a single command.
    /// </summary>
    /// <remarks>
    /// Both entry points share this so a dropped tool and a keyboard-armed one cannot diverge, and
    /// so the transaction stays indivisible: the command builds the semantic object and its DI
    /// together, and a refusal leaves neither (section 9.3).
    /// </remarks>
    private async Task<BpmnEditResult?> CreateAsync(
        BpmnStencilEntry entry,
        Func<BpmnCanvas, ValueTask<DropTarget?>> locate,
        bool isDrop = false)
    {
        if (!entry.IsEnabled || _canvas is null || IsReadOnly) return null;

        var history = EnsureHistory();
        if (history is null) return null;

        var diagramId = ActiveDiagramId;
        var effectiveContainerId = EffectiveContainerId;
        var target = await locate(_canvas).ConfigureAwait(true);
        if (target is null) return null;

        /*
         * Where it landed beats where the selection was: the Pool under the pointer is a fact
         * about the new shape's geometry, and owning it to a different Process would leave the
         * DI and the semantics disagreeing. The active container only decides when the drop
         * landed nowhere in particular. The plane's own Process is the last resort, because a
         * Flow Node must belong to exactly one flow-elements container (section 9.3).
         */
        var containerId = target.ContainerId
            ?? ProcessForPool(target.PoolId)
            ?? effectiveContainerId
            ?? PlaneOwnerId(diagramId)
            ?? FirstProcessId();

        if (containerId is null) return null;

        /*
         * A Call Activity cannot be created from a drop alone: section 9.1 requires it to name the
         * definition it invokes, so the gesture opens a picker and the creation happens when that
         * is answered. The drop position is kept, so the element still lands where it was aimed.
         */
        if (entry.NeedsChoice)
        {
            _pendingCall = new PendingCallActivity(containerId, target.Bounds, target.PoolId, diagramId);
            NotifyStateChanged();
            return null;
        }

        var command = CommandFor(entry, containerId, target, isDrop, diagramId);

        if (command is null)
        {
            // Only a Boundary Event can fail to produce one, and only by landing on nothing.
            await OnEditRejected
                .InvokeAsync(new EditRejected(
                    "EV-001",
                    "A Boundary Event must be dropped on an Activity.",
                    string.Empty))
                .ConfigureAwait(true);

            return null;
        }

        if (command is CreateElementCommand create && entry.Template is { } template)
            command = BpmnTemplateStencil.CreateCommand(create, template, _options.Value.Templates.Registry);

        var result = history.Apply(command);

        await AfterEditAsync(result).ConfigureAwait(true);
        return result;
    }

    /// <summary>
    /// The command a stencil entry runs.
    /// </summary>
    /// <remarks>
    /// Three commands behind one palette, because the three things are not alike: an artifact is
    /// not a Flow Node, and a data reference has a definition behind it that is never drawn.
    /// </remarks>
    private BpmnEditCommand? CommandFor(
        BpmnStencilEntry entry,
        string containerId,
        DropTarget target,
        bool isDrop,
        string? diagramId) => entry.Kind
        switch
        {
            /*
             * A Pool is two commands. Section 9.3 keeps Process assignment separate from Pool
             * creation, and composing them here rather than hiding a Process inside the Participant
             * command leaves both visible — and makes one undo take back the pair, since a Process
             * nobody draws is not something to leave behind.
             */
            BpmnStencilKind.Participant => PoolCommand(target, diagramId),
            BpmnStencilKind.PoolOrLane => target.PoolId is { } poolId
                ? new CreateLaneCommand(BpmnId.New("Lane"), ProcessForPool(poolId) ?? containerId, "Lane") { DiagramId = diagramId }
                : PoolCommand(target, diagramId),

            // A Lane takes no bounds: it is a subdivision, and where it lands is whatever is left once
            // the stack it joins has been re-tiled (section 1.5.3).
            BpmnStencilKind.Lane => new CreateLaneCommand(BpmnId.New("Lane"), containerId, entry.Label) { DiagramId = diagramId },

            BpmnStencilKind.BoundaryEvent => BoundaryCommand(entry, containerId, target, diagramId),
            BpmnStencilKind.IntermediateBoundaryEvent => IntermediateBoundaryCommand(entry, containerId, target, isDrop, diagramId),

            BpmnStencilKind.Artifact => new CreateArtifactCommand(
                BpmnId.New(entry.Artifact == BpmnArtifactKind.TextAnnotation ? "TextAnnotation" : "Group"),
                entry.Artifact,
                containerId,
                target.Bounds,
                Text: entry.Artifact == BpmnArtifactKind.TextAnnotation ? entry.Label : null,
                Name: entry.Artifact == BpmnArtifactKind.Group ? entry.Label : null)
            { DiagramId = diagramId },

            BpmnStencilKind.Data => DataCommand(entry, containerId, target, diagramId),

            _ => new CreateElementCommand(
                BpmnId.New(WireNameOf(entry.Type)),
                entry.Type,
                containerId,
                target.Bounds,
                entry.Label,
                entry.EventDefinition,
                PoolId: target.PoolId)
            { DiagramId = diagramId },
        };

    /// <summary>
    /// Creates an Intermediate Event unless a dragged tool lands on an Activity, where it creates
    /// a Boundary Event attached to that Activity.
    /// </summary>
    private CreateElementCommand? IntermediateBoundaryCommand(
       BpmnStencilEntry entry,
       string containerId,
       DropTarget target,
       bool isDrop,
       string? diagramId)
    {
        if (EditedDocument is { } document
            && isDrop
            && BpmnBoundaryPlacement.Resolve(document, target.Bounds, requireInsideHost: true, diagramId: diagramId) is not null)
        {
            return BoundaryCommand(
                entry with
                {
                    Type = BpmnFlowNodeType.BoundaryEvent,
                    EventDefinition = BpmnEventDefinitionType.Message,
                    Kind = BpmnStencilKind.BoundaryEvent,
                },
                containerId,
                target,
                diagramId);
        }

        return new CreateElementCommand(
            BpmnId.New(WireNameOf(BpmnFlowNodeType.IntermediateThrowEvent)),
            BpmnFlowNodeType.IntermediateThrowEvent,
            containerId,
            target.Bounds,
            entry.Label,
            PoolId: target.PoolId)
        { DiagramId = diagramId };
    }

    /// <summary>
    /// A Pool, and the Process it draws.
    /// </summary>
    /// <remarks>
    /// Section 9.3 makes Process assignment a command of its own, so this is a compound rather
    /// than one command that quietly does both. A black-box Pool would also be legal, but it is
    /// not what someone dragging a Pool onto a Collaboration wants: nothing could be put inside it
    /// until a Process was assigned, and there is no surface for that yet.
    /// </remarks>
    private static CompoundCommand PoolCommand(DropTarget target, string? diagramId)
    {
        var processId = BpmnId.New("Process");

        return new CompoundCommand(
            [
                new CreateProcessCommand(processId),
                new CreateParticipantCommand(BpmnId.New("Participant"), target.Bounds, "Pool", processId) { DiagramId = diagramId },
            ],
            "Create Pool");
    }

    /// <summary>
    /// A Boundary Event on whatever Activity it was dropped on.
    /// </summary>
    /// <remarks>
    /// Null when the drop found no Activity. Section 9.3 forbids a free-floating Boundary Event, so
    /// there is nothing to create — the caller reports the refusal rather than attaching the Event
    /// to whichever shape happened to be nearest.
    /// </remarks>
    private CreateElementCommand? BoundaryCommand(BpmnStencilEntry entry, string containerId, DropTarget target, string? diagramId)
    {
        if (EditedDocument is not { } document) return null;

        var placement = BpmnBoundaryPlacement.Resolve(document, target.Bounds, diagramId: diagramId)
            ?? OnSelectedActivity(document, target, diagramId);
        if (placement is not { } resolved) return null;

        // The Event belongs to the container its host belongs to, not to whatever the pointer was
        // over: a Boundary Event on a Task inside a Sub-Process is owned by that Sub-Process.
        var host = document.FindFlowNode(resolved.HostId);

        return new CreateElementCommand(
            BpmnId.New(WireNameOf(entry.Type)),
            entry.Type,
            host?.ContainerId ?? containerId,
            resolved.Bounds,
            entry.Label,
            entry.EventDefinition,
            AttachedToRef: resolved.HostId,
            PoolId: host?.PoolId ?? target.PoolId)
        { DiagramId = diagramId };
    }

    /// <summary>
    /// The selected Activity, for a Boundary Event armed from the keyboard.
    /// </summary>
    /// <remarks>
    /// A keyboard user has no pointer to aim with, and the creation point every other tool falls
    /// back to — the middle of the view — is never on an Activity's border. Selecting the host
    /// first is the keyboard's way of saying which one, and exactly one selected Activity is an
    /// unambiguous answer. Anything else stays a refusal rather than a guess.
    /// </remarks>
    private (string HostId, Model.Di.Bounds Bounds)? OnSelectedActivity(BpmnDocument document, DropTarget target, string? diagramId)
    {
        if (_selection.Count != 1) return null;

        var host = document.FindFlowNode(_selection[0]);
        if (host is null || !BpmnBoundaryPlacement.IsAttachable(host.Type)) return null;

        var shape = document.Diagrams
            .Where(diagram => diagramId is null || diagram.Id == diagramId)
            .SelectMany(diagram => diagram.Plane.Shapes)
            .FirstOrDefault(candidate => candidate.BpmnElement == host.Id);

        if (shape is null) return null;

        // The middle of the bottom edge, which is where a Boundary Event is conventionally drawn
        // and where it is least likely to sit on top of an incoming or outgoing flow.
        var placed = target.Bounds with
        {
            X = shape.Bounds.X + (shape.Bounds.Width / 2) - (target.Bounds.Width / 2),
            Y = shape.Bounds.Y + shape.Bounds.Height - (target.Bounds.Height / 2),
        };

        return (host.Id, placed);
    }

    /// <summary>
    /// A data reference and a fresh definition for it.
    /// </summary>
    /// <remarks>
    /// A new definition every time. Section 9.1's other option — pointing at an existing one — is a
    /// choice the property panel will offer, because a palette entry cannot ask a question and
    /// silently reusing whatever definition happened to match would be the accidental duplication
    /// the specification warns about, in reverse.
    /// </remarks>
    private static CreateDataReferenceCommand DataCommand(BpmnStencilEntry entry, string containerId, DropTarget target, string? diagramId)
    {
        var prefix = entry.Data == BpmnDataKind.DataObject ? "DataObjectReference" : "DataStoreReference";
        var definitionPrefix = entry.Data == BpmnDataKind.DataObject ? "DataObject" : "DataStore";

        return new CreateDataReferenceCommand(
            BpmnId.New(prefix),
            entry.Data,
            containerId,
            target.Bounds,
            entry.Label,
            NewDefinitionId: BpmnId.New(definitionPrefix))
        { DiagramId = diagramId };
    }

    /// <summary>A Call Activity waiting for the user to say what it calls.</summary>
    /// <param name="ContainerId">Where it will be created.</param>
    /// <param name="Bounds">Where it was dropped.</param>
    /// <param name="PoolId">The Pool that will draw it.</param>
    /// <param name="DiagramId">The diagram open when creation was requested.</param>
    private sealed record PendingCallActivity(string ContainerId, Model.Di.Bounds Bounds, string? PoolId, string? DiagramId);

    private PendingCallActivity? _pendingCall;

    /// <summary>Whether the Call Activity picker is open.</summary>
    public bool IsPickingCalledElement => _pendingCall is not null;

    /// <summary>What a Call Activity may call in this document.</summary>
    public IReadOnlyList<(string Id, string? Name)> CallableDefinitions =>
        CachedCallableDefinitions();

    /// <summary>Abandons the pending Call Activity without creating anything.</summary>
    public void CancelCalledElement()
    {
        _pendingCall = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// Creates the pending Call Activity against the chosen definition.
    /// </summary>
    /// <param name="calledElementRef">The global Process or Global Task it will invoke.</param>
    /// <returns>The outcome, or <see langword="null"/> if nothing was pending.</returns>
    public async Task<BpmnEditResult?> ChooseCalledElementAsync(string calledElementRef)
    {
        var pending = _pendingCall;
        _pendingCall = null;
        NotifyStateChanged();

        if (pending is null || IsReadOnly || EnsureHistory() is not { } history) return null;

        var result = history.Apply(new CreateElementCommand(
            BpmnId.New("callActivity"),
            BpmnFlowNodeType.CallActivity,
            pending.ContainerId,
            pending.Bounds,
            "Call Activity",
            PoolId: pending.PoolId,
            CalledElementRef: calledElementRef)
        { DiagramId = pending.DiagramId });

        await AfterEditAsync(result).ConfigureAwait(true);
        return result;
    }

    /// <summary>The first Process, for legacy contexts without a diagram owner.</summary>
    private string? FirstProcessId() =>
        EditedDocument is { Processes.Count: > 0 } document ? document.Processes[0].Id : null;

    private string? PlaneOwnerId(string? diagramId) =>
        (diagramId is null
            ? EditedDocument is { Diagrams.Count: > 0 } document ? document.Diagrams[0] : null
            : EditedDocument?.Diagrams.FirstOrDefault(diagram => diagram.Id == diagramId))?.Plane.BpmnElement;

    private string? PlaneFlowContainerId(string? diagramId) =>
        PlaneOwnerId(diagramId) is { } owner
            && EditedDocument?.Find(owner) is BpmnProcess or BpmnFlowNode { FlowElementsOwned: true }
                ? owner : null;

    /// <summary>The Process a Pool references, so a drop inside a Pool lands in its Process.</summary>
    private string? ProcessForPool(string? poolId) =>
        poolId is null
            ? null
            : EditedDocument?.Collaboration?.Participants
                .FirstOrDefault(participant => participant.Id == poolId)?.ProcessRef;

    /// <summary>
    /// What to ask the canvas to size the drop as.
    /// </summary>
    /// <remarks>
    /// Not always the entry's flow node type. A Pool, a Lane, an artifact and a data reference are
    /// none of them Flow Nodes — their <c>Type</c> is a placeholder the command never reads — so
    /// asking by it returns the Task rectangle: a 100x80 Pool, or a 100x80 Data Object that the
    /// notation says is 36x50. The canvas sizes what it is told, so it has to be told the truth.
    /// </remarks>
    private static string DropTypeNameOf(BpmnStencilEntry entry) => entry.Kind switch
    {
        BpmnStencilKind.Participant => "participant",
        BpmnStencilKind.Lane => "lane",
        BpmnStencilKind.PoolOrLane => "poolOrLane",
        BpmnStencilKind.IntermediateBoundaryEvent => "intermediateThrowEvent",
        BpmnStencilKind.Artifact => entry.Artifact switch
        {
            BpmnArtifactKind.Group => "group",
            _ => "textAnnotation",
        },
        BpmnStencilKind.Data => entry.Data switch
        {
            BpmnDataKind.DataStore => "dataStoreReference",
            _ => "dataObjectReference",
        },
        _ => WireNameOf(entry.Type),
    };

    /// <summary>The wire name for a type, which is what the canvas and the icon manifest key by.</summary>
    private static string WireNameOf(BpmnFlowNodeType type) =>
        System.Text.Json.JsonSerializer.Serialize(type).Trim('"');

    private static string ConnectorPrefix(BpmnConnectorKind kind) => kind switch
    {
        BpmnConnectorKind.MessageFlow => "MessageFlow",
        BpmnConnectorKind.Association => "Association",
        BpmnConnectorKind.DataAssociation => "DataAssociation",
        _ => "Flow",
    };

    /// <summary>Applies a viewport command to the canvas.</summary>
    public ValueTask SetViewportAsync(ViewportCommand command) =>
        _canvas?.SetViewportAsync(command) ?? ValueTask.CompletedTask;

    /// <summary>Switches the active diagram.</summary>
    public ValueTask SetActiveDiagramAsync(string diagramId) =>
        _canvas?.SetActiveDiagramAsync(diagramId) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Handles a shortcut the canvas reported rather than executed.
    /// </summary>
    /// <remarks>
    /// Compared against the wire name rather than the enum: a newer canvas bundle may report a
    /// command this assembly does not know, and the right response is to ignore it and still
    /// forward it to the host, not to fail.
    /// </remarks>
    internal async Task HandleShortcutInvokedAsync(ShortcutInvoked payload)
    {
        if (string.Equals(payload.Command, "showShortcuts", StringComparison.Ordinal))
        {
            ShowShortcutHelp();
        }

        await OnShortcutInvoked.InvokeAsync(payload).ConfigureAwait(true);
    }

    /// <summary>
    /// Arms a stencil tool, so the next drop knows what to create.
    /// </summary>
    /// <remarks>
    /// The tool is remembered rather than travelling in the drag payload: HTML drag-and-drop can
    /// only carry strings, and round-tripping the entry through one would mean parsing back a
    /// type, a trigger and an enabled flag that are already in hand.
    /// </remarks>
    /// <param name="entry">The stencil tool to arm.</param>
    public void ArmTool(BpmnStencilEntry entry)
    {
        _armedTool = entry;
        NotifyStateChanged();
    }

    /// <summary>Forgets the armed stencil tool, for example when a drag ends without a drop.</summary>
    public void DisarmTool()
    {
        _armedTool = null;
        NotifyStateChanged();
    }

    /// <summary>Test seam: stands in for the canvas-backed creation of an activated tool.</summary>
    internal Func<BpmnStencilEntry, Task>? ActivateToolOverride { get; set; }

    /// <summary>Creates a tool the user activated from the keyboard, with no drop to wait for.</summary>
    /// <param name="entry">The stencil tool to create.</param>
    /// <returns>A task that completes once the element is created.</returns>
    public async Task ActivateToolAsync(BpmnStencilEntry entry)
    {
        _armedTool = null;
        NotifyStateChanged();
        if (ActivateToolOverride is { } observe)
        {
            await observe(entry).ConfigureAwait(true);
            return;
        }

        await CreateAsync(entry).ConfigureAwait(true);
    }

    internal async Task HandleDropAsync(DragEventArgs args)
    {
        var tool = _armedTool;
        _armedTool = null;
        NotifyStateChanged();

        if (tool is null) return;

        await CreateAtAsync(tool, args.ClientX, args.ClientY).ConfigureAwait(true);
    }
}
