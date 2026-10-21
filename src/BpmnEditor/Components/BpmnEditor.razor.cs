using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>The standard editor layout, composed over an editor scope and canvas.</summary>
public sealed partial class BpmnEditor : ComponentBase
{
    private BpmnEditorScope _scope = default!;

    /// <summary>The headless session owned by this editor's scope.</summary>
    public BpmnEditorSession Session => _scope.Session;

    /// <summary>Explicit configuration, overriding the DI-registered options.</summary>
    [Parameter]
    public EditorConfig? Configuration { get; set; }

    /// <summary>The document to display.</summary>
    [Parameter]
    public BpmnDocument? Document { get; set; }

    /// <summary>
    /// A binding to a stored document, instead of <see cref="Document"/>; see
    /// <see cref="BpmnEditorScope.DocumentBinding"/>.
    /// </summary>
    [Parameter]
    public Hasseware.AspNetCore.Components.Bpmn.Storage.BpmnDocumentBinding? DocumentBinding { get; set; }

    /// <summary>Which diagram to show; the first declared one when omitted.</summary>
    [Parameter]
    public string? DiagramId { get; set; }

    /// <summary>
    /// Whether the editor forbids mutation. Selection, inspection, navigation and export stay
    /// available; only mutating surfaces are disabled.
    /// </summary>
    [Parameter]
    public bool IsReadOnly { get; set; }

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
    [Parameter]
    public BpmnExecutionAnimation? ExecutionAnimation { get; set; }

    /// <summary>Additional CSS classes for the editor root.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Inline styles for the editor root.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Accessible name of the canvas region.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>
    /// Raised after an edit produced a new document.
    /// </summary>
    /// <remarks>
    /// The editor does not write back to the <c>Document</c> parameter. A host that wants to keep
    /// its own reference current handles this; one that does not can still read
    /// <see cref="EditedDocument"/>.
    /// </remarks>
    [Parameter]
    public EventCallback<BpmnDocument> OnDocumentChanged { get; set; }

    /// <summary>Raised (coalesced) whenever the canvas selection changes.</summary>
    [Parameter]
    public EventCallback<SelectionChanged> OnSelectionChanged { get; set; }

    /// <summary>Raised when an edit was refused, by either the canvas or the document editor.</summary>
    [Parameter]
    public EventCallback<EditRejected> OnEditRejected { get; set; }

    /// <summary>Accessible name of the whole editor region.</summary>
    [Parameter]
    public string? EditorLabel { get; set; }

    /// <summary>Accessible name of the stencil region.</summary>
    [Parameter]
    public string? StencilLabel { get; set; }

    /// <summary>Accessible name of the properties panel region.</summary>
    [Parameter]
    public string? PanelLabel { get; set; }

    /// <summary>Toolbar slot.</summary>
    [Parameter]
    public RenderFragment? ToolbarContent { get; set; }

    /// <summary>Footer slot, a full-width row below the canvas and panel; rendered inside the editor's scope.</summary>
    [Parameter]
    public RenderFragment? Footer { get; set; }

    /// <summary>Extra overlays rendered inside the canvas host, after the editor's own overlay parts.</summary>
    [Parameter]
    public RenderFragment? CanvasOverlays { get; set; }

    /// <summary>Extra dialogs rendered after the editor's own dialog parts.</summary>
    [Parameter]
    public RenderFragment? Dialogs { get; set; }

    [Inject]
    private IOptions<BpmnOptions> Options { get; set; } = default!;

    [Inject]
    private Microsoft.Extensions.Localization.IStringLocalizer<Resources.BpmnStrings> L { get; set; } = default!;

    private string EffectiveEditorLabel => EditorLabel ?? L["Editor.AriaLabel"];
    private string EffectiveStencilLabel => StencilLabel ?? L["Editor.StencilLabel"];
    private string EffectivePanelLabel => PanelLabel ?? L["Editor.PanelLabel"];

    private BpmnEditorComponentOptions Components => Options.Value.Components;
    private static BpmnKitDefaults KitDefaults => BpmnStandardDefaults.Kit;
    private bool HasToolbar => ToolbarContent is not null || Components.Get(BpmnEditorPart.Toolbar, KitDefaults) is not null;
    private bool HasPanel => PanelContent is not null || Components.Get(BpmnEditorPart.PropertyPanel, KitDefaults) is not null;

