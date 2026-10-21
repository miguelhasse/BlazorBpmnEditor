import type { dia } from '@joint/core';

import { projectToOutline } from '../geometry/outline.js';
import { findCell, findElement, positionOf } from '../graph/cells.js';
import { isLane } from '../graph/lane.js';
import { outlineFor } from './endpoint-anchor.js';
import type {
  EditingOptionsContract,
  GeometryBounds,
  ConnectorRouteContract,
  ShapeGeometryContract,
} from '../interop/contracts.js';
import { ownerUnder } from './drop-resolver.js';
import { findCollision, type CollisionCandidate } from './collision.js';
import { isProvisional } from './provisional.js';
import type { LiveRouting } from './live-routing.js';
import type { LinkEditController } from './link-tools.js';
import type { ResizeController } from './resize-tool.js';
import { Selection, type SelectionMode } from './selection.js';
import { SelectionView } from './selection-view.js';

/** What a press must not land on to count as blank paper. */
const NOT_BLANK = '.joint-cell, .joint-tools, .joint-highlight-mask, .bpmn-lane-splitters';

/**
 * What the controller asks the host to do, once a gesture has finished.
 */
export interface EditingCallbacks {
  readonly selectionChanged: (ids: readonly string[]) => void;
  readonly moveCommitted: (
    moves: readonly ShapeGeometryContract[],
    routes: readonly ConnectorRouteContract[],
  ) => void;
  readonly moveRejected: (code: string, message: string, elementId: string) => void;
  readonly deleteRequested: (elementIds: readonly string[]) => void;

  /** A drag ended over a different container, so the element should change owner. */
  readonly reparentRequested: (
    elementId: string,
    containerId: string,
    laneId: string | null,
    bounds: GeometryBounds,
  ) => void;
}

/**
 * Selection and element dragging.
 *
 * Dragging itself is JointJS's — `interactive` on the paper already moves an element and its
 * embedded descendants correctly, and reimplementing that would be reimplementing the framework.
 * What is here is the part JointJS has no opinion about: which elements are selected, whether the
 * result of a drag is allowed, and what to tell the host when it is.
 *
 * Moves are applied optimistically and reported afterwards. A drag that had to round-trip through
 * interop for permission on every frame would lag the pointer; instead the geometry is checked at
 * pointer-up and either committed or rolled back to what was captured at pointer-down. Because the
 * check happens before anything is reported, the document never sees a rejected position.
 */
export class InteractionController {
  readonly #paper: dia.Paper;
  readonly #graph: dia.Graph;
  readonly #options: EditingOptionsContract;
  readonly #callbacks: EditingCallbacks;
  readonly #view: SelectionView;

  /** Keeps flows right-angled and Manhattan-routed while things move; null when nothing is editable. */
  readonly #live: LiveRouting | null;

  #selection = Selection.empty;

  /** Where the dragged elements were when the gesture started, for rollback and for reporting. */
  #dragOrigin: Map<string, { x: number; y: number }> | null = null;


  /**
   * The element the pointer is actually on, and where it was last seen.
   *
   * JointJS moves the element under the pointer and its embedded cells, and nothing else. Carrying
   * the rest of the selection along means watching that one element and translating its companions
   * by the same amount.
   */
  #dragLeader: { id: string; at: { x: number; y: number } } | null = null;

  /** Guards the re-entrancy: translating a companion fires the same event that moved the leader. */
  #translating = false;

