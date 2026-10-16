/**
 * Versioned interop payload shapes.
 *
 * These are deliberately separate from the module's internal types: either side may
 * reshape its own model freely as long as this file stays compatible (implementation
 * plan §5). Everything here is `readonly` and JSON-round-trippable — no classes, no
 * methods, no `undefined` (the .NET serializer emits `null`).
 */

/** Canvas surface options. */
export interface CanvasOptionsContract {
  readonly background: string;
}

/** Grid rendering and snapping options. */
export interface GridOptionsContract {
  readonly size: number;
  readonly isVisible: boolean;
  readonly snapEnabled: boolean;
}

/**
 * What an unmodified mouse wheel does over the canvas.
 *
 * A wheel event is what a trackpad sends for a two-finger scroll, so `pan` is the gesture most
 * users expect. Holding Ctrl (or Cmd) always zooms whatever this says, because that is what a
 * trackpad pinch reports and what every browser already treats as zoom.
 */
export type WheelMode = 'pan' | 'zoom';

/** Zoom/pan bounds and fit behaviour. */
export interface ViewportOptionsContract {
  readonly minZoom: number;
  readonly maxZoom: number;
  readonly zoomStep: number;
  readonly fitPadding: number;
  readonly wheelMode: WheelMode;
  readonly wheelPanSpeed: number;
  readonly dragPanEnabled: boolean;
  readonly touchEnabled: boolean;
}

/** One key combination bound to one canvas command. */
export interface ShortcutBindingContract {
  readonly command: ShortcutCommand;
  /** Matched against `KeyboardEvent.key`, case-insensitively for printable keys. */
  readonly key: string;
  readonly ctrl: boolean;
  readonly shift: boolean;
  readonly alt: boolean;
  readonly meta: boolean;
  /** Shown in the shortcut help surface. */
  readonly description: string;
}

/** Everything a keyboard shortcut can ask the canvas to do in this milestone. */
export type ShortcutCommand =
  | 'zoomIn'
  | 'zoomOut'
  | 'resetZoom'
  | 'fitToContent'
  | 'panLeft'
  | 'panRight'
  | 'panUp'
  | 'panDown'
  | 'showShortcuts'
  | 'selectAll'
  | 'clearSelection'
  | 'delete'
  | 'undo'
  | 'redo'
  | 'copy'
  | 'cut'
  | 'paste'
  | 'duplicate';

/** Keyboard navigation options. Bindings are host-configurable; none are hard-coded here. */
export interface KeyboardOptionsContract {
  readonly enabled: boolean;
  /** Paper units moved per arrow-key press. */
  readonly panStep: number;
  readonly bindings: readonly ShortcutBindingContract[];
}

/** How an event edit that conflicts only with interruption rules is handled. */
export type EventEditPolicy = 'reject' | 'autoAdjustInterrupting';

/** Editing behaviour: what the canvas enforces while the user changes a diagram. */
export interface EditingOptionsContract {
  readonly preventCollisions: boolean;
  readonly collisionClearance: number;
  readonly historyDepth: number;
  readonly eventEditPolicy: EventEditPolicy;

  /**
   * How long a touch must be held before it opens the context menu, in milliseconds.
   *
   * Section 7 calls this "a configured touch long-press", so it is configuration rather than a
   * constant. Zero turns it off, which is what a host with its own touch gestures wants.
   */
  readonly longPressMilliseconds: number;
}

/**
 * Which automatic router computes a connector's shape.
 *
 * Manhattan is the only automatic router in the initial release, and deliberately: JointJS+'s
 * obstacle-avoiding router is not licensable here, so the choice is between the core Manhattan
 * router and writing one. `straight` is not a second router — it is the absence of one, kept so a
 * host that lays out its own routes can turn ours off without the two fighting.
 */
export type RouterMode = 'manhattan' | 'straight';

/**
 * How connectors are routed.
 *
 * Only connectors the editor draws are routed. A connector whose waypoints came from a document is
 * drawn as the document states them (sections 10.6 and 11), so these settings decide what a *new*
 * or *re-dragged* route looks like, not what an imported one is redrawn as.
 */
export interface RoutingOptionsContract {
  readonly mode: RouterMode;

  /** The Manhattan router's grid step, in paper units. */
  readonly step: number;

  /** How far a route keeps clear of the shapes it passes. */
  readonly padding: number;