    /// <summary>Stencil slot.</summary>
    [Parameter]
    public RenderFragment? StencilContent { get; set; }

    /// <summary>Property panel slot.</summary>
    [Parameter]
    public RenderFragment? PanelContent { get; set; }

    /// <summary>Raised once the canvas is ready for calls.</summary>
    [Parameter]
    public EventCallback<CanvasInitialized> OnCanvasInitialized { get; set; }

    /// <summary>Raised after a document has been projected onto the canvas.</summary>
    [Parameter]
    public EventCallback<DocumentLoaded> OnDocumentLoaded { get; set; }

    /// <summary>Raised when the canvas refuses a configuration snapshot or patch.</summary>
    [Parameter]
    public EventCallback<ConfigRejected> OnConfigRejected { get; set; }

    /// <summary>Raised (coalesced) whenever the canvas transform changes.</summary>
    [Parameter]
    public EventCallback<ViewportChanged> OnViewportChanged { get; set; }

    /// <summary>
    /// Raised when a bound key fires a command the canvas does not handle itself.
    /// </summary>
    /// <remarks>
    /// Forwarded after the editor has acted on it, so a host can observe <c>showShortcuts</c>
    /// without having to reimplement the overlay, or handle a command a newer canvas reports that
    /// this assembly does not know.
    /// </remarks>
    [Parameter]
    public EventCallback<ShortcutInvoked> OnShortcutInvoked { get; set; }

    /// <summary>
    /// Whether the editor renders its own stencil when the host supplies no stencil content.
    /// </summary>
    /// <remarks>
    /// A host that fills <c>StencilContent</c> is taking over the palette, so the built-in one
    /// steps aside rather than rendering two.
    /// </remarks>
    [Parameter]
    public bool ShowStencil { get; set; }

    /// <summary>
    /// Whether the side panel slot is given its column. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The column is a fixed width, so a docked panel costs the canvas that width for as long as
    /// it is there — enough, on a small window, that a wide Pool no longer fits. A host that wants
    /// the panel on demand sets this rather than swapping <c>PanelContent</c> in and out, so the
    /// panel keeps its state while it is hidden.
    /// </remarks>
    [Parameter]
    public bool ShowPanel { get; set; } = true;

    /// <summary>
    /// How the editor is coloured.
    /// </summary>
    /// <remarks>
    /// Never null when read, so the markup does not have to ask twice: a host that says nothing
    /// gets the system's preference and ordinary contrast, which is what the stylesheet does on its
    /// own anyway.
    /// </remarks>
    [Parameter]
    public ThemeOptions? Theming { get; set; }

    /// <summary>
    /// How the host curates the palette (sections 9.1 to 9.3).
    /// </summary>
    /// <remarks>
    /// Its own parameter rather than a section of <see cref="Configuration"/>, because that
    /// contract is the canvas's and the canvas never renders the stencil. Putting curation in it
    /// would send bytes nothing reads and, worse, make every relabelled tooltip advance the
    /// configuration revision and re-apply a configuration to the paper that had not changed.
    /// </remarks>
    [Parameter]
    public BpmnStencilOptions? Stencil { get; set; }

    /// <summary>
    /// The container new elements are created into, or <see langword="null"/> for the plane.
    /// </summary>
    [Parameter]
    public string? ActiveContainerId { get; set; }

    /// <summary>
    /// Whether the parallel accessible tree is rendered.
    /// </summary>
    /// <remarks>
    /// On by default and offered as a parameter only for a host that has built its own equivalent.
    /// Turning it off leaves the diagram unreachable by keyboard, so it is not a decision to make
    /// casually — which is why the default is the other way.
    /// </remarks>
    [Parameter]
    public bool ShowAccessibleTree { get; set; } = true;

    /// <summary>Accessible name of the navigable tree.</summary>
    [Parameter]
    public string? AccessibleTreeLabel { get; set; }

    /// <summary>
    /// Raised when an import is accepted, with the document that replaced the current one.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OnDocumentChanged"/>, which reports edits. A host usually wants to
    /// know that the file on screen is a different file — to retitle a tab, to forget a path — and
    /// that is not the same event as a shape moving.
    /// </remarks>
    [Parameter]
    public EventCallback<BpmnImportPreview> OnImported { get; set; }