  /**
   * @param paper The paper to bind to.
   * @param options The accepted editing options.
   * @param callbacks What to tell the host when a gesture finishes.
   * @param allowConnectors
   * Whether a selected element offers a connector-initiation handle. False in read-only.
   * @param resize Builds the resize handles, or null when resizing is not offered.
   * @param linkEdit Builds the connector tools, or null when connectors are not editable.
   */
  public constructor(
    paper: dia.Paper,
    options: EditingOptionsContract,
    callbacks: EditingCallbacks,
    allowConnectors: boolean,
    resize: ResizeController | null = null,
    linkEdit: LinkEditController | null = null,
    live: LiveRouting | null = null,
  ) {
    this.#live = live;
    this.#paper = paper;
    this.#graph = paper.model;
    this.#options = options;
    this.#callbacks = callbacks;
    this.#view = new SelectionView(paper, allowConnectors, resize, linkEdit);

    paper.on('element:pointerdown', this.#handleElementDown);
    paper.on('link:pointerdown', this.#handleLinkDown);
    paper.on('blank:pointerdown', this.#handleBlankDown);
    paper.el.addEventListener('pointerdown', this.#handleBlankPress);
    paper.on('element:pointerup', this.#handleElementUp);
    this.#graph.on('remove', this.#handleRemoved);
    this.#graph.on('change:position', this.#handleMoved);
  }

  public get selection(): readonly string[] {
    return this.#selection.ids;
  }

  /** Replaces the selection from outside — a host command, or a reload. */
  public setSelection(ids: readonly string[]): void {
    this.#apply(Selection.of(ids).retaining((id) => findCell(this.#graph, id) !== undefined));
  }

  public selectAll(): void {
    this.#apply(Selection.of(this.#graph.getElements().map((element) => String(element.id))));
  }

  public clearSelection(): void {
    this.#apply(this.#selection.clear());
  }

  /** Asks the host to delete the selection. The document is not this side's to change. */
  public deleteSelection(): void {
    if (this.#selection.isEmpty) return;
    this.#callbacks.deleteRequested(this.#selection.ids);
  }

  /** Re-draws the selection outlines, after a render replaced the views they were attached to. */
  public refresh(): void {
    this.#apply(this.#selection.retaining((id) => findCell(this.#graph, id) !== undefined), true);
  }

  public destroy(): void {
    this.#live?.end(true);
    this.#paper.off('element:pointerdown', this.#handleElementDown);
    this.#paper.off('link:pointerdown', this.#handleLinkDown);
    this.#paper.off('blank:pointerdown', this.#handleBlankDown);
    this.#paper.el.removeEventListener('pointerdown', this.#handleBlankPress);
    this.#paper.off('element:pointerup', this.#handleElementUp);
    this.#graph.off('remove', this.#handleRemoved);
    this.#graph.off('change:position', this.#handleMoved);
    this.#view.destroy();
  }

  // --- pointer --------------------------------------------------------------

  readonly #handleElementDown = (view: dia.ElementView, event: dia.Event): void => {
    this.#apply(this.#selection.apply(String(view.model.id), modeOf(event)));
    this.#captureDragOrigin();

    const leader = findElement(this.#graph, String(view.model.id));
    this.#dragLeader = leader === undefined
      ? null
      : { id: String(leader.id), at: positionOf(leader) };
  };

  /**
   * Carries the rest of the selection along with the element being dragged.
   *
   * JointJS moves what is under the pointer and its embedded cells; the selection is this module's
   * idea, so moving the other members of it is too. Only the selected *roots* are translated,
   * because JointJS already moves a container's contents with it — translating a child as well
   * would move it twice.
   */
  /**
   * Keeps a dragged Boundary Event on its host Activity's outline.
   *
   * DI-010 requires the Event's centre to lie on the outline, and the editor enforced that when it
   * was created and again when the host moved — but not while the Event itself was being dragged,
   * so it could be pulled anywhere on the canvas and only snapped back when the drag ended. What
   * the user saw during the gesture was an Event floating free of the shape it belongs to.
   *
   * Projecting on every position change makes the constraint the gesture itself: the Event slides
   * along the border under the pointer and cannot be anywhere else, which is also the only honest
   * preview of where releasing will put it.
   *
   * @param cell The cell whose position changed.
   * @returns Whether this was a Boundary Event, and so handled here.
   */
  #pinToHost(cell: dia.Cell): boolean {
    if (this.#translating || !cell.isElement()) return false;

    const type = (cell.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType;
    if (type !== 'boundaryEvent') return false;

    const host = cell.getParentCell();
    if (host?.isElement() !== true) return true;

    /*
     * Not while the host is the thing being dragged.
     *
     * Moved by its host, a Boundary Event keeps the place on the border the user put it — the host
     * carries it as an embedded child and the offset is already right. Projecting it again here
     * would answer a different question, "which point of the outline is nearest now", and slide it
     * along the border for the rest of the gesture. Dragged on its own it is projected, which is
     * the case this method exists for.
     */
    if (this.#dragOrigin?.has(String(host.id)) === true) return true;

    const size = cell.size();
    const position = cell.position();
    const centre = { x: position.x + (size.width / 2), y: position.y + (size.height / 2) };
    const pinned = projectToOutline(centre, outlineFor(host));

    const x = pinned.x - (size.width / 2);
    const y = pinned.y - (size.height / 2);

    // Only when it actually moves: setting the same position would loop through this handler.
    if (Math.abs(x - position.x) < 0.01 && Math.abs(y - position.y) < 0.01) return true;

    this.#translating = true;
    try {
      cell.position(x, y);
    } finally {
      this.#translating = false;
    }

    return true;
  }

  readonly #handleMoved = (cell: dia.Cell, _position?: unknown, options?: { readonly reconcile?: boolean }): void => {
    // A position the document dictated (an undo, a reload) is not the user moving anything.
    if (options?.reconcile === true) return;

    // Before anything else: a Boundary Event never leaves its host's border, mid-drag included.
    if (this.#pinToHost(cell)) return;

    const leader = this.#dragLeader;
    if (leader === null || this.#translating || String(cell.id) !== leader.id) return;

    const element = findElement(this.#graph, leader.id);
    if (element === undefined) return;

    const now = positionOf(element);
    const dx = now.x - leader.at.x;
    const dy = now.y - leader.at.y;

    this.#dragLeader = { id: leader.id, at: now };

    if (dx === 0 && dy === 0) return;

    /*
     * The flows attached to what is moving are handed to the Manhattan router for the rest of the
     * gesture, so the gesture never shows a diagonal it is about to correct. Started here rather
     * than on pointer-down, so a click that never moves anything leaves every route alone.
     */
    if (this.#live !== null && this.#dragOrigin !== null && !this.#live.isActive) {
      this.#live.beginForElements(new Set(this.#dragOrigin.keys()));
    }

    const companions = this.#selectedRoots().filter((candidate) => String(candidate.id) !== leader.id);

    if (companions.length === 0) {
      this.#live?.carry();
      return;
    }

    this.#translating = true;
    try {
      // One batch, so the paper repaints the whole group once rather than once per member.
      this.#graph.startBatch('bpmn-group-move');
      for (const companion of companions) companion.translate(dx, dy);
    } finally {
      this.#graph.stopBatch('bpmn-group-move');
      this.#translating = false;
    }

    // Once every member has moved: flows with both ends among the moving elements keep their route
    // and travel with them, and are measured against where the ends actually are.
    this.#live?.carry();
  };

  /**
   * The selected elements that no other selected element contains.
   *
   * A container and its child cannot both be roots: JointJS translates the child with the parent,
   * so treating both as roots moves the child twice as far as the pointer went.
   */
  #selectedRoots(): readonly dia.Element[] {
    const selected = new Set(this.#selection.ids);

    return this.#selection.ids
      .map((id) => findElement(this.#graph, id))
      .filter((element) => element !== undefined)
      // A Lane only ever moves with its Pool, so it is never carried along by another drag.
      .filter((element) => !isLane(element))
      .filter((element) => !element.getAncestors().some((ancestor) => selected.has(String(ancestor.id))));
  }

  readonly #handleLinkDown = (view: dia.LinkView, event: dia.Event): void => {
    /*
     * The link a connector gesture is drawing fires this the instant it is added to the graph.
     * Selecting it would clear the source element's selection and remove its tools — including the
     * handle the drag is being driven from — so the gesture would die on its first movement.
     */
    if (isProvisional(view.model)) return;

    // Connectors are selectable but not draggable in this milestone: moving a route is a
    // waypoint edit, which is its own command.
    this.#apply(this.#selection.apply(String(view.model.id), modeOf(event)));
  };

  readonly #handleBlankDown = (): void => {
    this.clearSelection();
  };

  /**
   * A press on blank paper clears the selection, whether or not it goes on to pan.
   *
   * JointJS raises `blank:pointerdown` from `mousedown`, and a mouse press on blank paper is now
   * cancelled at `pointerdown` (so the browser starts no native drag or text selection), which
   * suppresses the `mousedown` that follows. This listens to the pointer event itself. Touch still
   * arrives through JointJS, and clearing twice is harmless.
   *
   * What counts as blank is the same as for panning: not a cell, not one of a selected cell's tools
   * or outlines, and not a lane splitter. Shift is the rubber band's, which selects rather than
   * clears.
   */
  readonly #handleBlankPress = (event: PointerEvent): void => {
    if (event.button !== 0 || event.shiftKey) return;
    if (event.target instanceof Element && event.target.closest(NOT_BLANK) !== null) return;

    this.clearSelection();
  };

  readonly #handleElementUp = (): void => {
    const origin = this.#dragOrigin;
    this.#dragOrigin = null;
    this.#dragLeader = null;
    if (origin === null) {
      this.#live?.end(true);
      return;
    }

    const moved = [...origin.keys()].filter((id) => {
      const element = findElement(this.#graph, id);
      if (element === undefined) return false;

      const now = positionOf(element);
      const before = origin.get(id);
      return before !== undefined && (now.x !== before.x || now.y !== before.y);
    });

    if (moved.length === 0) {
      this.#live?.end(true);
      return;
    }

    /*
     * A drag that ended somewhere else's container is a reparent, not a move: section 2.8.1 wants
     * ownership, Lane membership, DI and embedding changed together, and reporting it as a plain
     * move would change the geometry while leaving the element owned by the container it just
     * visibly left.
     */
    const reparent = this.#reparentFor(moved, origin);

    if (reparent !== null) {
      // The host answers a reparent with a document, and the routes are its to keep.
      this.#live?.end(true);
      this.#callbacks.reparentRequested(
        reparent.elementId,
        reparent.containerId,
        reparent.laneId,
        reparent.bounds,
      );
      return;
    }

    const collision = this.#options.preventCollisions
      ? findCollision(this.#candidates(), new Set(moved), this.#options.collisionClearance)
      : null;

    if (collision !== null) {
      this.#live?.end(true);
      this.#rollback(origin);
      this.#callbacks.moveRejected(
        'DI-011',
        `This position would overlap '${collision.peerId}'.`,
        collision.id,
      );
      return;
    }

    // What was drawn is what is stored: the routes are read off the flows the gesture owned, and
    // left on screen until the document comes back.
    const routes = this.#live?.routes() ?? [];
    this.#live?.end(false);

    this.#callbacks.moveCommitted(this.#geometryOf(moved), routes);
  };

  /**
   * Whether a finished drag left its element in a different container.
   *
   * Only for a single element. Reparenting a multi-selection means one command per element, each
   * of which can be refused on its own — a half-applied group reparent is exactly the partial
   * state CT-008 forbids, so the group case waits for a compound command to carry it.
   *
   * Two drops are deliberately left to the move: one into another Pool, which the host transfers
   * there together with the connectors that would otherwise cross its boundary, and any Group,
   * which is an Artifact that may sit anywhere in its container without changing what it is in.
   */
  #reparentFor(moved: readonly string[], origin: ReadonlyMap<string, { x: number; y: number }>): {
    elementId: string;
    containerId: string;
    laneId: string | null;
    bounds: GeometryBounds;
  } | null {
    const only = moved.length === 1 ? moved[0] : undefined;
    if (only === undefined) return null;

    const element = findElement(this.#graph, only);
    if (element === undefined) return null;

    const bpmn = element.get('bpmn') as
      { readonly elementType?: string; readonly containerId?: string | null } | undefined;

    // A Boundary Event belongs to its host, and a container dragged as a whole keeps its own
    // owner — neither is a reparent, whatever it was dropped on.
    if (bpmn?.elementType === undefined || bpmn.elementType === 'boundaryEvent') return null;
    if (bpmn.elementType === 'group') return null;

    const { x, y } = element.position();
    const { width, height } = element.size();
    const centre = { x: x + (width / 2), y: y + (height / 2) };

    const before = origin.get(only);
    if (before !== undefined) {
      const targetPool = this.#poolAt(centre, only);
      const sourcePool = this.#poolAt({ x: before.x + (width / 2), y: before.y + (height / 2) }, only);
      // Content that was in no Pool (a Collaboration-level Artifact) is still reparented into one.
      if (sourcePool !== null && targetPool !== null && targetPool !== sourcePool) return null;
    }

    const owner = ownerUnder(this.#graph, centre, { width, height }, only);

    // Nothing recognisable underneath, or the same container it already had.
    if (owner.containerId === null && owner.laneId === null) return null;
    if (owner.containerId === (bpmn.containerId ?? null) && owner.laneId === null) return null;

    return {
      elementId: only,
      containerId: owner.containerId ?? bpmn.containerId ?? '',
      laneId: owner.laneId,
      bounds: { x, y, width, height },
    };
  }

  /** The id of the Pool drawn under a point, other than the element itself, or null. */
  #poolAt(point: { x: number; y: number }, exclude: string): string | null {
    for (const candidate of this.#graph.getElements()) {
      if (String(candidate.id) === exclude) continue;

      const bpmn = candidate.get('bpmn') as { readonly elementType?: string } | undefined;
      if (bpmn?.elementType !== 'pool') continue;

      const { x, y } = positionOf(candidate);
      const { width, height } = candidate.size();
      if (point.x >= x && point.x <= x + width && point.y >= y && point.y <= y + height) {
        return String(candidate.id);
      }
    }

    return null;
  }

  /**
   * Records where everything that could move is, before it does.
   *
   * Every selected element and all of their descendants, because dragging a Pool drags its
   * contents and a rollback has to put all of them back — not just the one under the pointer.
   */
  #captureDragOrigin(): void {
    const origin = new Map<string, { x: number; y: number }>();

    for (const id of this.#selection.ids) {
      const cell = findElement(this.#graph, id);
      if (cell === undefined) continue;

      for (const element of [cell, ...cell.getEmbeddedCells({ deep: true })]) {
        if (!element.isElement()) continue;
        origin.set(String(element.id), positionOf(element));
      }
    }

    this.#dragOrigin = origin;
    this.#live?.prepareAnnotations(new Set(origin.keys()));
  }

  #rollback(origin: Map<string, { x: number; y: number }>): void {
    // One batch, so the paper repaints once and the rejected position never appears.
    this.#graph.startBatch('bpmn-rollback');
    try {
      for (const [id, position] of origin) {
        findElement(this.#graph, id)?.position(position.x, position.y);
      }

    } finally {
      this.#graph.stopBatch('bpmn-rollback');
    }
  }

  #candidates(): CollisionCandidate[] {
    return this.#graph.getElements().map((element) => {
      const bpmn = element.get('bpmn') as
        { readonly elementType?: string; readonly containerId?: string | null } | undefined;

      const { x, y } = positionOf(element);
      const { width, height } = element.size();
      const type = bpmn?.elementType ?? '';

      return {
        id: String(element.id),
        containerId: bpmn?.containerId ?? null,
        box: { x, y, width, height },
        // Containers enclose their content and Boundary Events sit on their host's outline, so
        // neither can be judged by overlap. A Group is an Artifact drawn around things, and DI-011
        // governs Flow Nodes only.
        participates: type !== 'boundaryEvent' && type !== 'group' && element.getEmbeddedCells().length === 0,
      };
    });
  }

  /** The committed geometry of the moved elements, keyed by their DI shape ids. */
  #geometryOf(ids: readonly string[]): ShapeGeometryContract[] {
    const geometry: ShapeGeometryContract[] = [];

    for (const id of ids) {
      const element = findElement(this.#graph, id);
      if (element === undefined) continue;

      const shapeId = element.get('diShapeId') as string | null | undefined;
      if (shapeId === undefined || shapeId === null) continue;

      const { x, y } = positionOf(element);
      const { width, height } = element.size();
      geometry.push({ shapeId, bounds: { x, y, width, height } });
    }

    return geometry;
  }

  readonly #handleRemoved = (): void => {
    // A render replaces some cells (one that changed kind, a connector whose end was replaced): they leave and come back inside one batch. What is
    // selected is settled once it ends, by `refresh()`, which keeps what is still drawn and says so if something truly went.
    if ((this.#graph as unknown as { hasActiveBatch(name: string): boolean }).hasActiveBatch('render')) return;

    const retained = this.#selection.retaining((id) => findCell(this.#graph, id) !== undefined);
    if (!retained.equals(this.#selection)) this.#apply(retained);
  };

  #apply(next: Selection, force = false): void {
    const changed = !next.equals(this.#selection);
    this.#selection = next;

    // Re-rendered even when unchanged on a forced refresh: the views the highlighters were
    // attached to are gone after a re-render, so the outlines have to be re-applied.
    if (changed || force) this.#view.render(next);
    if (changed) this.#callbacks.selectionChanged(next.ids);
  }
}

/** Shift or Ctrl extends the selection; a plain press replaces it. */
function modeOf(event: dia.Event): SelectionMode {
  const source = event as unknown as { shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean };
  return source.shiftKey === true || source.ctrlKey === true || source.metaKey === true
    ? 'toggle'
    : 'replace';
}
