import { CanvasAccessibility } from '../a11y/canvas-accessibility.js';
import type { AcceptedConfig } from '../config/accepted-config.js';
import { AnchorResolver } from '../editing/anchor.js';
import { ConnectController } from '../editing/connect-controller.js';
import { ContextMenuController } from '../editing/context-menu.js';
import { resolveAppendPoint, resolveCreationPoint, resolveDrop } from '../editing/drop-resolver.js';
import { LiveRouting } from '../editing/live-routing.js';
import { FileDropTarget } from '../editing/file-drop.js';
import { InteractionController } from '../editing/interaction-controller.js';
import { ExpandControls } from '../editing/expand-controls.js';
import { LabelEditController } from '../editing/label-edit.js';
import { LinkEditController } from '../editing/link-tools.js';
import { SegmentDragController } from '../editing/segment-drag.js';
import { suppressNativeMenu } from '../editing/native-menu.js';
import { positionToolbarColors } from '../editing/toolbar-colors.js';
import { LaneSplitterController } from '../editing/lane-splitter.js';
import { ResizeController } from '../editing/resize-tool.js';
import { RubberBandController } from '../editing/rubber-band.js';
import { ExecutionOverlay } from '../execution/execution-overlay.js';
import { StencilDragImage } from '../editing/stencil-drag.js';
import { findCell, findElement, findView } from '../graph/cells.js';
import { removeDocumentElements, renderDocument } from '../graph/document-renderer.js';
import { PaperHost } from '../graph/paper-host.js';
import { SelectionAnchorReporter } from './selection-anchor.js';
import type { BpmnEventBridge } from '../interop/event-bridge.js';
import type {
  AnchorContract,
  ContextMenuSource,
  DocumentLoadedPayload,
  DropTargetContract,
  ExecutionStateContract,
  ShortcutCommand,
  ViewportCommand,
} from '../interop/contracts.js';
import { ShortcutController } from '../input/shortcuts.js';
import { CanvasFontMetrics, type FontMetricsProvider } from '../labels/font-metrics.js';
import type { BpmnDocumentContract } from '../model/contracts.js';
import { SnapController } from '../snapping/snap-controller.js';
import { ViewportController } from '../viewport/viewport.js';

/**
 * The commands that change the document, and so are unavailable in read-only mode.
 *
 * Selection and navigation are deliberately absent: read-only retains both.
 */
/*
 * Copy is deliberately absent. Reading the selection onto the clipboard changes nothing about the
 * document, and section 7 asks read-only to keep inspection while losing every mutation surface —
 * being unable to copy a diagram you are allowed to look at would be a restriction nobody asked
 * for.
 */
const MUTATING_COMMANDS: ReadonlySet<ShortcutCommand> = new Set([
  'delete', 'undo', 'redo', 'cut', 'paste', 'duplicate',
]);

/**
 * Everything one BpmnCanvas component owns on the TypeScript side.
 *
 * One instance per component, addressed by id, so several editors can coexist on a page
 * with independent configuration — which is also why nothing here touches module-level
 * or global JointJS state.
 */
export class BpmnCanvasInstance {
  readonly #paperHost: PaperHost;
  readonly #viewport: ViewportController;
  readonly #shortcuts: ShortcutController;
  readonly #interaction: InteractionController;
  readonly #fileDrop: FileDropTarget;
  readonly #connect: ConnectController;
  readonly #rubberBand: RubberBandController | null;
  readonly #resize: ResizeController | null;
  readonly #laneSplitters: LaneSplitterController | null;
  readonly #live: LiveRouting | null;
  readonly #releaseNativeMenu: () => void;
  readonly #releaseToolbarColors: () => void;
  readonly #linkEdit: LinkEditController | null;
  readonly #segmentDrag: SegmentDragController | null;
  readonly #snapping: SnapController;
  readonly #accessibility: CanvasAccessibility;
  readonly #contextMenu: ContextMenuController;
  readonly #labelEdit: LabelEditController;
  readonly #expandControls: ExpandControls;
  readonly #anchors: AnchorResolver;
  readonly #bridge: BpmnEventBridge;
  readonly #metrics: FontMetricsProvider;
  readonly #execution: ExecutionOverlay;
  readonly #stencilDrag: StencilDragImage;

  #document: BpmnDocumentContract | null = null;
  #activeDiagramId: string | null = null;
  #disposed = false;

  /** Keeps the quick-action strip beside the selected element; null until built and after disposal. */
  #anchorReporter: SelectionAnchorReporter | null = null;

