import type { dia } from '@joint/core';

import { findCell, findElement } from '../graph/cells.js';
import { annotationConnections, isAnnotation } from './annotation-policy.js';
import type {
  ConnectorRouteContract,
  GeometryBounds,
  PointContract,
  RoutingOptionsContract,
} from '../interop/contracts.js';
import { alignToEndpoints } from '../routing/align.js';
import { crosses } from '../routing/live-router.js';
import { isOrthogonal, routeFor, type ConnectorKind } from '../routing/router.js';
import { axisOf } from './connect-controller.js';
import { isProvisional } from './provisional.js';

/** A link a gesture has taken over, and what it looked like before. */
interface Routed {
  readonly link: dia.Link;

  /** The bends it had, for a rollback. */
  readonly vertices: readonly PointContract[];

  /** The elements at its ends. */
  readonly sourceId: string | null;
  readonly targetId: string | null;

  /** True while both ends move together, so the route is carried along instead of re-routed. */
  rigid: boolean;
}

/**
 * Keeps Sequence and Message Flows orthogonal, and Manhattan-routed, while something moves.
 *
 * A flow has right angles at rest, and a gesture must not be the exception. So for the length of a
 * gesture, every flow attached to what is moving is handed to JointJS's own Manhattan router with
 * its stored bends cleared: JointJS then re-routes it by itself whenever an end or an obstacle
 * changes, which is every frame of a drag and needs no code of ours. That is why this is a router
 * swap and not a per-frame squaring pass — the line the user sees is a fresh route around the
 * neighbours, not the old one with its end bends nudged.
 *
 * Three ways a flow is taken over:
 * - an **element** moves or is resized: its flows are re-routed (bends the user placed are replaced);
 * - a flow whose **both ends** move together (a group, or a Pool with its contents) is carried by
 *   the same offset instead, because its route is still right and only its bends need to follow;
 * - an **endpoint or bend** of one flow is dragged: that flow is re-routed with its bends kept as
 *   via-points where the gesture is a bend, and cleared where it is an end.
 *
 * Associations and Data Associations are the one exception and are never touched: they are straight
 * lines by notation (`isOrthogonal`).
 *
 * At the end of a gesture the routed points are written back as the link's own vertices and the
 * router is removed, so a flow at rest is drawn from its stored waypoints exactly as before; or the
 * old vertices are restored when the gesture is refused.
 */
export class LiveRouting {
  readonly #paper: dia.Paper;
  readonly #options: RoutingOptionsContract;
  readonly #active = new Map<string, Routed>();

