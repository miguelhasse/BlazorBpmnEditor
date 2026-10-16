import { dia, linkTools } from '@joint/core';

import { findElement } from '../graph/cells.js';
import type { GridOptionsContract, PointContract } from '../interop/contracts.js';
import { alignToEndpoints } from '../routing/align.js';
import { isOrthogonal, type ConnectorKind } from '../routing/router.js';
import { attachmentPoint, routeWith } from './endpoint-anchor.js';
import type { LiveRouting } from './live-routing.js';
import { isProvisional } from './provisional.js';

/*
 * Destructured for the reason the other tool modules destructure it: `dia.ToolsView` is declared
 * as both a class and a namespace, and reading it through the namespace makes the lint rule
 * resolve `dia` as a type-only import, which then fails to compile.
 */
const { ToolsView } = dia;

/** What a finished connector gesture asks the host to do. */
export interface LinkEditCallbacks {
  /** An end was moved to a different element. */
  readonly reconnected: (
    connectorId: string,
    sourceRef: string,
    targetRef: string,
    waypoints: readonly PointContract[],
  ) => void;

  /** The route changed without either end moving. */
  readonly rerouted: (connectorId: string, waypoints: readonly PointContract[]) => void;

  /** The connector should go. */
  readonly removed: (connectorId: string) => void;

  /** A reconnection was refused by the live rules, and the end has snapped back. */
  readonly reconnectRejected: (code: string, message: string, connectorId: string) => void;
}

/** Where a refused reconnection finds out which rule stopped it. */
export interface RejectionSource {
  readonly takeLastRejection: () => { readonly id: string; readonly message: string } | null;
}

/**
 * The tools a selected connector offers.
 *
 * All three are JointJS's own: the two arrowheads reconnect an end, `Remove` deletes. There are no
 * bend tools: the line itself is dragged, by any point (see `segment-drag.ts`). What is added here is the BPMN
 * part: committing the result as a command, and holding the endpoints to section 4.6 so a
 * reconnected line meets its shape exactly where creation would have put it.
 *
 * Nothing is committed while the pointer is down. The gestures mutate the JointJS link freely —
 * that is how a drag looks responsive — and the document hears about it once, at the end, which is
 * also what makes one undo step undo one gesture rather than forty mouse-moves.
 */
/**
 * A grab handle for one end of a connector, drawn so the BPMN marker still reads.
 *
 * JointJS's own `SourceArrowhead` and `TargetArrowhead` are solid triangles pointing along the
 * line, and they sit exactly on the endpoints. Selecting a connector therefore put an arrow on both
 * ends of it — which is not a decoration but a statement in BPMN: a Message Flow starts with a
 * hollow circle and ends with an open arrow, an Association has no filled head at all. Selecting
 * one made every connector look like a Sequence Flow in both directions, and the symbols that say
 * what kind of connector it is were the very things covered up.
 *
 * An invisible circle instead. It is still a target big enough to grab — `pointer-events: all`
 * makes it live — and whatever the connector draws at that end shows through it. The selection
 * contour, closed around the ends, is what marks them.
 */
const endHandleAttributes = (className: string): Record<string, string | number> => ({
  'r': 9,
  'fill': 'none',
  'stroke': 'transparent',
  'stroke-width': 2,
  'pointer-events': 'all',
  'cursor': 'move',
  'class': className,
});

/**
 * `extend` is untyped in the JointJS declarations, so it is narrowed here rather than left to
 * spread `any` through the tool list — one cast at the one boundary that needs it.
 */
interface Extendable<T> { extend(properties: Record<string, unknown>): T }

const extendArrowhead = <T,>(base: T, className: string): T =>
  (base as Extendable<T>).extend({
    tagName: 'circle',
    attributes: endHandleAttributes(className),
  });

/*
 * JointJS's own class names are kept alongside ours. These are still its source and target
 * arrowhead tools — only their appearance is replaced — and that class is what the rest of the
 * world, this repo's own tests included, identifies them by.
 */
