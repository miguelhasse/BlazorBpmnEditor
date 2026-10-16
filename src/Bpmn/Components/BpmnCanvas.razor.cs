using System.Collections;
using System.Globalization;
using System.Resources;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Resources;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// Hosts the BPMN canvas and owns the whole JavaScript interop boundary.
/// </summary>
/// <remarks>
/// <para>
/// This is the only type in the library that imports the canvas module, holds the resulting
/// <see cref="IJSObjectReference"/>, or creates and disposes the
/// <see cref="DotNetObjectReference{T}"/> for <see cref="BpmnCanvasEventBridge"/>. Every other
/// component talks to the canvas through this component's .NET API — methods in, events out
/// (implementation plan section 5). One owner means one lifetime to get right, and an
/// architecture test enforces it rather than leaving it to review.
/// </para>
/// <para>
/// Blazor never manipulates the SVG. The canvas host below is an empty element that JointJS owns
/// from the moment it is handed over, which keeps the renderer's DOM out of reach of the Blazor
/// diff.
/// </para>
/// </remarks>
public sealed partial class BpmnCanvas : ComponentBase, IAsyncDisposable
{
    private static readonly ResourceManager CanvasResources = new(typeof(BpmnCanvasStrings));

    /// <summary>The resource keys, which are the invariant resource set's and never change, read once.</summary>
    private static readonly Lazy<string[]> CanvasResourceKeys = new(() =>
        (CanvasResources.GetResourceSet(CultureInfo.InvariantCulture, true, true)
            ?? throw new MissingManifestResourceException("Canvas display resources are missing."))
        .Cast<DictionaryEntry>()
        .Select(entry => (string)entry.Key)
        .ToArray());

    /// <summary>The localized strings for <see cref="_localizedStringsCulture"/>, built once per culture for this component.</summary>
    private CanvasStrings? _localizedStrings;

    private string? _localizedStringsCulture;

    private readonly CancellationTokenSource _disposalCts = new();

    /// <summary>
    /// Lets one pass at a time reconcile the canvas with the parameters.
    /// </summary>
    /// <remarks>
    /// Every render reaches <see cref="OnAfterRenderAsync"/>, and a pass awaits the browser. A second pass that starts meanwhile would see the document
    /// not yet recorded as drawn and send it again, so passes wait for each other and the one that follows judges what the one before left.
    /// </remarks>
    private readonly SemaphoreSlim _synchronization = new(1, 1);

    private bool _disposed;

    private ElementReference _hostElement;

    /// <summary>What overlays rendered as this canvas's child content are told about it.</summary>
    private BpmnCanvasSurface? _surface;

    /// <summary>The host element the overlays are positioned against.</summary>
    internal ElementReference HostElement => _hostElement;

    private BpmnCanvasSurface Surface => _surface ??= new BpmnCanvasSurface(this);

    /// <summary>
    /// The element JointJS mounts its paper into.
    /// </summary>
    /// <remarks>
    /// Separate from the host because Blazor and JointJS cannot both manage one element's
    /// children: the renderer tracks its own logical positions, and an <c>svg</c> appended among
    /// them makes it write attributes onto the wrong node. The host stays the renderer's, this
    /// one is the library's, and the overlays render beside it rather than into it.
    /// </remarks>
    private ElementReference _paperElement;
    private IJSObjectReference? _module;
    private BpmnCanvasEventBridge? _bridge;
    private DotNetObjectReference<BpmnCanvasEventBridge>? _bridgeReference;

    /// <summary>The live canvas, held as a reference so calls go straight to it.</summary>
    private IJSObjectReference? _instance;

    /// <summary>An initialization reported before its instance was recorded, raised once it is.</summary>
    private CanvasInitialized? _pendingInitialized;

    /// <summary>The configuration the live instance was built with.</summary>
    private EditorConfig? _appliedConfig;

    /// <summary>The last view the canvas reported, so a rebuilt one can be put back where it was.</summary>
    private ViewportChanged? _viewport;

    /// <summary>The last selection it reported, for the same reason.</summary>
    private IReadOnlyList<string> _selected = [];
    private BpmnDocument? _renderedDocument;