  /**
   * How far apart two connectors between the same pair of shapes are pulled.
   *
   * Zero lets them overlap, which is what "no separation" means. The separation moves the route's
   * interior rather than its endpoints: section 4.6 requires an attachment to sit on the outline,
   * so a nudged endpoint is projected back onto it and most of the nudge is undone.
   */
  readonly parallelSeparation: number;

  /**
   * Whether a route leaves and enters along the Pool's dominant flow axis.
   *
   * The specification requires auto-routing to branch on `isHorizontal` so new connectors read the
   * way the Pool does — left-to-right in a horizontal Pool, top-to-bottom in a vertical one.
   */
  readonly followPoolOrientation: boolean;
}

/** Alignment guide ("snapline") options. */
export interface SnappingOptionsContract {
  readonly alignmentGuidesEnabled: boolean;
  /** How close two edges must be, in paper units, before they snap and a guide is drawn. */
  readonly tolerance: number;
}

/**
 * The authoritative editor configuration snapshot, resolved and validated on the Blazor
 * side and sent before the paper is constructed. TypeScript never maintains competing
 * public defaults (specification "Rendering-platform and configuration boundary").
 */
export interface EditorConfigContract {
  readonly contractVersion: string;
  readonly revision: number;
  readonly isReadOnly: boolean;
  readonly canvas: CanvasOptionsContract;
  readonly grid: GridOptionsContract;
  readonly viewport: ViewportOptionsContract;
  readonly keyboard: KeyboardOptionsContract;
  readonly snapping: SnappingOptionsContract;
  readonly editing: EditingOptionsContract;
  readonly routing: RoutingOptionsContract;
  /** Display resources resolved by Blazor; never used for semantic or diagnostic data. */
  readonly strings: CanvasStringsContract;
}

export interface CanvasStringsContract {
  readonly values: Readonly<Record<string, string>>;
}

/** What an engine reports an element is doing. `None` draws no overlay. */
export type ActivityStatus = 'None' | 'Active' | 'Completed' | 'Failed' | 'Canceled' | 'Waiting';

/** How much an incident matters. */
export type IncidentSeverity = 'Info' | 'Warning' | 'Error';

/** Something the engine reported against one element. */
export interface IncidentContract {
  readonly id: string;
  readonly message: string;
  readonly severity: IncidentSeverity;
}

/** What one element is doing. */
export interface ElementExecutionContract {
  readonly elementId: string;
  readonly status: ActivityStatus;
  /** Tokens sitting on the element. Zero draws no counter. */
  readonly tokenCount: number;
  readonly incidents: readonly IncidentContract[];
}

/** Whether a connector has carried anything. */
export interface FlowExecutionContract {
  readonly flowId: string;
  readonly traversed: boolean;
  readonly traversalCount: number;
}

/**
 * One engine's view of one running instance.
 *
 * A whole snapshot rather than a delta, so a lost update corrects itself on the next one instead
 * of leaving the overlay permanently out of step with the engine.
 */
export interface ExecutionStateContract {
  readonly instanceId: string;
  readonly elements: readonly ElementExecutionContract[];
  readonly flows: readonly FlowExecutionContract[];
}

/** Why a configuration snapshot or patch was refused. Never thrown across interop. */
export interface ConfigRejection {
  readonly revision: number;
  readonly code: string;
  readonly path: string;
  readonly message: string;
}

/** Returned synchronously from `initialize`. */
export interface CanvasInitResult {
  readonly instanceId: string;
  readonly contractVersion: string;
  readonly configRevision: number;
  readonly accepted: boolean;
  readonly rejection: ConfigRejection | null;
}

/** Raised once the paper exists and the instance is registered. */
export interface CanvasInitializedPayload {
  readonly instanceId: string;
  readonly configRevision: number;
}

/** Raised after a configuration patch is accepted. */
export interface ConfigAppliedPayload {
  readonly revision: number;
  readonly rebuildPerformed: boolean;
}

/** Raised after a document has been projected onto the graph. */
export interface DocumentLoadedPayload {
  readonly diagramId: string;
  readonly shapeCount: number;
  readonly edgeCount: number;
  readonly unrenderedElementIds: readonly string[];
}

/** Raised (coalesced) whenever the paper transform changes. */
export interface ViewportChangedPayload {
  readonly zoom: number;
  readonly translateX: number;
  readonly translateY: number;
}