    /// <summary>Raised whenever the shown diagram changes, with the whole path to it.</summary>
    [Parameter]
    public EventCallback<BpmnDiagramNavigated> OnDiagramNavigated { get; set; }

    /// <summary>
    /// Whether the editor validates after every change.
    /// </summary>
    /// <remarks>
    /// On by default. Validation is linear in the document and runs in well under a frame even on
    /// the 602-shape fixture, so the cost of being always-right is lower than the cost of a stale
    /// panel. A host that validates on its own schedule turns this off and calls
    /// <see cref="ValidateAsync"/>.
    /// </remarks>
    [Parameter]
    public bool ValidateOnChange { get; set; } = true;

    /// <summary>Raised whenever a new report is produced.</summary>
    [Parameter]
    public EventCallback<BpmnValidationReport> OnValidated { get; set; }

    /// <summary>
    /// The document as edited, which is what the canvas renders.
    /// </summary>
    /// <remarks>
    /// Distinct from the <c>Document</c> parameter, which is what the host supplied. Edits produce
    /// new documents, and a host that wants to follow them binds <see cref="OnDocumentChanged"/>
    /// rather than having its own reference silently replaced.
    /// </remarks>
    public BpmnDocument? EditedDocument => Session.EditedDocument;

    /// <summary>Whether there is an edit to undo.</summary>
    public bool CanUndo => Session.CanUndo;

    /// <summary>Whether there is an edit to redo.</summary>
    public bool CanRedo => Session.CanRedo;

    /// <summary>Steps back one edit.</summary>
    public Task UndoAsync() => Session.UndoAsync();

    /// <summary>Steps forward one edit.</summary>
    public Task RedoAsync() => Session.RedoAsync();

    /// <summary>Applies an edit command, recording it in the history.</summary>
    /// <param name="command">The edit to apply.</param>
    /// <returns>The outcome, whose document is always the one now in effect.</returns>
    public Task<BpmnEditResult> ApplyAsync(BpmnEditCommand command) => Session.ApplyAsync(command);

    /// <summary>
    /// Opens the shortcut help overlay.
    /// </summary>
    /// <remarks>
    /// Public so a host's own toolbar button can open it, not only the bound key. The overlay is
    /// the editor's because it describes the editor's configuration, and a host that had to build
    /// its own would have to read the binding list and format it identically.
    /// </remarks>
    public void ShowShortcutHelp() => Session.ShowShortcutHelp();

    /// <summary>Closes the shortcut help overlay.</summary>
    public void HideShortcutHelp() => Session.HideShortcutHelp();

    /// <summary>What is currently on the editor's clipboard.</summary>
    /// <remarks>
    /// Per editor, not per application. Two editors on one page are two documents, and a fragment
    /// copied from one carries container ids that mean nothing in the other.
    /// </remarks>
    public BpmnClipboardPayload Clipboard => Session.Clipboard;

    /// <summary>Whether a paste would have anything to add.</summary>
    public bool CanPaste => Session.CanPaste;

    /// <summary>Puts a selection on the clipboard, replacing whatever was there.</summary>
    public void CopyToClipboard(IReadOnlyList<string> elementIds) => Session.CopyToClipboard(elementIds);

    /// <summary>
    /// The stencil for the container the user is creating into.
    /// </summary>
    /// <remarks>
    /// Recomputed on every render rather than cached: the available tools change the moment a
    /// Process is assigned to a Participant or the active container changes, and a stale palette
    /// offers creations the editor would then refuse.
    /// </remarks>
    public IReadOnlyList<BpmnStencilGroup> StencilGroups => Session.StencilGroups;

    /// <summary>
    /// The container the stencil is computed for.
    /// </summary>
    /// <remarks>
    /// A host that pins <see cref="ActiveContainerId"/> means it; otherwise the selection decides,
    /// because that is the only thing on screen that says which container the user is working in.
    /// Without this the palette on a Collaboration plane is permanently empty — section 9.1 is
    /// right that a Collaboration holds no Flow Nodes, but the answer is to create into a Pool,
    /// not to make collaboration diagrams uneditable.
    /// </remarks>
    public string? EffectiveContainerId => Session.EffectiveContainerId;

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
    public Task<BpmnEditResult?> CreateAtAsync(BpmnStencilEntry entry, double clientX, double clientY) => Session.CreateAtAsync(entry, clientX, clientY);