    /// <summary>
    /// The <see cref="DiagramId"/> value the canvas last rendered for.
    /// </summary>
    /// <remarks>
    /// Deliberately the requested id, not the one the module resolved. Comparing against the
    /// resolved id makes a null request (meaning "the first diagram") differ from its own result
    /// forever, and every render then reloads the document — a loop that ends in a crashed tab.
    /// </remarks>
    private string? _renderedDiagramId;

    [Inject]
    private IJSRuntime JS { get; init; } = default!;

    [Inject]
    private IServiceProvider Services { get; init; } = default!;

    [Inject]
    private IStringLocalizer<BpmnCanvasStrings> Strings { get; init; } = default!;

    /// <summary>The configuration snapshot to initialise with. Required before first render.</summary>
    [Parameter]
    public EditorConfig? Configuration { get; set; }

    /// <summary>The document to render. Rendering is skipped until the canvas is initialised.</summary>
    [Parameter]
    public BpmnDocument? Document { get; set; }

    /// <summary>Which diagram to show; the first declared one when omitted.</summary>
    [Parameter]
    public string? DiagramId { get; set; }

    /// <summary>Additional CSS classes for the canvas host.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Inline styles for the canvas host.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Accessible name of the canvas region; localized default when omitted.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>Overlay content rendered above the canvas, inside its host element.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

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
    /// Panning and zooming never reach here — the canvas does those directly, because routing a
    /// key press through interop and back would make the canvas lag the keyboard. Only commands
    /// owned by the chrome, such as opening the shortcut help, are reported.
    /// </remarks>
    [Parameter]
    public EventCallback<ShortcutInvoked> OnShortcutInvoked { get; set; }

    /// <summary>Raised (coalesced) whenever the selection changes.</summary>
    [Parameter]
    public EventCallback<SelectionChanged> OnSelectionChanged { get; set; }

    /// <summary>
    /// Raised when the one selected element moved on screen while the selection stayed the same.
    /// </summary>
    /// <remarks>
    /// For chrome drawn beside the element. The anchor is <see langword="null"/> while a gesture is
    /// moving it, and arrives again once the gesture has ended and the view has settled.
    /// </remarks>
    [Parameter]
    public EventCallback<SelectionAnchorChanged> OnSelectionAnchorChanged { get; set; }

    /// <summary>
    /// Raised when the canvas asks for an edit to be applied.
    /// </summary>
    /// <remarks>
    /// The canvas cannot change the document, so every edit arrives as a request. Whoever owns the
    /// document decides — which is what makes the model, not the SVG, the source of truth.
    /// </remarks>
    [Parameter]
    public EventCallback<EditRequest> OnEditRequested { get; set; }

    /// <summary>Raised when the canvas refused a gesture and rolled it back itself.</summary>
    [Parameter]
    public EventCallback<EditRejected> OnEditRejected { get; set; }

    /// <summary>
    /// Raised when a context menu was asked for anywhere inside the canvas.
    /// </summary>
    /// <remarks>
    /// The browser's own menu has already been suppressed by the time this fires, in both Edit and
    /// Readonly modes (section 7). A handler that does nothing therefore leaves the user with a
    /// right-click that produced no menu at all, which is why the specification requires the
    /// surface to open <em>something</em> — a canvas-level menu with inspection commands, if
    /// nothing else applies.
    /// </remarks>
    [Parameter]
    public EventCallback<ContextMenuRequested> OnContextMenuRequested { get; set; }

    /// <summary>Raised when a label should be edited in place (section 4.5).</summary>
    [Parameter]
    public EventCallback<LabelEditRequested> OnLabelEditRequested { get; set; }

    /// <summary>Raised when a BPMN file is dropped on the canvas.</summary>
    [Parameter]
    public EventCallback<FileDropped> OnFileDropped { get; set; }

    /// <summary>
    /// Raised while something is dragged over the canvas.
    /// </summary>
    /// <remarks>
    /// The default is prevented in markup regardless of whether a host handles this, because a
    /// browser that does not see preventDefault on dragover never raises a drop at all.
    /// </remarks>
    [Parameter]
    public EventCallback<DragEventArgs> OnDragOver { get; set; }