/** A viewport operation requested from the Blazor toolbar or a keyboard shortcut. */
export type ViewportCommand =
  | { readonly kind: 'zoomIn' }
  | { readonly kind: 'zoomOut' }
  | { readonly kind: 'reset' }
  | { readonly kind: 'fitToContent' }
  | { readonly kind: 'setZoom'; readonly zoom: number }
  | { readonly kind: 'panBy'; readonly dx: number; readonly dy: number }
  | { readonly kind: 'revealElement'; readonly elementId: string }
  /**
   * Puts the paper back at an exact scale and translation.
   *
   * The others are gestures — a step, a fit, a nudge — and none of them can reproduce a
   * particular view. This one exists so a canvas that had to be rebuilt can be handed back the
   * view it was showing, rather than dumping the reader at the origin because a setting changed.
   */
  | { readonly kind: 'setTransform'; readonly zoom: number; readonly dx: number; readonly dy: number };

/**
 * Raised when a bound key combination fires a command the canvas does not own itself.
 *
 * Only `showShortcuts` reaches Blazor today: the help surface is chrome, so the canvas reports
 * the intent rather than rendering a dialog of its own inside the SVG.
 */
export interface ShortcutInvokedPayload {
  readonly command: ShortcutCommand;
}

/**
 * A rectangle on the wire.
 *
 * Declared here rather than imported from the model contracts: this file is the interop surface,
 * and it stays independently versionable only if it does not reach into the model's own shapes.
 */
export interface GeometryBounds {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** One shape's committed geometry. Keyed by the DI shape, not the semantic element. */
/** A BPMN file dropped on the canvas, read but not interpreted. */
export interface FileDroppedPayload {
  readonly fileName: string;
  readonly xml: string;
}

/** One connector's route, as the document should store it. */
export interface ConnectorRouteContract {
  readonly elementId: string;
  readonly waypoints: readonly PointContract[];
}

export interface ShapeGeometryContract {
  readonly shapeId: string;
  readonly bounds: GeometryBounds;
}

/** Raised whenever the set of selected cells changes. */
export interface SelectionChangedPayload {
  readonly elementIds: readonly string[];

  /**
   * Where the one selected element is on screen, for chrome that has to sit beside it.
   *
   * Null unless exactly one element is selected: the contextual-action surface attaches to a
   * single element, and a rectangle around several of them would put the actions somewhere none
   * of them are. Sent with the selection rather than fetched afterwards so the surface does not
   * have to make a second interop call to find out where to draw itself.
   */
  readonly anchor: AnchorContract | null;
}

/**
 * The one selected element moved on screen, or the view moved under it, while the selection
 * stayed the same. The anchor is null while a gesture is moving it.
 */
export interface SelectionAnchorChangedPayload {
  readonly elementId: string;
  readonly anchor: AnchorContract | null;
}

/**
 * Where something is, in both spaces that matter.
 *
 * Section 7 specifies paper-relative bounds for a context-menu target, which is the space the
 * document is in and the one a command has to be expressed in. HTML chrome needs the other space,
 * because it is positioned in the canvas host's box. Sending both means neither side has to know
 * the other's transform, and the two can never disagree about which element was meant.
 */
export interface AnchorContract {
  /** Paper-relative bounds; zero-size for a point. */
  readonly bounds: GeometryBounds;

  /** The same rectangle in CSS pixels, relative to the canvas host element's top-left corner. */
  readonly client: GeometryBounds;
}

/** How a context menu was asked for. */
export type ContextMenuSource = 'pointer' | 'keyboard' | 'longPress';

/** What the request resolved to. */
export type ContextMenuTarget = 'element' | 'link' | 'canvas';

/**
 * A request for the Blazor-owned context menu (specification section 7).
 *
 * The native menu is already suppressed by the time this is sent, unconditionally and in both Edit
 * and Readonly modes. A request always arrives — a blank-canvas right-click resolves to the canvas
 * with a zero-size anchor rather than to nothing — because section 7 forbids silently falling back
 * to the browser's own menu when there is no target-specific command to offer.
 */
export interface ContextMenuRequestPayload {
  readonly target: ContextMenuTarget;

  /** The BPMN element the target owns, or null for the canvas itself. */
  readonly elementId: string | null;

  readonly anchor: AnchorContract;
  readonly source: ContextMenuSource;