    /// <summary>
    /// Creates a stencil tool without a pointer position, for a tool armed from the keyboard.
    /// </summary>
    /// <remarks>
    /// The canvas picks the visible centre and steps clear of what is already there, so repeated
    /// presses do not stack elements on one spot and get refused by DI-011.
    /// </remarks>
    /// <param name="entry">The tool that was activated.</param>
    /// <returns>The outcome, or <see langword="null"/> if nothing could be resolved.</returns>
    public Task<BpmnEditResult?> CreateAsync(BpmnStencilEntry entry) => Session.CreateAsync(entry);

    /// <summary>Whether the Call Activity picker is open.</summary>
    public bool IsPickingCalledElement => Session.IsPickingCalledElement;

    /// <summary>What a Call Activity may call in this document.</summary>
    public IReadOnlyList<(string Id, string? Name)> CallableDefinitions => Session.CallableDefinitions;

    /// <summary>Abandons the pending Call Activity without creating anything.</summary>
    public void CancelCalledElement() => Session.CancelCalledElement();

    /// <summary>
    /// Creates the pending Call Activity against the chosen definition.
    /// </summary>
    /// <param name="calledElementRef">The global Process or Global Task it will invoke.</param>
    /// <returns>The outcome, or <see langword="null"/> if nothing was pending.</returns>
    public Task<BpmnEditResult?> ChooseCalledElementAsync(string calledElementRef) => Session.ChooseCalledElementAsync(calledElementRef);

    /// <summary>Applies a viewport command to the canvas.</summary>
    public ValueTask SetViewportAsync(ViewportCommand command) => Session.SetViewportAsync(command);

    /// <summary>Switches the active diagram.</summary>
    public ValueTask SetActiveDiagramAsync(string diagramId) => Session.SetActiveDiagramAsync(diagramId);

    /// <summary>The diagram as a navigable graph, in reading order.</summary>
    public IReadOnlyList<BpmnGraphNode> AccessibleGraph => Session.AccessibleGraph;

    /// <summary>The element the keyboard reader is on.</summary>
    /// <remarks>
    /// Follows the selection when exactly one element is selected, so a reader who clicks and a
    /// reader who arrows are looking at the same place. A multi-selection says nothing about where
    /// a cursor should be, so it leaves the cursor alone.
    /// </remarks>
    public string? AccessibleCurrent => Session.AccessibleCurrent;

    /// <summary>
    /// Moves the reader to an element: selects it and brings it into view.
    /// </summary>
    /// <remarks>
    /// Both, not either. Selecting alone leaves a sighted keyboard user watching an unchanged
    /// screen while the announcements move somewhere off-canvas, and scrolling alone loses the
    /// outline that says which element is being talked about.
    /// </remarks>
    /// <param name="elementId">The element to move to.</param>
    public Task GoToAccessibleAsync(string elementId) => Session.GoToAccessibleAsync(elementId);

    /// <summary>
    /// Activates the element the reader is on, which opens its label for editing.
    /// </summary>
    /// <remarks>
    /// Read-only keeps the reader's place and refuses the edit, in the same way every other
    /// mutation surface is closed rather than made to fail after the fact (section 7).
    /// </remarks>
    /// <param name="elementId">The element to activate.</param>
    public Task ActivateAccessibleAsync(string elementId) => Session.ActivateAccessibleAsync(elementId);

    /// <summary>The execution state currently drawn, or <see langword="null"/> when none is.</summary>
    public BpmnExecutionState? ExecutionState => Session.ExecutionState;

    /// <summary>
    /// Draws an engine's view of a running instance over the diagram.
    /// </summary>
    /// <remarks>
    /// A whole snapshot each time, not a delta. An overlay that accumulated patches would drift
    /// from the engine the first time one was lost, and a host that can describe a change can
    /// describe the state it produced.
    /// </remarks>
    /// <param name="state">The state to draw.</param>
    public Task SetExecutionStateAsync(BpmnExecutionState state) => Session.SetExecutionStateAsync(state);

    /// <summary>Removes every execution overlay.</summary>
    public Task ClearExecutionStateAsync() => Session.ClearExecutionStateAsync();