const SourceHandle = extendArrowhead(
  linkTools.SourceArrowhead,
  'source-arrowhead bpmn-endpoint-handle bpmn-endpoint-handle--source',
);

const TargetHandle = extendArrowhead(
  linkTools.TargetArrowhead,
  'target-arrowhead bpmn-endpoint-handle bpmn-endpoint-handle--target',
);

export class LinkEditController {
  readonly #callbacks: LinkEditCallbacks;
  readonly #grid: GridOptionsContract;

  #paper: dia.Paper | null = null;

  /** The ends a gesture started with, so a finished drag can tell whether they changed. */
  #before: { source: string | null; target: string | null } | null = null;

  /** Whether an end has actually been dragged since the pointer went down, as opposed to a click. */
  #dragged = false;

  /** The whole route as it was drawn when the gesture started, ends included. */
  #beforeRoute: readonly PointContract[] | null = null;

  /** Both end definitions exactly as they were, anchors included, for a gesture that is not kept. */
  #beforeEnds: { source: object; target: object } | null = null;

  readonly #rejections: RejectionSource | null;

  /** Holds the flow's route to right angles while one of its ends or bends is being dragged. */
  readonly #live: LiveRouting | null;

  /**
   * @param grid The accepted grid options, which section 4.6 makes the endpoint depend on.
   * @param callbacks What to tell the host when a gesture finishes.
   * @param rejections
   * Where to find out why a release was refused. The live rules live in the connect controller,
   * because the same `validateConnection` judges a new connector and a reconnected end.
   */
  public constructor(
    grid: GridOptionsContract,
    callbacks: LinkEditCallbacks,
    rejections: RejectionSource | null = null,
    live: LiveRouting | null = null,
  ) {
    this.#live = live;
    this.#grid = grid;
    this.#callbacks = callbacks;
    this.#rejections = rejections;
  }