    /// <summary>Raised when something is dropped on the canvas.</summary>
    [Parameter]
    public EventCallback<DragEventArgs> OnDrop { get; set; }

    /// <summary>Attributes splatted onto the canvas host element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object?>? AdditionalAttributes { get; set; }

    /// <summary>True once the module has accepted the configuration and created the canvas.</summary>
    public bool IsInitialized => _instance is not null;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            await _synchronization.WaitAsync(_disposalCts.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && _disposed)
        {
            return;
        }

        try
        {
            if (firstRender)
            {
                await InitializeAsync().ConfigureAwait(true);

                // The parameters may have changed while the canvas was starting, and nothing renders them again for it.
                await SynchronizeDocumentAsync().ConfigureAwait(true);
                return;
            }

            // A configuration is applied when the instance is built and never afterwards, so a host
            // that changes one has to be given a new instance. Checked before the document, because
            // rebuilding loads it anyway and doing both would render twice.
            if (await SynchronizeConfigurationAsync().ConfigureAwait(true))
            {
                await SynchronizeDocumentAsync().ConfigureAwait(true);
                return;
            }

            // The document parameter can change before the module finishes loading, so the render
            // is reconciled here rather than in OnParametersSetAsync.
            await SynchronizeDocumentAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && _disposed)
        {
            // Disposed while a call was in flight: the work it was doing is the work disposal does.
        }
        finally
        {
            if (!_disposed) _synchronization.Release();
        }
    }

    /// <summary>Applies a viewport command. Ignored before initialisation.</summary>
    public async ValueTask SetViewportAsync(ViewportCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_instance is null || _disposed) return;

        await InvokeSafelyAsync(async () =>
            await _instance.InvokeVoidAsync("setViewport", _disposalCts.Token, command)
                .ConfigureAwait(false)).ConfigureAwait(true);
    }

    /// <summary>Switches the active diagram, keeping the loaded document resident.</summary>
    public async ValueTask SetActiveDiagramAsync(string diagramId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagramId);
        if (_instance is null || _disposed) return;

        var loaded = await _instance
            .InvokeAsync<DocumentLoaded?>("setActiveDiagram", _disposalCts.Token, diagramId)
            .ConfigureAwait(true);

        if (loaded is null) return;

        _renderedDiagramId = diagramId;
        await OnDocumentLoaded.InvokeAsync(loaded).ConfigureAwait(true);
    }

    private async Task InitializeAsync()
    {
        var config = Configuration ?? EditorConfig.CreateDefault();

        // Validate before crossing the boundary: a host's own mistake should surface as a typed
        // failure here, not as a rejection event arriving later from the browser.
        var rejection = EditorConfigValidator.Validate(config);
        if (rejection is not null)
        {
            await OnConfigRejected.InvokeAsync(rejection).ConfigureAwait(true);
            return;
        }

        // The module import and the localized strings are independent: start the import, build the strings while it is in flight,
        // then wait. On Blazor Server the import is a network round trip and the strings are the work that used to follow it.
        var importing = JS
            .InvokeAsync<IJSObjectReference>("import", _disposalCts.Token, BpmnInteropContract.ModulePath)
            .AsTask();
        _ = LocalizedStrings();
        var module = await importing.ConfigureAwait(true);
        if (_disposed)
        {
            // Disposed while the module was loading: disposal found nothing to release, so it is released here.
            await InvokeSafelyAsync(async () => await module.DisposeAsync().ConfigureAwait(true)).ConfigureAwait(true);
            return;
        }

        _module = module;

        _bridge = new BpmnCanvasEventBridge();
        _bridge.InitializedReceived += HandleInitialized;
        _bridge.ConfigAppliedReceived += HandleConfigApplied;
        _bridge.ConfigRejectedReceived += HandleConfigRejected;
        _bridge.DocumentLoadedReceived += HandleDocumentLoaded;
        _bridge.ViewportChangedReceived += HandleViewportChanged;
        _bridge.ShortcutInvokedReceived += HandleShortcutInvoked;
        _bridge.SelectionChangedReceived += HandleSelectionChanged;
        _bridge.SelectionAnchorChangedReceived += HandleSelectionAnchorChanged;
        _bridge.EditRequestedReceived += HandleEditRequested;
        _bridge.EditRejectedReceived += HandleEditRejected;
        _bridge.ContextMenuRequestedReceived += HandleContextMenuRequested;
        _bridge.LabelEditRequestedReceived += HandleLabelEditRequested;
        _bridge.FileDroppedReceived += HandleFileDropped;

        _bridgeReference = DotNetObjectReference.Create(_bridge);

        await StartInstanceAsync(config).ConfigureAwait(true);
    }

    /// <summary>
    /// The canvas's display strings in the current UI culture, built once per culture for this component.
    /// </summary>
    /// <remarks>
    /// Per component and not static, because the injected localizer can be replaced by the host; keyed on the culture so that a
    /// culture switch rebuilds them. The same instance is returned until then, so comparing two configurations that carry it is a
    /// reference check and not a walk over every string.
    /// </remarks>
    private CanvasStrings LocalizedStrings()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        if (_localizedStrings is not null && _localizedStringsCulture == culture) return _localizedStrings;

        var keys = CanvasResourceKeys.Value;
        var values = new Dictionary<string, string>(keys.Length, StringComparer.Ordinal);
        foreach (var key in keys) values[key] = Strings[key].Value;

        _localizedStringsCulture = culture;
        return _localizedStrings = CanvasStrings.Adopt(values);
    }

    /// <summary>Builds a canvas instance from a configuration, and records what it was built with.</summary>
    private async Task StartInstanceAsync(EditorConfig config)
    {
        if (_module is null || _bridgeReference is null) return;

        var localizedConfig = config with { Strings = LocalizedStrings() };

        // What is sent is what is recorded as drawn: the parameters may change while the browser works, and the next pass has to see that.
        var sentDocument = Document;
        var sentDiagramId = DiagramId;

        var instance = await _module
            .InvokeAsync<IJSObjectReference>(
                "initialize",
                _disposalCts.Token,
                _hostElement,
                _paperElement,
                CanvasDocumentPayload.For(sentDocument),
                localizedConfig,
                _bridgeReference)
            .ConfigureAwait(true);

        CanvasInitResult result;
        try
        {
            result = await instance
                .InvokeAsync<CanvasInitResult>("getInitResult", _disposalCts.Token)
                .ConfigureAwait(true);
        }
        catch
        {
            // The canvas exists, with listeners on the window and a paper, and no reference to it is recorded yet: disposal cannot find it.
            await ReleaseInstanceAsync(instance).ConfigureAwait(true);
            throw;
        }

        if (_disposed)
        {
            await ReleaseInstanceAsync(instance).ConfigureAwait(true);
            return;
        }

        if (result.ContractVersion != BpmnInteropContract.Version || !result.Accepted)
        {
            _pendingInitialized = null;
            await InvokeSafelyAsync(async () => await instance.DisposeAsync().ConfigureAwait(true))
                .ConfigureAwait(true);

            if (result.ContractVersion != BpmnInteropContract.Version)
            {
                throw new BpmnInteropVersionException(BpmnInteropContract.Version, result.ContractVersion);
            }

            // The module already raised ConfigRejected through the bridge; nothing was built.
            return;
        }

        _instance = instance;
        _appliedConfig = config;
        _renderedDocument = sentDocument;
        _renderedDiagramId = sentDiagramId;

        if (_pendingInitialized is { } pending)
        {
            _pendingInitialized = null;
            await RaiseInitializedAsync(pending).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Rebuilds the canvas when the host has changed its configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A configuration is read once, when the instance is built: the paper's interactivity, the
    /// snapping, the grid and every controller that only exists in edit mode are all decided then.
    /// Nothing re-applies one to a live instance, so a host that turned <c>IsReadOnly</c> on got an
    /// editor that refused mutations while its canvas still offered the gestures that make them.
    /// </para>
    /// <para>
    /// Rebuilt rather than reconfigured. Turning read-only on and off again has to leave a canvas
    /// that behaves exactly like one built that way, and the way to be sure of that is to build one
    /// — the alternative is teaching six controllers to be switched off and back on, each of them a
    /// chance for a mode to be half-applied.
    /// </para>
    /// <para>
    /// What the reader had is handed back: the same view and the same selection. A setting changing
    /// is not a reason to dump someone at the origin.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> when a rebuild happened, which also reloads the document.</returns>
    private async Task<bool> SynchronizeConfigurationAsync()
    {
        if (_instance is null || _appliedConfig is null) return false;

        var config = Configuration ?? EditorConfig.CreateDefault();
        if (config == _appliedConfig) return false;

        if (EditorConfigValidator.Validate(config) is { } rejection)
        {
            // Refused, and the instance that is already running is left alone: a host's mistake
            // should not take away the canvas they had.
            await OnConfigRejected.InvokeAsync(rejection).ConfigureAwait(true);
            _appliedConfig = config;
            return false;
        }

        var selected = _selected;
        var previous = _instance;

        _instance = null;
        await ReleaseInstanceAsync(previous).ConfigureAwait(true);

        // Read after the release: releasing makes the canvas report the last view it had, which it only holds back for a moment while a gesture is moving it.
        var view = _viewport;

        await StartInstanceAsync(config).ConfigureAwait(true);

        if (_instance is null || _disposed) return true;

        await RestoreAsync(view, selected).ConfigureAwait(true);
        return true;
    }

    /// <summary>Puts a rebuilt canvas back where the reader left it.</summary>
    private async Task RestoreAsync(ViewportChanged? view, IReadOnlyList<string> selected)
    {
        if (view is not null)
        {
            await SetViewportAsync(
                new ViewportCommand("setTransform", view.Zoom, view.TranslateX, view.TranslateY))
                .ConfigureAwait(true);
        }

        if (selected.Count > 0) await SetSelectionAsync(selected).ConfigureAwait(true);
    }

    /// <summary>
    /// Records that this document reference is already on screen, without re-rendering.
    /// </summary>
    /// <remarks>
    /// A move is applied on the canvas first and recorded in the model afterwards, so by the time
    /// the new document arrives as a parameter the paper is already showing it. Without this the
    /// reference comparison would see a change and reload the whole diagram mid-gesture, undoing
    /// the very responsiveness that applying it optimistically bought.
    /// </remarks>
    /// <param name="document">The document the canvas is already displaying.</param>
    internal void AcceptRenderedDocument(BpmnDocument document) => _renderedDocument = document;

    /// <summary>
    /// Applies a host-confirmed deletion directly to the rendered graph when its semantics need no
    /// layout changes.
    /// </summary>
    /// <param name="document">The document the graph will represent after deletion.</param>
    /// <param name="elementIds">Every semantic element removed by the delete.</param>
    /// <returns>Whether the live canvas applied the deletion.</returns>
    internal async ValueTask<bool> DeleteElementsAsync(
        BpmnDocument expectedDocument,
        BpmnDocument document,
        IReadOnlyCollection<string> elementIds)
    {
        ArgumentNullException.ThrowIfNull(expectedDocument);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(elementIds);
        if (elementIds.Count == 0 || _instance is null || _disposed) return false;

        try
        {
            await _synchronization.WaitAsync(_disposalCts.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && _disposed)
        {
            return false;
        }

        try
        {
            var instance = _instance;
            if (instance is null || _disposed
                || !ReferenceEquals(_renderedDocument, expectedDocument)
                || !ReferenceEquals(Document, expectedDocument)
                || _renderedDiagramId != DiagramId)
            {
                return false;
            }

            if (!await instance.InvokeAsync<bool>("deleteElements", _disposalCts.Token, elementIds).ConfigureAwait(true))
                return false;

            _renderedDocument = document;
            _renderedDiagramId = DiagramId;
            return true;
        }
        finally
        {
            if (!_disposed) _synchronization.Release();
        }
    }

    /// <summary>
    /// Draws the current document again, for a canvas that was changed ahead of the document and then refused.
    /// </summary>
    /// <remarks>
    /// A move or a re-routed connector is applied on the canvas first. When the document refuses it the parameter does not change, so nothing
    /// would ever put the shape back; this forgets what was recorded as drawn and reconciles.
    /// </remarks>
    /// <returns>A task that completes when the canvas shows the document again.</returns>
    internal async Task ResyncAsync()
    {
        if (_disposed || _instance is null) return;

        try
        {
            await _synchronization.WaitAsync(_disposalCts.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && _disposed)
        {
            return;
        }

        try
        {
            _renderedDocument = null;
            await SynchronizeDocumentAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && _disposed)
        {
        }
        finally
        {
            if (!_disposed) _synchronization.Release();
        }
    }

    private async Task SynchronizeDocumentAsync()
    {
        if (_instance is null || _disposed) return;
        if (ReferenceEquals(_renderedDocument, Document) && _renderedDiagramId == DiagramId) return;
        if (Document is null) return;

        var sentDocument = Document;
        var sentDiagramId = DiagramId;
        var loaded = await _instance
            .InvokeAsync<DocumentLoaded?>("loadDocument", _disposalCts.Token, CanvasDocumentPayload.For(sentDocument), sentDiagramId)
            .ConfigureAwait(true);

        // Recorded as what was sent, not as whatever the parameters are by now: a host that moved on while the browser worked is loaded again by the next pass.
        _renderedDocument = sentDocument;
        _renderedDiagramId = sentDiagramId;

        if (loaded is not null)
        {
            await OnDocumentLoaded.InvokeAsync(loaded).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Raises <see cref="OnCanvasInitialized"/> once the instance it describes is recorded.
    /// </summary>
    /// <remarks>
    /// The module reports initialization before <c>initialize</c> returns, so a handler that
    /// calls back into the canvas (re-pushing an execution overlay, say) would otherwise find no
    /// instance and be silently dropped.
    /// </remarks>
    private void HandleInitialized(CanvasInitialized payload) =>
        Dispatch(async () =>
        {
            if (_instance is null)
            {
                _pendingInitialized = payload;
                return;
            }

            await RaiseInitializedAsync(payload).ConfigureAwait(true);
        });

    private async Task RaiseInitializedAsync(CanvasInitialized payload)
    {
        await OnCanvasInitialized.InvokeAsync(payload).ConfigureAwait(true);
        StateHasChanged();
    }

    // The callbacks come from the browser with nobody awaiting them. A host handler that throws used to leave a faulted task that nothing ever looked at, so the
    // failure vanished (or surfaced later as an unobserved task exception); it is logged, and a callback that arrives after disposal is not worth reporting.
    private void Dispatch(Func<Task> work) => _ = DispatchAsync(work);

    private async Task DispatchAsync(Func<Task> work)
    {
        try
        {
            await InvokeAsync(work).ConfigureAwait(true);
        }
        catch (Exception exception) when (_disposed && exception is OperationCanceledException or ObjectDisposedException or JSDisconnectedException)
        {
            // Torn down while the callback ran.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (Services.GetService(typeof(ILogger<BpmnCanvas>)) is ILogger logger) LogCallbackFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A handler of an event from the BPMN canvas failed.")]
    private static partial void LogCallbackFailed(ILogger logger, Exception exception);

    private void HandleConfigApplied(ConfigApplied payload) => Dispatch(() =>
    {
        StateHasChanged();
        return Task.CompletedTask;
    });

    private void HandleConfigRejected(ConfigRejected payload) =>
        Dispatch(async () => await OnConfigRejected.InvokeAsync(payload).ConfigureAwait(true));

    private void HandleDocumentLoaded(DocumentLoaded payload) =>
        Dispatch(async () => await OnDocumentLoaded.InvokeAsync(payload).ConfigureAwait(true));

    private void HandleViewportChanged(ViewportChanged payload)
    {
        _viewport = payload;
        Dispatch(async () => await OnViewportChanged.InvokeAsync(payload).ConfigureAwait(true));
    }

    private void HandleShortcutInvoked(ShortcutInvoked payload) =>
        Dispatch(async () => await OnShortcutInvoked.InvokeAsync(payload).ConfigureAwait(true));

    private void HandleSelectionChanged(SelectionChanged payload)
    {
        _selected = payload.ElementIds;
        Dispatch(async () => await OnSelectionChanged.InvokeAsync(payload).ConfigureAwait(true));
    }

    private void HandleSelectionAnchorChanged(SelectionAnchorChanged payload) =>
        Dispatch(async () => await OnSelectionAnchorChanged.InvokeAsync(payload).ConfigureAwait(true));

    private void HandleEditRequested(EditRequest payload) =>
        Dispatch(async () => await OnEditRequested.InvokeAsync(payload).ConfigureAwait(true));

    private void HandleEditRejected(EditRejected payload) =>
        Dispatch(async () => await OnEditRejected.InvokeAsync(payload).ConfigureAwait(true));

    private void HandleContextMenuRequested(ContextMenuRequested payload) =>
        Dispatch(async () => await OnContextMenuRequested.InvokeAsync(payload).ConfigureAwait(true));

    private void HandleLabelEditRequested(LabelEditRequested payload) =>
        Dispatch(async () => await OnLabelEditRequested.InvokeAsync(payload).ConfigureAwait(true));

    private void HandleFileDropped(FileDropped payload) =>
        Dispatch(async () => await OnFileDropped.InvokeAsync(payload).ConfigureAwait(true));

    /// <summary>
    /// Resolves a stencil drop into paper geometry and an owning container.
    /// </summary>
    /// <remarks>
    /// The canvas is the only side that knows the paper coordinates, the current zoom and which
    /// container the point fell inside. It answers the question and the editor turns the answer
    /// into a creation command, so creation stays one transaction owned by the model side.
    /// </remarks>
    /// <param name="clientX">Pointer X, in client coordinates.</param>
    /// <param name="clientY">Pointer Y, in client coordinates.</param>
    /// <param name="elementType">The wire name of the type being created, which decides its default size.</param>
    /// <returns>The drop target, or <see langword="null"/> if the canvas is not ready.</returns>
    internal async ValueTask<DropTarget?> ResolveDropAsync(double clientX, double clientY, string elementType)
    {
        if (_instance is null || _disposed) return null;

        return await _instance
            .InvokeAsync<DropTarget?>("resolveDrop", _disposalCts.Token, clientX, clientY, elementType)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Resolves where a keyboard-armed stencil tool should be created.
    /// </summary>
    /// <param name="elementType">The wire name of the type being created.</param>
    /// <returns>The target, or <see langword="null"/> if the canvas is not ready.</returns>
    internal async ValueTask<DropTarget?> ResolveCreationPointAsync(string elementType)
    {
        if (_instance is null || _disposed) return null;

        return await _instance
            .InvokeAsync<DropTarget?>("resolveCreationPoint", _disposalCts.Token, elementType)
            .ConfigureAwait(true);
    }

    /// <summary>Where an element appended to another one should be created.</summary>
    /// <param name="sourceId">The element being appended to.</param>
    /// <param name="elementType">The wire name of the type being created.</param>
    /// <returns>The target, or <see langword="null"/> if the source is not drawn.</returns>
    internal async ValueTask<DropTarget?> ResolveAppendPointAsync(string sourceId, string elementType)
    {
        if (_instance is null || _disposed) return null;

        return await _instance
            .InvokeAsync<DropTarget?>("resolveAppendPoint", _disposalCts.Token, sourceId, elementType)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the context menu for a target the host resolved itself.
    /// </summary>
    /// <remarks>
    /// The pointer and touch paths never come through here: the canvas owns those, because it has
    /// to suppress the native menu in the same handler. This is the keyboard path from chrome that
    /// already knows which element has focus.
    /// </remarks>
    /// <param name="elementId">The element to anchor on, or <see langword="null"/> for the canvas.</param>
    /// <param name="source">One of <c>pointer</c>, <c>keyboard</c> or <c>longPress</c>.</param>
    internal ValueTask RequestContextMenuAsync(string? elementId, string source) =>
        _instance is null || _disposed
            ? ValueTask.CompletedTask
            : _instance.InvokeVoidAsync("requestContextMenu", _disposalCts.Token, elementId, source);

    /// <summary>Opens the inline label editor for one element.</summary>
    /// <param name="elementId">The element whose label to edit.</param>
    /// <returns>False when it has no label to edit, or the canvas is not ready.</returns>
    internal async ValueTask<bool> EditLabelAsync(string elementId)
    {
        if (_instance is null || _disposed) return false;

        return await _instance
            .InvokeAsync<bool>("editLabel", _disposalCts.Token, elementId)
            .ConfigureAwait(true);
    }

    /// <summary>Where an element is, in paper and client space.</summary>
    /// <param name="elementId">The element to locate.</param>
    /// <returns>The anchor, or <see langword="null"/> if it is not drawn on the active plane.</returns>
    internal async ValueTask<Anchor?> AnchorOfAsync(string elementId)
    {
        if (_instance is null || _disposed) return null;

        return await _instance
            .InvokeAsync<Anchor?>("anchorOf", _disposalCts.Token, elementId)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Draws an engine's view of a running instance over the diagram.
    /// </summary>
    /// <remarks>
    /// Available in read-only, and deliberately not routed through the edit path: an execution
    /// state changes nothing about the document, so it is neither a command nor undoable. The
    /// canvas keeps the last one it was given and redraws it after every render, which is what
    /// makes an overlay survive a switch to another plane and back.
    /// </remarks>
    /// <param name="state">The state to draw.</param>
    internal ValueTask SetExecutionStateAsync(BpmnExecutionState state) =>
        _instance is null || _disposed
            ? ValueTask.CompletedTask
            : _instance.InvokeVoidAsync("setExecutionState", _disposalCts.Token, state);

    /// <summary>Removes every execution overlay.</summary>
    internal ValueTask ClearExecutionStateAsync() =>
        _instance is null || _disposed
            ? ValueTask.CompletedTask
            : _instance.InvokeVoidAsync("clearExecutionState", _disposalCts.Token);

    /// <summary>Replaces the canvas selection.</summary>
    /// <param name="elementIds">The elements to select.</param>
    internal ValueTask SetSelectionAsync(IReadOnlyList<string> elementIds) =>
        _instance is null || _disposed
            ? ValueTask.CompletedTask
            : _instance.InvokeVoidAsync("setSelection", _disposalCts.Token, elementIds);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _disposalCts.CancelAsync().ConfigureAwait(false);

        await ReleaseInstanceAsync(_instance).ConfigureAwait(false);

        if (_module is not null)
        {
            await InvokeSafelyAsync(async () => await _module.DisposeAsync().ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        _bridgeReference?.Dispose();
        _bridge?.Dispose();
        _disposalCts.Dispose();
        _synchronization.Dispose();

        _module = null;
        _bridge = null;
        _bridgeReference = null;
        _instance = null;
    }

    /// <summary>Tears the canvas down and lets go of the reference to it.</summary>
    /// <remarks>
    /// Disposing the JS object reference alone would leave the canvas's listeners and paper alive,
    /// so the canvas is asked to dispose itself first. Either step may find the other side already
    /// gone, which is not worth surfacing.
    /// </remarks>
    private static async ValueTask ReleaseInstanceAsync(IJSObjectReference? instance)
    {
        if (instance is null) return;

        await InvokeSafelyAsync(async () => await instance.InvokeVoidAsync("dispose").ConfigureAwait(false))
            .ConfigureAwait(false);
        await InvokeSafelyAsync(async () => await instance.DisposeAsync().ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an interop call, swallowing the three failures that mean "the other side is already
    /// gone".
    /// </summary>
    /// <remarks>
    /// During teardown the circuit may have dropped (<see cref="JSDisconnectedException"/>), the
    /// token may already be cancelled, or the runtime may be disposed. None of those are errors
    /// worth surfacing — the work they would have done is exactly the work disposal is doing —
    /// and letting them escape turns every page navigation into a logged exception.
    /// </remarks>
    private static async Task InvokeSafelyAsync(Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
