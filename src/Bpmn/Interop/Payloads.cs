namespace Hasseware.AspNetCore.Components.Bpmn.Interop;

/// <summary>
/// Payloads exchanged with the canvas module.
/// </summary>
/// <remarks>
/// Deliberately separate from the semantic model types: either side may reshape its own
/// representation as long as these records stay compatible (implementation plan section 5).
/// No <c>JsonPropertyName</c> attributes are needed — Blazor JS interop already serialises
/// with <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>, which camel-cases names.
/// </remarks>

/// <summary>Result of the module's <c>initialize</c> call.</summary>
/// <param name="InstanceId">Handle used for every later call; empty when the config was refused.</param>
/// <param name="ContractVersion">Interop version the loaded bundle implements.</param>
/// <param name="ConfigRevision">Revision of the configuration snapshot that was accepted.</param>
/// <param name="Accepted">False when the configuration was refused and no canvas was created.</param>
/// <param name="Rejection">Why the configuration was refused, when it was.</param>
public sealed record CanvasInitResult(
    string InstanceId,
    string ContractVersion,
    int ConfigRevision,
    bool Accepted,
    ConfigRejected? Rejection);

/// <summary>Raised once the paper exists and the instance is registered.</summary>
public sealed record CanvasInitialized(string InstanceId, int ConfigRevision);

/// <summary>Raised after a configuration patch is accepted.</summary>
public sealed record ConfigApplied(int Revision, bool RebuildPerformed);

/// <summary>
/// Raised when a configuration snapshot or patch is refused. The previously accepted
/// configuration stays active; nothing is ever partially applied.
/// </summary>
public sealed record ConfigRejected(int Revision, string Code, string Path, string Message);

/// <summary>Raised after a document has been projected onto the canvas.</summary>
/// <param name="DiagramId">The BPMNDiagram that was rendered.</param>
/// <param name="ShapeCount">Number of BPMNShape entries drawn.</param>
/// <param name="EdgeCount">Number of connectors drawn.</param>
/// <param name="UnrenderedElementIds">
/// DI entries whose referenced element could not be resolved. Surfaced rather than swallowed:
/// specification section 10.1 treats an unresolvable <c>bpmnElement</c> as a document defect.
/// </param>
public sealed record DocumentLoaded(
    string DiagramId,
    int ShapeCount,
    int EdgeCount,
    IReadOnlyList<string> UnrenderedElementIds);

/// <summary>Raised (coalesced) whenever the paper transform changes.</summary>
public sealed record ViewportChanged(double Zoom, double TranslateX, double TranslateY);

/// <summary>Raised whenever the set of selected cells changes.</summary>
/// <param name="ElementIds">The selected element ids, sorted.</param>
/// <param name="Anchor">
/// Where the one selected element is, or <see langword="null"/> unless exactly one is selected.
/// The contextual-action surface attaches to a single element; a rectangle around several would
/// put the actions beside none of them.
/// </param>
public sealed record SelectionChanged(IReadOnlyList<string> ElementIds, Anchor? Anchor = null);

/// <summary>
/// The one selected element moved on screen, or the view moved under it, while the selection stayed
/// the same.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="SelectionChanged"/> so a host listening for selection changes is not
/// told about one that did not happen. Only chrome drawn beside the element needs to know.
/// </remarks>
/// <param name="ElementId">The selected element.</param>
/// <param name="Anchor">
/// Where it is now, or <see langword="null"/> while a gesture is moving it — the chrome beside it
/// hides until the gesture ends.
/// </param>
public sealed record SelectionAnchorChanged(string ElementId, Anchor? Anchor);

/// <summary>
/// Where something is, in both spaces that matter.
/// </summary>
/// <remarks>
/// Section 7 specifies paper-relative bounds for a context-menu target, because that is the space
/// the document is in and the one a command has to be expressed in. HTML chrome needs the other
/// space: it is positioned inside the canvas host's box. Both are sent so neither side has to know
/// the other's transform, and the two can never disagree about which element was meant.
/// </remarks>
/// <param name="Bounds">Paper-relative bounds; zero-size for a point.</param>
/// <param name="Client">The same rectangle in CSS pixels, relative to the canvas host's top-left.</param>
public sealed record Anchor(Model.Di.Bounds Bounds, Model.Di.Bounds Client);