    /// <summary>
    /// Opens a Sub-Process, or closes it, whichever it is not.
    /// </summary>
    /// <remarks>
    /// The one place a toggle is the right shape. The commands are a pair on purpose — applying
    /// the same one twice has to give the same document — but a reader pressing a key is asking
    /// for the other state, not for a particular one, and only the editor knows which that is.
    /// </remarks>
    /// <param name="elementId">The Sub-Process to open or close.</param>
    /// <returns>A task that completes once the document has been redrawn.</returns>
    public Task ToggleExpansionAsync(string elementId) => Session.ToggleExpansionAsync(elementId);

    /// <summary>The import waiting for an answer, if any.</summary>
    public BpmnImportPreview? PendingImport => Session.PendingImport;

    /// <summary>Whether an import is waiting for an answer.</summary>
    public bool IsImporting => Session.IsImporting;

    /// <summary>
    /// Reads a file and offers what it contains, without changing anything.
    /// </summary>
    /// <remarks>
    /// BPMN XML and BPMN JSON are both accepted; the format is detected from the content, then
    /// from the file extension (see <see cref="Serialization.BpmnFileFormats.Detect"/>).
    /// </remarks>
    /// <param name="fileName">The file's name, for the offer.</param>
    /// <param name="xml">Its contents, as BPMN XML or BPMN JSON.</param>
    /// <returns>The preview, so a caller can inspect it as well as see it.</returns>
    public BpmnImportPreview PreviewImport(string fileName, string xml) => Session.PreviewImport(fileName, xml);

    /// <summary>
    /// Takes the offered import, replacing the document and everything that described the old one.
    /// </summary>
    /// <returns>A task that completes once the new document is on screen.</returns>
    public Task AcceptImportAsync() => Session.AcceptImportAsync();

    /// <summary>
    /// Declines the offered import.
    /// </summary>
    /// <remarks>
    /// Nothing to undo, because nothing was done: the parsed document is simply dropped.
    /// </remarks>
    public void CancelImport() => Session.CancelImport();

    /// <summary>Whether the custom context menu is open.</summary>
    public bool IsContextMenuOpen => Session.IsContextMenuOpen;

    /// <summary>The snapshot the menu is showing, or <see langword="null"/> for a canvas-level menu.</summary>
    public BpmnElementSnapshot? ContextMenuSnapshot => Session.ContextMenuSnapshot;

    /// <summary>Whether a label is being edited in place.</summary>
    public bool IsEditingLabel => Session.IsEditingLabel;

    /// <summary>
    /// Describes an element the way section 7 requires, against the document now in effect.
    /// </summary>
    /// <param name="elementId">The element to describe.</param>
    /// <returns>The snapshot, or <see langword="null"/> if no document holds that id.</returns>
    public BpmnElementSnapshot? Describe(string elementId) => Session.Describe(elementId);

    /// <summary>Closes the context menu.</summary>
    public void CloseContextMenu() => Session.CloseContextMenu();

    /// <summary>
    /// The selected elements, described for a property surface.
    /// </summary>
    /// <remarks>
    /// Built on demand from the current document rather than cached alongside the selection: an
    /// edit anywhere can change what a snapshot says, and a panel showing a snapshot taken when the
    /// element was selected is the stale standard field section 7 forbids.
    /// </remarks>
    public IReadOnlyList<BpmnElementSnapshot> SelectionSnapshots => Session.SelectionSnapshots;

    /// <summary>
    /// Every place the document names the selected element.
    /// </summary>
    /// <remarks>
    /// Only for a single selection. "What refers to this" is a question about one element, and the
    /// union across a selection would mix the answers together without saying which was whose.
    /// </remarks>
    public IReadOnlyList<BpmnReferenceSite> SelectionUsages => Session.SelectionUsages;

    /// <summary>
    /// Selects the element that holds a reference, so a usage can be followed.
    /// </summary>
    /// <param name="usage">The usage that was chosen.</param>
    /// <returns>A task that completes once the owner is selected.</returns>
    public Task GoToUsageAsync(BpmnReferenceSite usage) => Session.GoToUsageAsync(usage);

    /// <summary>The diagnostics raised against the current selection.</summary>
    public IReadOnlyList<Validation.BpmnDiagnostic> SelectionDiagnostics => Session.SelectionDiagnostics;

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
    public Task<BpmnEditResult?> ApplyPropertyCommandAsync(BpmnEditCommand command) => Session.ApplyPropertyCommandAsync(command);

