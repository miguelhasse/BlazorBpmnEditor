using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Owns a headless editor session and cascades its immutable state without adding DOM.</summary>
public sealed partial class BpmnEditorScope : ComponentBase, IDisposable
{
    [Inject] private IOptions<BpmnOptions> Options { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;
    private EditorConfig _snapshot = EditorConfig.CreateDefault();

    /// <summary>The session owned by this scope, available after initialization.</summary>
    public BpmnEditorSession Session { get; private set; } = default!;

    /// <summary>The editor composition enclosed by this scope.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

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

    /// <summary>Explicit configuration, overriding the DI-registered options.</summary>
    [Parameter]
    public EditorConfig? Configuration { get; set; }

    /// <summary>The document to display.</summary>
    [Parameter]
    public BpmnDocument? Document { get; set; }

    /// <summary>
    /// A binding to a stored document, instead of <see cref="Document"/>: the editor shows the binding's
    /// document, reports every edit to it, and shows its external changes.
    /// </summary>
    /// <remarks>
    /// The host creates the binding through <see cref="Storage.IBpmnDocumentBindingFactory"/>, loads and
    /// saves through it, and disposes it. One binding serves one editor at a time; an editor given a binding
    /// another editor holds takes it over, and the other stops reporting to it. Setting both this and
    /// <see cref="Document"/> is an error.
    /// </remarks>
    [Parameter]
    public Storage.BpmnDocumentBinding? DocumentBinding { get; set; }

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
    /// <see cref="BpmnEditorSession.EditedDocument"/>.
    /// </remarks>
    [Parameter]
    public EventCallback<BpmnDocument> OnDocumentChanged { get; set; }

    /// <summary>Raised (coalesced) whenever the canvas selection changes.</summary>
    [Parameter]
    public EventCallback<SelectionChanged> OnSelectionChanged { get; set; }

    /// <summary>Raised when an edit was refused, by either the canvas or the document editor.</summary>
    [Parameter]
    public EventCallback<EditRejected> OnEditRejected { get; set; }

    /// <summary>The composition's own part and editor defaults, used when the host chose none.</summary>
    [Parameter]
    public BpmnKitDefaults? KitDefaults { get; set; }

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
    /// Whether the editor validates after every change.
    /// </summary>
    /// <remarks>
    /// On by default. Validation is linear in the document and runs in well under a frame even on
    /// the 602-shape fixture, so the cost of being always-right is lower than the cost of a stale
    /// panel. A host that validates on its own schedule turns this off and calls
    /// <see cref="BpmnEditorSession.ValidateAsync"/>.
    /// </remarks>
    [Parameter]
    public bool ValidateOnChange { get; set; } = true;

    /// <summary>Raised whenever a new report is produced.</summary>
    [Parameter]
    public EventCallback<BpmnValidationReport> OnValidated { get; set; }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Session = new BpmnEditorSession(Options.Value, Services);
        Session.StateChanged += HandleStateChanged;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        Session.KitDefaults = KitDefaults;
        Session.ShowAccessibleTree = ShowAccessibleTree;
        Session.AccessibleTreeLabel = AccessibleTreeLabel;
        Session.OnImported = OnImported;
        Session.OnDiagramNavigated = OnDiagramNavigated;
        Session.Configuration = Configuration;
        AttachBinding();
        Session.Document = DocumentBinding?.Document ?? Document;
        Session.DiagramId = DiagramId;
        Session.IsReadOnly = IsReadOnly;
        Session.ExecutionAnimation = ExecutionAnimation;
        Session.AriaLabel = AriaLabel ?? Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Resources.BpmnStrings>>()["Editor.CanvasLabel"];
        Session.OnDocumentChanged = OnDocumentChanged;
        Session.OnSelectionChanged = OnSelectionChanged;
        Session.OnEditRejected = OnEditRejected;
        Session.OnCanvasInitialized = OnCanvasInitialized;
        Session.OnDocumentLoaded = OnDocumentLoaded;
        Session.OnConfigRejected = OnConfigRejected;
        Session.OnViewportChanged = OnViewportChanged;
        Session.OnShortcutInvoked = OnShortcutInvoked;
        Session.Theming = Theming;
        Session.Stencil = Stencil;
        Session.ActiveContainerId = ActiveContainerId;
        Session.ValidateOnChange = ValidateOnChange;
        Session.OnValidated = OnValidated;
        var resolved = Resolve();
        // Advance only on configuration changes, never on an unrelated render.
        if (resolved with { Revision = _snapshot.Revision } != _snapshot)
            _snapshot = resolved with { Revision = _snapshot.Revision + 1 };
        Session.UpdateParameters(_snapshot);
    }