/// <summary>
/// A request for the Blazor-owned context menu (specification section 7).
/// </summary>
/// <remarks>
/// The native menu is already suppressed by the time this arrives, unconditionally and in both Edit
/// and Readonly modes. A request always arrives — a right-click on blank paper resolves to the
/// canvas with a zero-size anchor rather than to nothing — because section 7 forbids ever falling
/// back to the browser's own menu.
/// </remarks>
/// <param name="Target">One of <c>element</c>, <c>link</c> or <c>canvas</c>.</param>
/// <param name="ElementId">The BPMN element the target owns, or <see langword="null"/> for the canvas.</param>
/// <param name="Anchor">Where to put the menu.</param>
/// <param name="Source">One of <c>pointer</c>, <c>keyboard</c> or <c>longPress</c>.</param>
/// <param name="Selection">The selection at the moment of the request, which the commands act on.</param>
public sealed record ContextMenuRequested(
    string Target,
    string? ElementId,
    Anchor Anchor,
    string Source,
    IReadOnlyList<string> Selection);

/// <summary>
/// A request to edit a label in place (specification section 4.5).
/// </summary>
/// <remarks>
/// The metrics are the ones the canvas drew with, read off the rendered text node rather than
/// recomputed, because section 4.5 requires the inline editor's layout to match the canvas's and
/// the only measurement guaranteed to match is the canvas's own.
/// </remarks>
/// <param name="ElementId">The element whose label is being edited.</param>
/// <param name="Anchor">The label box, in both spaces.</param>
/// <param name="Text">The text as the document holds it, newlines included.</param>
/// <param name="FontFamily">The rendered font stack.</param>
/// <param name="FontSize">The font size in paper units, before the paper transform.</param>
/// <param name="LineHeight">The distance between baselines, in paper units.</param>
/// <param name="TextAlign">One of <c>left</c>, <c>center</c> or <c>right</c>.</param>
/// <param name="Scale">The paper's current scale.</param>
public sealed record LabelEditRequested(
    string ElementId,
    Anchor Anchor,
    string Text,
    string FontFamily,
    double FontSize,
    double LineHeight,
    string TextAlign,
    double Scale);

/// <summary>One shape's committed geometry, keyed by its DI shape rather than its element.</summary>
/// <param name="ShapeId">The DI shape that moved.</param>
/// <param name="Bounds">Its new bounds.</param>
public sealed record ShapeGeometryPayload(string ShapeId, Model.Di.Bounds Bounds);

/// <summary>A BPMN file dropped on the canvas, read but not interpreted.</summary>
/// <param name="FileName">What the file was called.</param>
/// <param name="Xml">Its contents.</param>
public sealed record FileDropped(string FileName, string Xml);

/// <summary>One connector's route, re-squared by the canvas after its ends moved.</summary>
/// <param name="ElementId">The connector.</param>
/// <param name="Waypoints">Its whole route, ends included.</param>
public sealed record ConnectorRoutePayload(string ElementId, IReadOnlyList<Model.Di.DiPoint> Waypoints);

/// <summary>
/// An edit the canvas is asking to have applied.
/// </summary>
/// <remarks>
/// The canvas never changes the document; it renders one this side owns. A move has already been
/// applied to the graph optimistically so the drag felt immediate, and arrives here to be recorded;
/// a delete, an undo and a redo are requests with nothing applied yet.
/// </remarks>
/// <param name="Kind">One of <c>move</c>, <c>delete</c>, <c>connect</c>, <c>undo</c>, <c>redo</c>.</param>
/// <param name="Moves">The committed geometry, for <c>move</c>.</param>
/// <param name="ElementIds">The elements to delete, for <c>delete</c>.</param>
/// <param name="ConnectorKind">Which connector the canvas resolved, for <c>connect</c>.</param>
/// <param name="SourceRef">The originating element, for <c>connect</c>.</param>
/// <param name="TargetRef">The receiving element, for <c>connect</c>.</param>
/// <param name="Waypoints">The route the canvas drew, for <c>connect</c>.</param>
/// <param name="ElementId">The shape that was resized, for <c>resize</c>.</param>
/// <param name="Bounds">Its new bounds, for <c>resize</c> and <c>reparent</c>.</param>
/// <param name="ContainerId">The container it was dropped into, for <c>reparent</c>.</param>
/// <param name="LaneId">The Lane it landed in, for <c>reparent</c>.</param>
/// <param name="Routes">The re-squared connectors attached to what moved, for <c>move</c>.</param>
/// <param name="Position">The splitter's new coordinate, for <c>laneSplitter</c>, whose <c>ElementId</c> is the Lane before it.</param>
public sealed record EditRequest(
    string Kind,
    IReadOnlyList<ShapeGeometryPayload>? Moves = null,
    IReadOnlyList<string>? ElementIds = null,
    string? ConnectorKind = null,
    string? SourceRef = null,
    string? TargetRef = null,
    IReadOnlyList<Model.Di.DiPoint>? Waypoints = null,
    string? ElementId = null,
    Model.Di.Bounds? Bounds = null,
    string? ContainerId = null,
    string? LaneId = null,
    IReadOnlyList<ConnectorRoutePayload>? Routes = null,
    double? Position = null)
{
    /// <summary>The diagram on which the gesture was committed.</summary>
    public string? DiagramId { get; init; }
}