  public attach(paper: dia.Paper): this {
    this.#paper = paper;

    /*
     * An arrowhead drag runs through the link view's own machinery, so the paper reports it as an
     * ordinary link pointerdown/up pair; the batch it opens marks where it starts.
     */
    paper.on('link:pointerdown', this.#handleDown);
    paper.on('link:pointerup', this.#handleUp);
    paper.model.on('batch:start', this.#handleBatch);

    return this;
  }

  public destroy(): void {
    this.#paper?.off('link:pointerdown', this.#handleDown);
    this.#paper?.off('link:pointerup', this.#handleUp);
    this.#paper?.model.off('batch:start', this.#handleBatch);
    this.#paper = null;
  }

  /** The tool set for one connector. */
  // (see EndHandle for why the arrowhead tools are replaced)
  public toolsFor(): dia.ToolsView {
    return new ToolsView({
      name: 'bpmn-link',
      tools: [
        /*
         * No bend handles. A handle on a bend lets a drag fold the route back over itself, and a
         * flow is held to right angles that a free-dragged bend cannot promise. A run is slid by
         * pressing anywhere on the line (`segment-drag.ts`), which keeps both promises; the ends
         * are moved with the handles below.
         */
        new SourceHandle(),
        new TargetHandle(),
        /*
         * Offset clear of the line. On its default placement the remove button sits *on* the
         * connector a short way from the source, which on a short flow puts it within thirty
         * pixels of the end handle — aim for the end, delete the connector. Lifting it above
         * the line keeps one gesture per place on screen.
         */
        new linkTools.Remove({
          distance: 24,
          offset: -18,
          /*
           * Named markup rather than the default. The endpoint handles are circles now too, so
           * "the first circle in the tool layer" no longer identifies this button — and that is
           * how it was being found, here and in the tests.
           */
          markup: [
            {
              tagName: 'circle',
              selector: 'button',
              attributes: {
                'r': 7,
                'fill': 'var(--bpmn-error-color)',
                'cursor': 'pointer',
                'class': 'bpmn-remove-tool',
              },
            },
            {
              tagName: 'path',
              selector: 'icon',
              attributes: {
                'd': 'M -3 -3 3 3 M -3 3 3 -3',
                'fill': 'none',
                'stroke': 'var(--bpmn-fill)',
                'stroke-width': 2,
                'pointer-events': 'none',
              },
            },
          ],
          action: (_event, view) => { this.#callbacks.removed(String(view.model.id)); },
        }),
      ],
    });
  }

  readonly #handleDown = (view: dia.LinkView): void => {
    if (isProvisional(view.model)) return;

    this.#before = endsOf(view.model);
    this.#beforeRoute = drawnRoute(view);
    this.#beforeEnds = {
      source: JSON.parse(JSON.stringify(view.model.get('source'))) as object,
      target: JSON.parse(JSON.stringify(view.model.get('target'))) as object,
    };
  };

  /**
   * The end of any gesture on a connector.
   *
   * One handler for reconnecting and rerouting because the pointer cannot tell them apart until it
   * is released: dragging an arrowhead onto the same shape it started on is a reroute, and
   * dragging a bend can never be anything else.
   */
  readonly #handleUp = (view: dia.LinkView): void => {
    const before = this.#before;
    const beforeRoute = this.#beforeRoute;
    const dragged = this.#dragged;
    const beforeEnds = this.#beforeEnds;
    this.#before = null;
    this.#beforeRoute = null;
    this.#beforeEnds = null;

    // Consumed here, not reset on pointer-down: the tool opens its batch before the paper reports
    // the pointer going down, so a reset there would wipe out the very drag it had just recorded.
    this.#dragged = false;

    if (before === null || isProvisional(view.model)) return;

    const link = view.model;
    const after = endsOf(link);

    /*
     * Both ends put back as they were, anchors included, wherever the gesture does not commit. A
     * refused or empty drag leaves the link holding the end the pointer left it on, and the route
     * the router had restored then runs from that end to the other: a diagonal, from a gesture
     * that changed nothing.
     */
    const restoreEnds = (): void => {
      if (beforeEnds === null) return;

      link.set({ source: beforeEnds.source, target: beforeEnds.target });
    };


    // A loose end. The paper refuses to commit one (linkPinning is off), so the link has already
    // snapped back; there is nothing to report, only the router to let go of.
    if (after.source === null || after.target === null) {
      this.#live?.end(true);
      restoreEnds();
      return;
    }

    const waypoints = this.#anchoredRoute(view, after.source, after.target);

    if (after.source !== before.source || after.target !== before.target) {
      // Kept on screen until the document comes back with the same route.
      this.#live?.end(false);
      this.#callbacks.reconnected(String(link.id), after.source, after.target, waypoints);
      return;
    }

    // A click that never took hold of an end: nothing to commit, whatever the grid would snap to.
    if (!dragged) {
      this.#live?.end(true);
      return;
    }

    /*
     * A release the rules refused is not a reroute, however the router would have drawn the flow
     * from the spot the pointer left it: the end has snapped back and the route goes with it.
     */
    const rejection = this.#rejections?.takeLastRejection() ?? null;

    if (rejection !== null) {
      this.#live?.end(true);
      restoreEnds();
      this.#callbacks.reconnectRejected(rejection.id, rejection.message, String(link.id));
      return;
    }

    if (beforeRoute === null || routeChanged(beforeRoute, waypoints)) {
      this.#live?.end(false);
      this.#callbacks.rerouted(String(link.id), waypoints);
      return;
    }

    // Nothing changed: the user clicked, or let go where the flow already was.
    this.#live?.end(true);
    restoreEnds();
  };

  /**
   * The start of an end drag.
   *
   * The flow is handed to the Manhattan router for the length of the gesture, so the line the user
   * drags is right-angled at every step. Dragging an end clears the bends: the route runs from the
   * new end. The batch is the only edge of the gesture the tool exposes before the pointer comes up.
   */
  readonly #handleBatch = (event: { batchName?: string; cell?: dia.Cell }): void => {
    const { batchName, cell } = event;

    if (batchName !== 'arrowhead-move' || cell === undefined || !cell.isLink() || isProvisional(cell)) return;

    this.#dragged = true;
    this.#live?.beginForLink(cell, false);
  };