  /** Any element moving or resizing may be the selected one, or carry it along as its container. */
  readonly #handleGeometryChanged = (): void => {
    this.#anchorReporter?.moved();
  };

  /**
   * @param id This instance's registry id.
   * @param hostElement
   * The canvas root: what the context menu suppresses on, what the keyboard binds to, and the box
   * every client coordinate is relative to. Blazor owns its children.
   * @param paperElement The element JointJS mounts into, owned entirely by this module.
   * @param config The accepted configuration.
   * @param bridge The channel every event goes out on.
   * @param metrics A font-metrics provider; the canvas's own by default.
   */
  public constructor(
    public readonly id: string,
    hostElement: HTMLElement,
    paperElement: HTMLElement,
    public readonly config: AcceptedConfig,
    bridge: BpmnEventBridge,
    metrics?: FontMetricsProvider,
  ) {
    this.#bridge = bridge;
    // Scoped to the host: the theme declares --bpmn-font-family on .bpmn-canvas, not on
    // :root, so measurement resolved against the document element would find nothing.
    this.#metrics = metrics ?? new CanvasFontMetrics(hostElement);
    // Published on the host element so a test or a debugging session can address this instance
    // without the module having to expose its registry.
    hostElement.dataset.bpmnInstance = id;

    /*
     * The connect policy is built before the paper, because the paper needs its validator and
     * provisional-link factory at construction time — configuring them afterwards would leave a
     * window in which a connector drag was judged by nothing.
     */
    const connect = new ConnectController(config.grid, config.routing, config.strings);

    this.#paperHost = new PaperHost(paperElement, config, config.isReadOnly ? undefined : connect);
    this.#viewport = new ViewportController(this.#paperHost.paper, config, (payload) => {
      bridge.viewportChanged(payload);
      this.#anchorReporter?.moved();
    });
    this.#shortcuts = new ShortcutController(hostElement, config.keyboard, (command) => {
      this.#runShortcut(command);
    });
    this.#snapping = new SnapController(this.#paperHost.graph, this.#paperHost.paper, config.snapping);
    this.#accessibility = new CanvasAccessibility(this.#paperHost.paper, hostElement, config.strings);

    this.#connect = connect.attach(this.#paperHost.paper, {
      connectorRequested: (connectorKind, sourceRef, targetRef, waypoints) => {
        void bridge.editRequested({ kind: 'connect', diagramId: this.#activeDiagramId, connectorKind, sourceRef, targetRef, waypoints });
      },
      connectorRejected: (code, message, elementId) => {
        void bridge.editRejected({ code, message, elementId });
      },
    });

    // Keeps Sequence and Message Flows right-angled and Manhattan-routed while anything moves.
    this.#live = config.isReadOnly ? null : new LiveRouting(this.#paperHost.paper, config.routing);

    this.#resize = config.isReadOnly ? null : new ResizeController({
      resizeCommitted: (elementId, bounds) => {
        // The routes are worked out against the new size now, because the preview is all the
        // canvas has drawn and the document will not route anything itself.
        const routes = this.#live?.routesAfterResize(elementId, bounds) ?? [];
        void bridge.editRequested({ kind: 'resize', diagramId: this.#activeDiagramId, elementId, bounds, routes });
      },
    });

    // Not built in read-only: with no editing there is nothing a splitter could ask for.
    this.#laneSplitters = config.isReadOnly ? null : new LaneSplitterController(this.#paperHost.paper, {
      splitterMoved: (elementId, position) => {
        void bridge.editRequested({ kind: 'laneSplitter', elementId, position });
      },
    });

    this.#linkEdit = config.isReadOnly ? null : new LinkEditController(config.grid, {
      reconnected: (elementId, sourceRef, targetRef, waypoints) => {
        void bridge.editRequested({ kind: 'reconnect', diagramId: this.#activeDiagramId, elementId, sourceRef, targetRef, waypoints });
      },
      rerouted: (elementId, waypoints) => {
        void bridge.editRequested({ kind: 'reroute', diagramId: this.#activeDiagramId, elementId, waypoints });
      },
      removed: (elementId) => {
        void bridge.editRequested({ kind: 'delete', elementIds: [elementId] });
      },
      reconnectRejected: (code, message, elementId) => {
        void bridge.editRejected({ code, message, elementId });
      },
    }, connect, this.#live).attach(this.#paperHost.paper);

    // Any point on a connector's line takes hold of the run under it, so there are no bend handles.
    this.#segmentDrag = config.isReadOnly ? null : new SegmentDragController(config.grid, {
      rerouted: (elementId, waypoints) => {
        void bridge.editRequested({ kind: 'reroute', diagramId: this.#activeDiagramId, elementId, waypoints });
      },
    }, this.#live).attach(this.#paperHost.paper);

    /*
     * A file dropped on the canvas is offered, never opened. What it contains and whether it is
     * worth replacing the open document with are the editor's questions, and a drop that silently
     * swapped the file would be the worst possible answer to an accidental one.
     */
    this.#fileDrop = new FileDropTarget(hostElement, (file) => {
      void bridge.fileDropped(file);
    });
    this.#fileDrop.attach();

    /*
     * The stencil sits beside the canvas, not inside it, so this listens on the editor as a whole
     * — and falls back to the canvas host when there is no editor around it, where it simply never
     * sees a stencil tool. It is the canvas's to own because it is JavaScript, and nothing but the
     * canvas talks to JavaScript.
     */
    this.#releaseNativeMenu = suppressNativeMenu(hostElement.closest<HTMLElement>('.bpmn-editor') ?? hostElement);
    this.#releaseToolbarColors = positionToolbarColors(hostElement.closest<HTMLElement>('.bpmn-editor') ?? hostElement);
    this.#stencilDrag = new StencilDragImage(hostElement.closest('.bpmn-editor') ?? hostElement);

    this.#interaction = new InteractionController(this.#paperHost.paper, config.editing, {
      selectionChanged: (elementIds) => {
        // The anchor rides along with the selection so the contextual-action surface knows where
        // to draw itself without a second interop call. Only for a single element: a rectangle
        // around several would put the actions beside none of them.
        const single = elementIds.length === 1 ? elementIds[0] : undefined;
        const anchor = single === undefined ? null : this.#anchorOf(single);
        this.#anchorReporter?.selectionReported(anchor);
        bridge.selectionChanged({ elementIds, anchor });
      },
      moveCommitted: (moves, routes) => {
        void bridge.editRequested({ kind: 'move', diagramId: this.#activeDiagramId, moves, routes });
      },
      moveRejected: (code, message, elementId) => {
        void bridge.editRejected({ code, message, elementId });
      },
      deleteRequested: (elementIds) => {
        void bridge.editRequested({ kind: 'delete', elementIds });
      },
      reparentRequested: (elementId, containerId, laneId, bounds) => {
        void bridge.editRequested({ kind: 'reparent', elementId, containerId, laneId, bounds });
      },
    }, !config.isReadOnly, this.#resize, this.#linkEdit, this.#live);

    /*
     * Not built at all in read-only. The band is a selection gesture and selection is retained
     * there, but the only reason to drag one is to act on what it catches; leaving the modifier
     * inert keeps Shift-drag panning where nothing can be done with a selection anyway.
     */
    this.#rubberBand = config.isReadOnly ? null : new RubberBandController(this.#paperHost.paper, {
      selected: (ids) => { this.#interaction.setSelection(ids); },
    });

    this.#anchors = new AnchorResolver(hostElement, this.#paperHost.paper);

    this.#anchorReporter = new SelectionAnchorReporter(this.#paperHost.paper.el, {
      selection: () => this.#interaction.selection,
      anchorOf: (elementId) => this.#anchorOf(elementId),
      report: (elementId, anchor) => { bridge.selectionAnchorChanged({ elementId, anchor }); },
    }).attach();
    this.#paperHost.graph.on('change:position change:size', this.#handleGeometryChanged);

    /*
     * Built in read-only too. Section 7 makes the suppression unconditional, and the menu's
     * inspection and navigation commands are precisely what read-only keeps — a right-click that
     * dropped back to the browser's own menu there would be the one gap the rule exists to close.
     */
    this.#contextMenu = new ContextMenuController(
      hostElement,
      this.#paperHost.paper,
      config.editing.longPressMilliseconds,
      {
        requested: (payload) => { void bridge.contextMenuRequested(payload); },
        selection: () => this.#interaction.selection,
      },
    ).attach();

    // Editing a label changes the document, so read-only does not offer it. Inspection of the text
    // is what the snapshot in a context menu is for.
    this.#labelEdit = new LabelEditController(hostElement, this.#paperHost.paper, {
      editRequested: (payload) => { void bridge.labelEditRequested(payload); },
      textOf: (elementId) => this.#textOf(elementId),
    });

    if (!config.isReadOnly) this.#labelEdit.attach();

    /*
     * Expanding and collapsing a Sub-Process, in both modes.
     *
     * In Edit mode it is a document change, so the request goes to the host and comes back as a
     * new document. In Readonly it is an inspection toggle the canvas keeps to itself (section
     * 1.2.1) — wanting to see inside a closed container is a reading activity, and a reviewer
     * should not need write access to find out what a Sub-Process contains.
     *
     * The buttons go in the element Blazor renders for them, beside the paper rather than inside
     * it: a control the accessible tree has to be able to name cannot live under aria-hidden.
     */
    this.#expandControls = new ExpandControls(
      this.#paperHost.paper,
      hostElement.querySelector('.bpmn-canvas__controls') ?? hostElement,
      {
        expandRequested: (elementId) => { void bridge.editRequested({ kind: 'expand', elementId }); },
        collapseRequested: (elementId) => { void bridge.editRequested({ kind: 'collapse', elementId }); },
      },
      config.strings,
    );

    this.#expandControls.attach();

    // Read-only is not consulted: an execution overlay changes nothing about the document, and
    // watching a process run is exactly what a read-only editor is for.
    this.#execution = new ExecutionOverlay(this.#paperHost.paper, config.strings);
  }

  /** Draws an engine's view of a running instance over the diagram. */
  public setExecutionState(state: ExecutionStateContract): void {
    this.#assertUsable();
    this.#execution.set(state);
  }

  /** Removes every execution overlay. */
  public clearExecutionState(): void {
    this.#assertUsable();
    this.#execution.clear();
  }

  /**
   * The text a label editor should open with.
   *
   * Read from the resident document rather than from the rendered cell: what the canvas drew is
   * the wrapped text, with line breaks this side inserted to fit the current box. Editing that
   * would bake the box width into the element's name.
   */
  #textOf(elementId: string): string | null {
    const document = this.#document;
    if (document === null) return null;

    const annotation = document.textAnnotations.find((candidate) => candidate.id === elementId);
    if (annotation !== undefined) return annotation.text;

    const named = [
      ...document.flowNodes,
      ...document.sequenceFlows,
      ...document.messageFlows,
      ...document.groups,
      ...document.dataObjectReferences,
      ...document.dataStoreReferences,
      ...document.lanes,
      ...(document.collaboration?.participants ?? []),
    ].find((candidate) => candidate.id === elementId);

    return named === undefined ? null : named.name ?? '';
  }

  /** Opens the context menu for a target the host resolved — the keyboard path from Blazor. */
  public requestContextMenu(elementId: string | null, source: ContextMenuSource): void {
    this.#assertUsable();
    this.#contextMenu.request(elementId, source);
  }

  /** Opens the inline label editor for one element. False when it has no editable label. */
  public editLabel(elementId: string): boolean {
    this.#assertUsable();
    return this.#labelEdit.edit(elementId);
  }

  /** Where an element is, in both spaces, or null when it is not drawn on the active plane. */
  public anchorOf(elementId: string): AnchorContract | null {
    this.#assertUsable();
    return this.#anchorOf(elementId);
  }

  #anchorOf(elementId: string): AnchorContract | null {
    const cell = findCell(this.#paperHost.graph, elementId);
    if (cell === undefined) return null;

    const view = findView(this.#paperHost.paper, cell);
    if (view === undefined) return null;

    // Selection tools expand the SVG root bounds after rendering. Anchor shapes to their
    // geometry so the delayed refresh does not move the actions when those tools appear.
    return cell.isElement()
      ? this.#anchors.ofLocalRect(cell.getBBox())
      : this.#anchors.ofRect(view.el.getBoundingClientRect());
  }

  /** The currently selected cell ids. */
  public get selection(): readonly string[] {
    return this.#interaction.selection;
  }

  /**
   * Resolves a stencil drop into paper geometry and an owning container.
   *
   * The host knows which tool was dragged; only the canvas knows the paper coordinates, the zoom
   * and which container the point fell inside. Blazor turns the answer into a creation command, so
   * creation stays one transaction owned by the model side.
   */
  public resolveDrop(clientX: number, clientY: number, elementType: string): DropTargetContract {
    this.#assertUsable();
    return resolveDrop(this.#paperHost.paper, clientX, clientY, elementType, this.config.grid);
  }

  /**
   * Where a keyboard-armed stencil tool should be created.
   *
   * The same resolution as a drop, so a tool armed from the keyboard and one dragged onto the
   * canvas travel identical paths from here on.
   */
  public resolveCreationPoint(elementType: string): DropTargetContract {
    this.#assertUsable();
    return resolveCreationPoint(this.#paperHost.paper, elementType, this.config.grid);
  }

  /**
   * Where an element appended to another one should be created.
   *
   * The same resolution a drop gets — default size, container under the point, nudged clear of
   * collisions — so a contextual append and a dragged tool produce the same kind of command.
   */
  public resolveAppendPoint(sourceId: string, elementType: string): DropTargetContract | null {
    this.#assertUsable();
    return resolveAppendPoint(this.#paperHost.paper, sourceId, elementType, this.config.grid);
  }

  /** Replaces the selection from the host side. */
  public setSelection(elementIds: readonly string[]): void {
    this.#assertUsable();
    this.#interaction.setSelection(elementIds);
  }

  /**
   * Turns a shortcut into either a viewport command or a report to Blazor.
   *
   * The split is ownership, not convenience: panning and zooming are the canvas's to do, while the
   * shortcut help surface is chrome, and the canvas has no business drawing a dialog inside its own
   * SVG.
   */
  #runShortcut(command: ShortcutCommand): void {
    const step = this.config.keyboard.panStep;

    // Read-only keeps selection, inspection and navigation and loses every mutation
    // (specification section 7). Disabling the paper's drag handling covers the pointer; the
    // keyboard is a separate door into the same mutations and has to be closed too.
    if (this.config.isReadOnly && MUTATING_COMMANDS.has(command)) return;

    switch (command) {
      case 'zoomIn':
        this.#viewport.execute({ kind: 'zoomIn' });
        break;
      case 'zoomOut':
        this.#viewport.execute({ kind: 'zoomOut' });
        break;
      case 'resetZoom':
        this.#viewport.execute({ kind: 'reset' });
        break;
      case 'fitToContent':
        this.#viewport.execute({ kind: 'fitToContent' });
        break;
      // The canvas moves the opposite way to the intent: panning the view left means
      // translating the content right.
      case 'panLeft':
        this.#viewport.execute({ kind: 'panBy', dx: step, dy: 0 });
        break;
      case 'panRight':
        this.#viewport.execute({ kind: 'panBy', dx: -step, dy: 0 });
        break;
      case 'panUp':
        this.#viewport.execute({ kind: 'panBy', dx: 0, dy: step });
        break;
      case 'panDown':
        this.#viewport.execute({ kind: 'panBy', dx: 0, dy: -step });
        break;
      case 'selectAll':
        this.#interaction.selectAll();
        break;
      case 'clearSelection':
        this.#interaction.clearSelection();
        break;
      case 'delete':
        // A request, not an edit: the document belongs to the host, which applies the cascade and
        // sends back what is left.
        this.#interaction.deleteSelection();
        break;
      // Undo and redo are the host's history, so they travel out the same way a delete does.
      case 'undo':
        void this.#bridge.editRequested({ kind: 'undo' });
        break;
      case 'redo':
        void this.#bridge.editRequested({ kind: 'redo' });
        break;
      /*
       * The clipboard lives on the host, with the document. Sending the selection rather than
       * holding a fragment here keeps one model of record: the canvas is a projection, and a
       * projection cannot be the authority on what a copied Sub-Process contains.
       */
      case 'copy':
      case 'cut':
      case 'duplicate':
        void this.#bridge.editRequested({ kind: command, elementIds: this.#interaction.selection });
        break;
      case 'paste':
        void this.#bridge.editRequested({ kind: 'paste' });
        break;
      case 'showShortcuts':
        void this.#bridge.shortcutInvoked({ command });
        break;
    }
  }

  /** Replaces the rendered document and reports what was drawn. */
  public loadDocument(document: BpmnDocumentContract, diagramId: string | null): DocumentLoadedPayload {
    this.#assertUsable();
    this.#document = document;

    const result = renderDocument(this.#paperHost.graph, document, diagramId, this.#metrics);
    this.#activeDiagramId = result.diagramId;
    this.#accessibility.describe(result.diagramId, result.shapeCount, result.edgeCount);

    // Re-rendering replaces the cell views the selection outlines were attached to, so the
    // surviving part of the selection has to be drawn again against the new ones.
    this.#interaction.refresh();
    this.#laneSplitters?.draw();

    // A redraw can put the selected element somewhere else — a move the host had to adjust, a
    // container that grew — so the strip beside it is re-anchored against what was just drawn.
    this.#anchorReporter?.scheduleShow();

    // And the same for the execution overlay, which is what makes it survive a switch to another
    // plane and back rather than being something the host has to push again.
    this.#execution.draw();

    return {
      diagramId: result.diagramId,
      shapeCount: result.shapeCount,
      edgeCount: result.edgeCount,
      unrenderedElementIds: result.unrenderedElementIds,
    };
  }

  /** Applies a host-confirmed deletion without rebuilding unaffected cells. */
  public deleteElements(elementIds: readonly string[]): boolean {
    this.#assertUsable();
    const document = this.#document;
    if (document === null || this.#activeDiagramId === null) return false;

    const updated = removeDocumentElements(this.#paperHost.graph, document, elementIds);
    if (updated === null) return false;

    this.#document = updated;
    this.#accessibility.describe(
      this.#activeDiagramId,
      this.#paperHost.graph.getElements().length,
      this.#paperHost.graph.getLinks().length,
    );
    this.#interaction.refresh();
    this.#anchorReporter?.scheduleShow();
    this.#execution.draw();
    return true;
  }

  /** Switches planes without discarding the resident model. */
  public setActiveDiagram(diagramId: string): DocumentLoadedPayload | null {
    this.#assertUsable();
    if (this.#document === null) return null;
    return this.loadDocument(this.#document, diagramId);
  }

  public setViewport(command: ViewportCommand): void {
    this.#assertUsable();
    this.#viewport.execute(command);
  }

  public get activeDiagramId(): string | null {
    return this.#activeDiagramId;
  }

  /**
   * Moves one element, as a drag eventually will.
   *
   * Exists so alignment guides can be exercised before the editing interactions that will drive
   * them: snapping listens to position changes, not to a drag handler, so a programmatic move
   * goes through exactly the same path a pointer will. Reached only through the debug-only
   * module hook.
   */
  public moveElement(elementId: string, x: number, y: number): boolean {
    this.#assertUsable();

    // Searched among the elements rather than fetched by id, so an id that names a link or
    // nothing at all comes back as "not found" without a cast.
    const element = findElement(this.#paperHost.graph, elementId);
    if (element === undefined) return false;

    element.position(x, y);
    return true;
  }

  /** Idempotent: circuit teardown and explicit disposal can both reach this. */
  public dispose(): void {
    if (this.#disposed) return;
    this.#disposed = true;

    // Every step runs even if an earlier one throws: a controller that fails to let go must not leave the ones after it, and the window listeners some of
    // them hold, attached to a canvas nobody owns any more. The failures are reported once all of them have run.
    const steps: (() => void)[] = [
      () => { this.#contextMenu.dispose(); },
      () => { this.#paperHost.graph.off('change:position change:size', this.#handleGeometryChanged); },
      () => { this.#anchorReporter?.destroy(); },
      () => { this.#anchorReporter = null; },
      () => { this.#stencilDrag.destroy(); },
      () => { this.#execution.destroy(); },
      () => { this.#labelEdit.dispose(); },
      () => { this.#expandControls.dispose(); },
      () => { this.#linkEdit?.destroy(); },
      () => { this.#segmentDrag?.destroy(); },
      () => { this.#resize?.destroy(); },
      () => { this.#laneSplitters?.destroy(); },
      () => { this.#releaseNativeMenu(); },
      () => { this.#releaseToolbarColors(); },
      () => { this.#rubberBand?.destroy(); },
      () => { this.#connect.destroy(); },
      () => { this.#interaction.destroy(); },
      () => { this.#fileDrop.detach(); },
      () => { this.#shortcuts.destroy(); },
      () => { this.#snapping.destroy(); },
      () => { this.#accessibility.destroy(); },
      () => { this.#viewport.destroy(); },
      () => { this.#paperHost.destroy(); },
      () => { this.#bridge.dispose(); },
    ];

    const failures: unknown[] = [];
    for (const step of steps) {
      try {
        step();
      } catch (failure) {
        failures.push(failure);
      }
    }
    this.#document = null;

    if (failures.length === 1) throw failures[0];
    if (failures.length > 1) throw new AggregateError(failures, 'Several parts of the canvas failed to release.');
  }

  #assertUsable(): void {
    if (this.#disposed) {
      throw new Error(`BPMN canvas instance '${this.id}' has been disposed.`);
    }
  }
}