  /** The selection at the moment of the request, which the menu's commands act on. */
  readonly selection: readonly string[];
}

/**
 * A request to edit a label in place (specification section 4.5).
 *
 * Carries the metrics the canvas actually drew with — read off the rendered text node rather than
 * recomputed — so the overlay can wrap and break the same way. Section 4.5 requires the inline
 * editor to match the canvas's own layout, and the only way to be sure of that is to ask the
 * canvas what it did.
 */
export interface LabelEditRequestPayload {
  readonly elementId: string;

  /** The label box, in both spaces. */
  readonly anchor: AnchorContract;

  /** The text as the document holds it, newlines included. */
  readonly text: string;

  readonly fontFamily: string;
  readonly fontSize: number;

  /** The distance between baselines, in CSS pixels at the current zoom. */
  readonly lineHeight: number;

  readonly textAlign: 'left' | 'center' | 'right';

  /** The paper's current scale, so the overlay can draw text at the size the canvas shows. */
  readonly scale: number;
}

/**
 * An edit the canvas is asking the host to apply.
 *
 * The canvas never changes the document — it is a view over one Blazor owns. A move is applied to
 * the graph optimistically so the drag feels immediate, then reported here; a delete has nothing
 * to apply optimistically and is only ever a request.
 */
export type EditRequest = { readonly diagramId?: string | null } & (
  | {
      readonly kind: 'move';
      readonly moves: readonly ShapeGeometryContract[];
      /**
       * The connectors attached to what moved, re-squared against their new ends.
       *
       * A move changes where a connector starts and ends but not the bends between, so without
       * these the first and last segments come out at whatever angle the new position implies.
       * Sent with the move rather than as a second command, so one drag is one undo.
       */
      readonly routes: readonly ConnectorRouteContract[];
    }
  | { readonly kind: 'delete'; readonly elementIds: readonly string[] }
  | {
    readonly kind: 'connect';
    readonly connectorKind: string;
    readonly sourceRef: string;
    readonly targetRef: string;
    readonly waypoints: readonly PointContract[];
  }
  | { readonly kind: 'undo' }
  | { readonly kind: 'redo' }
  | { readonly kind: 'copy'; readonly elementIds: readonly string[] }
  | { readonly kind: 'cut'; readonly elementIds: readonly string[] }
  | { readonly kind: 'duplicate'; readonly elementIds: readonly string[] }
  | { readonly kind: 'paste' }
  | {
    readonly kind: 'resize';
    readonly elementId: string;
    readonly bounds: GeometryBounds;
    /** The flows attached to the element, routed against its new size. */
    readonly routes: readonly ConnectorRouteContract[];
  }
  /**
   * The splitter after a Lane was dragged. `elementId` is the Lane before the splitter and
   * `position` its new coordinate (Y in a horizontal Pool, X in a vertical one). Lanes always
   * fill their Pool, so this is the only way they are sized against one another.
   */
  | { readonly kind: 'laneSplitter'; readonly elementId: string; readonly position: number }
  | {
    readonly kind: 'reconnect';
    readonly elementId: string;
    readonly sourceRef: string;
    readonly targetRef: string;
    readonly waypoints: readonly PointContract[];
  }
  | { readonly kind: 'reroute'; readonly elementId: string; readonly waypoints: readonly PointContract[] }

  /**
    * A Sub-Process should be drawn expanded, or collapsed again (section 1.2.1).
    *
    * The target state, not a toggle: a toggle depends on the canvas and the document agreeing
    * about the current state at the moment it is applied, so two quick presses on the marker
    * would flip twice and land back where they started.
    *
    * Nothing is applied optimistically. Expanding materialises DI for content the canvas has
    * never drawn and resizes the container to hold it, and only the document can compute either
    * — so this is a request, and the canvas redraws from the answer.
    */
  | { readonly kind: 'expand'; readonly elementId: string }
  | { readonly kind: 'collapse'; readonly elementId: string }
  | {
    readonly kind: 'reparent';
    readonly elementId: string;
    readonly containerId: string;
    readonly laneId: string | null;
    readonly bounds: GeometryBounds;
  });

/** A point on the wire. */
export interface PointContract {
  readonly x: number;
  readonly y: number;
}

/** Where a stencil drop landed, and what would own the created element. */
export interface DropTargetContract {
  readonly bounds: GeometryBounds;
  readonly containerId: string | null;
  readonly poolId: string | null;
}

/** Raised when the canvas refused a gesture before reporting it. */
export interface EditRejectedPayload {
  readonly code: string;
  readonly message: string;
  readonly elementId: string;
}