    /// <summary>Closes the inline label editor without changing anything.</summary>
    public void CancelLabelEdit() => Session.CancelLabelEdit();

    /// <summary>
    /// The route from the root diagram to the one now shown.
    /// </summary>
    /// <remarks>
    /// For a host's breadcrumb. Always at least one step once a document is loaded.
    /// </remarks>
    public IReadOnlyList<BpmnDiagramStep> NavigationPath => Session.NavigationPath;

    /// <summary>Every diagram the document declares, for a picker.</summary>
    public IReadOnlyList<BpmnDiagramTarget> Diagrams => Session.Diagrams;

    /// <summary>The diagram the canvas is showing.</summary>
    public string? ActiveDiagramId => Session.ActiveDiagramId;

    /// <summary>
    /// Whether an element opens a diagram, and what to say when it does not.
    /// </summary>
    /// <param name="elementId">The element in question.</param>
    /// <returns>The target, or a refusal carrying the reason to show on a disabled control.</returns>
    public BpmnDrillTarget DrillTargetFor(string elementId) => Session.DrillTargetFor(elementId);

    /// <summary>
    /// Shows another of the document's diagrams.
    /// </summary>
    /// <remarks>
    /// Chosen from a list rather than drilled into, so the path restarts at that diagram: the user
    /// did not arrive through anything, and a breadcrumb claiming they did would offer a way back
    /// to a plane they were never on.
    /// </remarks>
    /// <param name="diagramId">The diagram to show.</param>
    /// <returns>A task that completes when the canvas has switched.</returns>
    public Task GoToDiagramAsync(string diagramId) => Session.GoToDiagramAsync(diagramId);

    /// <summary>
    /// Opens the diagram behind a Call Activity or a collapsed Sub-Process.
    /// </summary>
    /// <param name="elementId">The element to open.</param>
    /// <returns>The refusal when it could not be opened, so a caller can report it.</returns>
    public Task<BpmnDrillTarget> DrillIntoAsync(string elementId) => Session.DrillIntoAsync(elementId);

    /// <summary>
    /// Goes back to a diagram already on the path, dropping everything after it.
    /// </summary>
    /// <param name="diagramId">A diagram the path already contains.</param>
    /// <returns>A task that completes when the canvas has switched.</returns>
    public Task AscendToAsync(string diagramId) => Session.AscendToAsync(diagramId);

    /// <summary>
    /// Shows the diagram an element is drawn on, and selects it.
    /// </summary>
    /// <remarks>
    /// What a reference browser needs. A usage often names an element on another plane, and
    /// selecting it without going there first selects something nobody can see.
    /// </remarks>
    /// <param name="elementId">The element to reveal.</param>
    /// <returns>A task that completes once the element is shown and selected.</returns>
    public Task RevealAsync(string elementId) => Session.RevealAsync(elementId);

    /// <summary>
    /// The current findings.
    /// </summary>
    /// <remarks>
    /// Computed on demand and cached against the document instance. The model is immutable, so a
    /// document that is the same reference cannot have changed — which makes the cache exact rather
    /// than a guess, and means a panel may read this as often as it likes.
    /// </remarks>
    public BpmnValidationReport Validation => Session.Validation;

    /// <summary>
    /// Validates the document now and reports the result.
    /// </summary>
    /// <remarks>
    /// For a host that has turned <see cref="ValidateOnChange"/> off, and for a "validate now"
    /// command. Always re-runs: the caller asked.
    /// </remarks>
    /// <returns>The findings.</returns>
    public Task<BpmnValidationReport> ValidateAsync() => Session.ValidateAsync();

    /// <summary>
    /// Selects the element a finding is about.
    /// </summary>
    /// <remarks>
    /// The whole point of a finding carrying an element id. A diagnostic that named an element but
    /// could not take you to it would leave the user searching a diagram for the thing the editor
    /// already knows the location of.
    /// </remarks>
    /// <param name="diagnostic">The finding to go to.</param>
    /// <returns>A task that completes once the selection has been applied.</returns>
    public Task GoToAsync(BpmnDiagnostic diagnostic) => Session.GoToAsync(diagnostic);

    private ThemeOptions Theme => Theming ?? ThemeOptions.Default;
    private string? RootStyle => string.Concat(Style, Theme.Style) is { Length: > 0 } combined ? combined : null;
    private string? AnimationAttribute => Session.AnimationAttribute;
}