  /** Where the elements a gesture moves started, which is what "moved together" is measured from. */
  readonly #origins = new Map<string, PointContract>();
  readonly #annotations = new Map<string, {
    element: dia.Element; origin: PointContract; target: dia.Cell; targetOrigin: PointContract;
    ratio: number | null;
  }>();

  /** Capture before the first frame, so preview and rollback share the model's attachment offset. */
  public prepareAnnotations(movingIds: ReadonlySet<string>): void {
    this.#paper.updateViews();
    for (const element of this.#paper.model.getElements()) {
      const id = String(element.id);
      if (!isAnnotation(element) || movingIds.has(id) || this.#annotations.has(id)) continue;
      const connections = annotationConnections(this.#paper.model, id);
      const connection = connections[0];
      if (connections.length !== 1 || connection === undefined) continue;
      const source = String(connection.source().id) === id;
      const target = findCell(this.#paper.model, String(source ? connection.target().id : connection.source().id));
      if (target === undefined || isAnnotation(target)) continue;
      if (target.isElement()) {
        if (!movingIds.has(String(target.id))) continue;
        this.#annotations.set(id, {
          element, origin: element.position(), target, targetOrigin: target.position(), ratio: null,
        });
      } else if (target.isLink()) {
        if (!movingIds.has(String(target.id)) && !movingIds.has(String(target.source().id))
          && !movingIds.has(String(target.target().id))) continue;
        const associationView = this.#paper.findViewByModel<dia.LinkView>(connection);
        const targetView = this.#paper.findViewByModel<dia.LinkView>(target);
        const point = source ? associationView.targetPoint : associationView.sourcePoint;
        this.#annotations.set(id, {
          element, origin: element.position(), target, targetOrigin: { x: point.x, y: point.y },
          ratio: targetView.getClosestPointRatio(point),
        });
      }
    }
  }

  public constructor(paper: dia.Paper, options: RoutingOptionsContract) {
    this.#paper = paper;
    this.#options = options;

    /*
     * A container moves its children after its own position has changed, so the move that the
     * interaction controller hears about is over before the flows between the children have
     * anywhere new to be carried to. A `translate` batch closes once every member has moved.
     *
     * Only the outermost one: each embedded child opens and closes a `translate` batch of its own,
     * and carrying after the first child would measure a flow whose ends have not both moved yet and
     * hand it to the router for the rest of the gesture.
     */
    let depth = 0;
    paper.model.on('batch:start', (info: { batchName?: string }) => {
      if (info.batchName === 'translate') depth += 1;
    });
    paper.model.on('batch:stop', (info: { batchName?: string }) => {
      if (info.batchName !== 'translate') return;

      depth = Math.max(0, depth - 1);
      if (depth === 0 && this.isActive) this.carry();
    });
  }

  /** Whether a gesture currently owns any flow. */
  public get isActive(): boolean {
    return this.#active.size > 0;
  }

  /**
   * Takes over every flow attached to these elements.
   *
   * @param movingIds The elements being moved. A flow with both ends among them is carried rather than re-routed.
   */
  public beginForElements(movingIds: ReadonlySet<string>): void {
    const graph = this.#paper.model;

    for (const id of movingIds) {
      const element = findElement(graph, id);
      if (element === undefined) continue;

      if (!this.#origins.has(id)) this.#origins.set(id, element.position());

      for (const link of graph.getConnectedLinks(element)) {
        const source = link.source().id;
        const target = link.target().id;
        const rigid = source !== undefined && target !== undefined
          && movingIds.has(String(source)) && movingIds.has(String(target));

        this.#take(link, { keepVertices: rigid, rigid });
      }
    }
  }

  /**
   * Takes over one flow whose end or bend is being dragged.
   *
   * @param link The flow.
   * @param keepVertices True for a bend drag, where the bends are the gesture's own and become via-points.
   */
  public beginForLink(link: dia.Link, keepVertices: boolean): void {
    this.prepareAnnotations(new Set([String(link.id)]));
    this.#take(link, { keepVertices, rigid: false });
  }

  /**
   * Carries the flows whose both ends move together by the offset they have moved.
   *
   * Measured from the ends themselves rather than from the pointer, because alignment snapping can
   * nudge one element of a group and not another. A flow whose ends have stopped moving together is
   * handed to the router instead, for the rest of the gesture.
   */
  public carry(): void {
    for (const entry of this.#active.values()) {
      if (!entry.rigid) continue;

      const source = this.#offsetOf(entry.sourceId);
      const target = this.#offsetOf(entry.targetId);

      if (source === null || target === null || Math.abs(source.x - target.x) > 0.5 || Math.abs(source.y - target.y) > 0.5) {
        entry.rigid = false;
        this.#route(entry.link, false);
        continue;
      }

      entry.link.vertices(entry.vertices.map((vertex) => ({ x: vertex.x + source.x, y: vertex.y + source.y })));
    }
    this.#carryAnnotations();
  }

  #carryAnnotations(): void {
    if (this.#annotations.size === 0) return;
    this.#paper.updateViews();
    for (const { element, origin, target, targetOrigin, ratio } of this.#annotations.values()) {
      const now = target.isElement() ? target.position()
        : ratio === null ? null : this.#paper.findViewByModel<dia.LinkView>(target).getPointAtRatio(ratio);
      if (now === null) continue;
      element.position(origin.x + now.x - targetOrigin.x, origin.y + now.y - targetOrigin.y);
    }
  }

  /** The bends a flow had before the gesture took it over, or null if it has not. */
  public originalBendsOf(linkId: string): readonly PointContract[] | null {
    return this.#active.get(linkId)?.vertices ?? null;
  }

  /** The route of one flow as it is drawn now, ends included, every segment axial. */
  public routeOf(link: dia.Link): readonly PointContract[] | null {
    // The paper is asynchronous: a route read in the frame a move happened still describes where
    // the shape was.
    this.#paper.updateViews();

    return this.#read(link);
  }

  #read(link: dia.Link): readonly PointContract[] | null {
    const view = this.#paper.findViewByModel<dia.LinkView>(link);
    /*
     * The route, not the path. A connector is drawn with rounded corners, and the path of one is
     * curves — reading points off it would store the arcs as waypoints.
     */
    const { sourcePoint: from, route, targetPoint: to } = view as unknown as {
      sourcePoint?: PointContract;
      route?: readonly PointContract[];
      targetPoint?: PointContract;
    };
    if (from === undefined || to === undefined || route === undefined) return null;

    const drawn = [from, ...route, to];
    return alignToEndpoints(from, drawn.slice(1, -1), to);
  }

  /** The routes of every flow the gesture owns, ready to send with the edit. */
  public routes(): readonly ConnectorRouteContract[] {
    const routes: ConnectorRouteContract[] = [];
    this.#carryAnnotations();
    this.#paper.updateViews();

    for (const { link } of this.#active.values()) {
      const waypoints = this.#read(link);
      if (waypoints !== null) routes.push({ elementId: String(link.id), waypoints });
    }

    return routes;
  }

  /**
   * Hands every flow back.
   *
   * @param restore True to put the old bends back (a refused or empty gesture); false to keep what
   * was routed, so the canvas shows the new route until the document comes back.
   */
  public end(restore: boolean): void {
    if (!restore) this.#carryAnnotations();
    if (restore) {
      for (const { element, origin } of this.#annotations.values()) element.position(origin.x, origin.y);
    }
    this.#annotations.clear();
    const finished = [...this.#active.values()];
    this.#active.clear();
    this.#origins.clear();

    // Read before any router is removed, since removing it changes what is drawn.
    if (!restore) this.#paper.updateViews();
    const routed = restore ? [] : finished.map(({ link }) => this.#read(link));

    finished.forEach(({ link, vertices }, index) => {
      link.unset('router');
      link.vertices(restore ? [...vertices] : (routed[index] ?? []).slice(1, -1));
    });
  }

  /**
   * Routes for the flows attached to an element that is about to change size.
   *
   * A resize only draws a preview, so the model is briefly given the new bounds, the flows are
   * routed against them, and everything is put back. Ends attached to the element follow it
   * proportionally, the same rule `FollowResizedShapes` applies in the document, so both agree on
   * where each end lands.
   */
  public routesAfterResize(elementId: string, bounds: GeometryBounds): readonly ConnectorRouteContract[] {
    const graph = this.#paper.model;
    const element = findElement(graph, elementId);
    if (element === undefined || this.isActive) return [];

    const before = { position: element.position(), size: element.size() };
    if (before.size.width <= 0 || before.size.height <= 0) return [];

    const scaleX = bounds.width / before.size.width;
    const scaleY = bounds.height / before.size.height;

    const anchors: { link: dia.Link; end: 'source' | 'target'; dx: number; dy: number }[] = [];

    for (const link of graph.getConnectedLinks(element)) {
      for (const end of ['source', 'target'] as const) {
        const ref = link.get(end) as { id?: string; anchor?: { args?: { dx?: number; dy?: number } } };
        const args = ref.anchor?.args;
        if (String(ref.id) !== elementId || args?.dx === undefined || args.dy === undefined) continue;

        anchors.push({ link, end, dx: args.dx, dy: args.dy });
        link.prop(`${end}/anchor/args/dx`, args.dx * scaleX);
        link.prop(`${end}/anchor/args/dy`, args.dy * scaleY);
      }
    }

    element.resize(bounds.width, bounds.height);
    element.position(bounds.x, bounds.y);

    try {
      this.beginForElements(new Set([elementId]));
      return this.routes();
    } finally {
      this.end(true);

      element.position(before.position.x, before.position.y);
      element.resize(before.size.width, before.size.height);

      for (const { link, end, dx, dy } of anchors) {
        link.prop(`${end}/anchor/args/dx`, dx);
        link.prop(`${end}/anchor/args/dy`, dy);
      }
    }
  }

  #take(link: dia.Link, how: { keepVertices: boolean; rigid: boolean }): void {
    const id = String(link.id);
    if (isProvisional(link) || this.#active.has(id)) return;

    const kind = kindOf(link);
    if (!isOrthogonal(kind)) return;

    const sourceId = link.source().id;
    const targetId = link.target().id;

    /*
     * A flow that returns to its own shape is carried like any other, unless the route it has runs
     * through that shape — a loop drawn across its own body is an overlap of the flow with itself,
     * and carrying it would keep it.
     */
    const rigid = how.rigid && !(sourceId !== undefined && sourceId === targetId && this.#runsThroughItsShape(link));

    this.#active.set(id, {
      link,
      vertices: [...link.vertices()],
      sourceId: sourceId === undefined ? null : String(sourceId),
      targetId: targetId === undefined ? null : String(targetId),
      rigid,
    });

    // A carried flow keeps its route and is only offset; everything else is handed to the router.
    if (!rigid) this.#route(link, how.keepVertices && how.rigid === rigid);
  }

  #runsThroughItsShape(link: dia.Link): boolean {
    const view = this.#paper.findViewByModel<dia.LinkView>(link) as dia.LinkView | undefined;
    const drawn = view as unknown as { sourcePoint?: PointContract; route?: readonly PointContract[]; targetPoint?: PointContract } | undefined;
    const element = findElement(this.#paper.model, String(link.source().id));
    if (drawn?.sourcePoint === undefined || drawn.targetPoint === undefined || element === undefined) return false;

    return crosses([drawn.sourcePoint, ...(drawn.route ?? []), drawn.targetPoint], element.getBBox());
  }

  #route(link: dia.Link, keepVertices: boolean): void {
    const sourceId = link.source().id;
    const source = sourceId === undefined ? undefined : findElement(link.graph, String(sourceId));
    const route = routeFor(kindOf(link), this.#options, source === undefined ? 'horizontal' : axisOf(source));

    link.router(route.name, route.args as Record<string, unknown> | undefined);
    if (!keepVertices) link.vertices([]);
  }

  /** How far an element has moved since the gesture began, or null if it is unknown. */
  #offsetOf(elementId: string | null): PointContract | null {
    if (elementId === null) return null;

    const origin = this.#origins.get(elementId);
    const element = findElement(this.#paper.model, elementId);
    if (origin === undefined || element === undefined) return null;

    const now = element.position();

    return { x: now.x - origin.x, y: now.y - origin.y };
  }
}

function kindOf(link: dia.Link): ConnectorKind {
  const type = (link.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType;

  return (type ?? 'sequenceFlow') as ConnectorKind;
}