/// <summary>Where a stencil drop landed, and what would own the created element.</summary>
/// <param name="Bounds">The geometry for the new shape, already grid-snapped if snapping is on.</param>
/// <param name="ContainerId">
/// The innermost flow-elements container under the pointer, or <see langword="null"/> for the
/// plane itself. A Lane is never reported here: it classifies nodes without owning them.
/// </param>
/// <param name="PoolId">The Pool under the pointer, which governs Message Flow rather than containment.</param>
public sealed record DropTarget(Model.Di.Bounds Bounds, string? ContainerId, string? PoolId)
{
    /// <summary>Rendered content bounds by root Process, including unpersisted external labels.</summary>
    public IReadOnlyDictionary<string, Model.Di.Bounds>? ProcessBounds { get; init; }
}

/// <summary>Raised when the canvas refused a gesture and rolled it back itself.</summary>
/// <param name="Code">The rule code, for example <c>DI-011</c>.</param>
/// <param name="Message">What the user should be told.</param>
/// <param name="ElementId">The element the gesture was about.</param>
public sealed record EditRejected(string Code, string Message, string ElementId);

/// <summary>Raised when a bound key fires a command the canvas does not handle itself.</summary>
/// <param name="Command">
/// The command name as the canvas contract spells it, for example <c>showShortcuts</c>. A string
/// rather than the <c>BpmnShortcutCommand</c> enum: an unrecognised value from a newer canvas
/// bundle must arrive intact and be ignored, not fail deserialization of the whole payload.
/// </param>
public sealed record ShortcutInvoked(string Command);

/// <summary>A viewport operation requested from the toolbar or a keyboard shortcut.</summary>
/// <param name="Kind">
/// One of <c>zoomIn</c>, <c>zoomOut</c>, <c>reset</c>, <c>fitToContent</c>, <c>setZoom</c>,
/// <c>panBy</c>, <c>revealElement</c>, <c>setTransform</c>.
/// </param>
/// <param name="Zoom">Absolute scale, used by <c>setZoom</c> and <c>setTransform</c>.</param>
/// <param name="Dx">
/// Horizontal translation, in screen pixels for <c>panBy</c> and absolute for <c>setTransform</c>.
/// </param>
/// <param name="Dy">The vertical half of the same.</param>
/// <param name="ElementId">What to bring into view, used only by <c>revealElement</c>.</param>
public sealed record ViewportCommand(
    string Kind,
    double Zoom = 1d,
    double Dx = 0d,
    double Dy = 0d,
    string? ElementId = null)
{
    /// <summary>Zooms in by one configured step.</summary>
    public static ViewportCommand ZoomIn { get; } = new("zoomIn");

    /// <summary>Zooms out by one configured step.</summary>
    public static ViewportCommand ZoomOut { get; } = new("zoomOut");

    /// <summary>Restores 100% scale and the origin translation.</summary>
    public static ViewportCommand Reset { get; } = new("reset");

    /// <summary>Scales and centres the diagram to fit the viewport.</summary>
    public static ViewportCommand FitToContent { get; } = new("fitToContent");

    /// <summary>Sets an absolute scale, clamped to the configured zoom bounds.</summary>
    public static ViewportCommand SetZoom(double zoom) => new("setZoom", zoom);

    /// <summary>Translates the canvas by a screen-space delta.</summary>
    public static ViewportCommand PanBy(double dx, double dy) => new("panBy", Dx: dx, Dy: dy);

    /// <summary>
    /// Brings one element into view, without changing the scale.
    /// </summary>
    /// <remarks>
    /// Pans rather than zooms, and only when the element is not already visible. A keyboard reader
    /// stepping along a flow should see the canvas follow them; re-centring on every step, or
    /// changing the zoom under someone who chose it, would be the canvas taking over.
    /// </remarks>
    /// <param name="elementId">The element to reveal.</param>
    public static ViewportCommand RevealElement(string elementId) => new("revealElement", ElementId: elementId);
}