    private void HandleStateChanged(object? sender, EventArgs args) => _ = InvokeAsync(StateHasChanged);

    private void AttachBinding()
    {
        if (DocumentBinding is not null && Document is not null)
        {
            throw new InvalidOperationException($"Set either {nameof(Document)} or {nameof(DocumentBinding)} on {nameof(BpmnEditorScope)}, not both.");
        }

        var binding = DocumentBinding;
        if (ReferenceEquals(Session.DocumentBinding, binding)) return;

        DetachBinding();
        if (binding is null) return;

        // The most recently attached editor owns the binding. Blazor gives a replacement editor its
        // parameters before it disposes the one being replaced, so swapping one editor for another in a
        // single render must hand the binding over rather than fail.
        binding.Release?.Invoke();

        binding.Owner = this;
        binding.Release = DetachBinding;
        binding.Dispatcher = work => InvokeAsync(work);
        binding.IsBusy = () => Session.IsEditingLabel;
        binding.Changed += HandleBindingChanged;
        Session.DocumentBinding = binding;
    }

    private void DetachBinding()
    {
        if (Session.DocumentBinding is not { } binding) return;

        binding.Changed -= HandleBindingChanged;
        Session.DocumentBinding = null;

        // Leave the hooks alone if another editor has already taken the binding over.
        if (!ReferenceEquals(binding.Owner, this)) return;

        binding.Owner = null;
        binding.Release = null;
        binding.Dispatcher = null;
        binding.IsBusy = null;
    }

    // A load or reload replaces the document without the host re-rendering, so the session is told here.
    // An edit the session published itself is recognised as its own and does not reset history.
    private void HandleBindingChanged(object? sender, EventArgs args) => _ = InvokeAsync(() =>
    {
        if (Session.DocumentBinding is not { } binding) return;

        if (!ReferenceEquals(Session.Document, binding.Document))
        {
            Session.Document = binding.Document;
            Session.UpdateParameters(_snapshot);
        }

        Session.BindingChanged();
    });

    private BpmnSchemaChildOrder? _extensionOrder;

    private EditorConfig Resolve()
    {
        var extensionOrder = Options.Value.Extensions.Registry.Schemas.Count == 0
            ? null
            : _extensionOrder ??= new BpmnSchemaChildOrder(Options.Value.Extensions.Registry);
        if (Configuration is not null)
        {
            return Configuration with
            {
                ContractVersion = BpmnInteropContract.Version,
                IsReadOnly = IsReadOnly,
                Editing = Configuration.Editing with { ExtensionChildOrder = Configuration.Editing.ExtensionChildOrder ?? extensionOrder },
            };
        }

        var options = Options.Value;
        return new EditorConfig
        {
            ContractVersion = BpmnInteropContract.Version,
            Revision = _snapshot.Revision,
            IsReadOnly = IsReadOnly,
            Canvas = options.Canvas,
            Grid = options.Grid,
            Viewport = options.Viewport,
            Keyboard = options.Keyboard,
            Snapping = options.Snapping,
            Editing = options.Editing with { ExtensionChildOrder = options.Editing.ExtensionChildOrder ?? extensionOrder },
            Routing = options.Routing,
        };
    }

    /// <summary>Unsubscribes from session changes and detaches any document binding when the scope is removed.</summary>
    public void Dispose()
    {
        Session.StateChanged -= HandleStateChanged;
        DetachBinding();

        // An agent waiting on the user must not wait forever for an editor that is gone.
        Session.ResolveConfirmation(allowed: false);
    }
}