  /**
   * The route to commit, with both ends held to the outline.
   *
   * JointJS stores a connected end as a reference and computes the drawn point from its anchor;
   * BPMNDI stores a coordinate. Section 4.6 says which coordinate: the pointer's position snapped
   * to the grid and then projected onto the outline. Reading JointJS's rendered endpoint instead
   * would commit whatever its own anchor policy produced, which is not the same thing and not
   * stable across a grid-size change.
   */
  #anchoredRoute(view: dia.LinkView, sourceId: string, targetId: string): readonly PointContract[] {
    const link = view.model;

    // A flow the router is holding is committed as routed: the end anchors were already projected
    // onto the outline as the end was dragged, and the route between them is the right-angled one.
    const routed = this.#live?.originalBendsOf(String(link.id)) === null || this.#live === null
      ? null
      : this.#live.routeOf(link);
    if (routed !== null) return routed;

    const source = findElement(link.graph, sourceId);
    const target = findElement(link.graph, targetId);
    const existing = link.vertices();
    const rendered = view.getConnection();

    if (source === undefined || target === undefined) {
      return existing.length >= 2 ? existing : [{ x: 0, y: 0 }, { x: 0, y: 0 }];
    }

    // Aim from where the line currently meets each shape, which is where the user left it.
    const first = rendered.start ?? link.getSourcePoint();
    const last = rendered.end ?? link.getTargetPoint();

    const route = routeWith(
      [first, ...existing, last],
      attachmentPoint(first, source, this.#grid),
      attachmentPoint(last, target, this.#grid),
    );

    /*
     * Moving an end leaves the bends where they were, so the first and last segments now run from
     * the new end to a bend placed for the old one — a diagonal. A flow is right-angled however
     * its end got where it is, so the route is squared up to the ends the way the router does.
     */
    const type = (link.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType;
    const from = route[0];
    const to = route.at(-1);
    if (from === undefined || to === undefined || !isOrthogonal((type ?? 'sequenceFlow') as ConnectorKind)) return route;

    return alignToEndpoints(from, route.slice(1, -1), to);
  }
}

/** A link's ends, as element ids, or null where an end is a bare point. */
function endsOf(link: dia.Link): { source: string | null; target: string | null } {
  const source = link.source().id;
  const target = link.target().id;

  return {
    source: source === undefined ? null : String(source),
    target: target === undefined ? null : String(target),
  };
}

/**
 * A link's route as it is drawn, ends included, or null before it has been drawn.
 *
 * The route, not the path: a connector's path is curves where it turns.
 */
function drawnRoute(view: dia.LinkView): readonly PointContract[] | null {
  const { sourcePoint, route, targetPoint } = view as unknown as {
    sourcePoint?: PointContract;
    route?: readonly PointContract[];
    targetPoint?: PointContract;
  };

  return sourcePoint === undefined || route === undefined || targetPoint === undefined
    ? null
    : [sourcePoint, ...route, targetPoint];
}

/**
 * Whether a committed route differs from the one the link was drawn with.
 *
 * Ends count. Sliding an end along its shape changes where the flow meets it and nothing else — a
 * straight flow has no bends before or after — and comparing the bends alone called that "nothing
 * changed", so the new attachment was never stored and the flow was left drawn between two ends
 * that no longer lined up.
 */
function routeChanged(before: readonly PointContract[], waypoints: readonly PointContract[]): boolean {
  if (before.length !== waypoints.length) return true;

  return before.some((point, index) => {
    const candidate = waypoints[index];
    return candidate === undefined
      || Math.abs(point.x - candidate.x) > 0.01
      || Math.abs(point.y - candidate.y) > 0.01;
  });
}
