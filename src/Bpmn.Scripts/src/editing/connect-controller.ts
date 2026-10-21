import { shapes } from '@joint/core';
import type { dia } from '@joint/core';

import { centerOf, outlineIntersection, projectToOutline } from '../geometry/outline.js';
import { DASH_ASSOCIATION } from '../geometry/constants.js';
import { findCell, findElement } from '../graph/cells.js';
import { attachmentPoint, outlineFor } from './endpoint-anchor.js';
import type {
  CanvasStringsContract,
  GeometryBounds,
  GridOptionsContract,
  RoutingOptionsContract,
} from '../interop/contracts.js';
import { canvasText } from '../a11y/canvas-text.js';
import {
  inferConnectorKind,
  validateConnection,
  type RuleViolation,
} from '../rules/connection-rules.js';
import { endpointOf, type ConnectorKind, type Endpoint } from '../rules/endpoint-facts.js';
import { isProvisional, PROVISIONAL_FLAG } from './provisional.js';
import { alignToEndpoints } from '../routing/align.js';
import type { FlowAxis } from '../routing/router.js';
import { peersBetween, separate } from '../routing/parallel.js';
import { routeFor } from '../routing/router.js';

/** What the controller asks the host to do once a connector gesture finishes. */
export interface ConnectCallbacks {
  readonly connectorRequested: (
    kind: ConnectorKind,
    sourceId: string,
    targetId: string,
    waypoints: readonly { readonly x: number; readonly y: number }[],
  ) => void;
  readonly connectorRejected: (code: string, message: string, elementId: string) => void;
}

/**
 * Drag-to-connect.
 *
 * Built on JointJS's own link-from-tool machinery, which already handles the drag, the provisional
 * link, target highlighting and the arrowhead. What is added is the BPMN part: which pairs are
 * legal, which connector a pair implies, and reporting the result as a command.
 *
 * The gesture starts from the handle on a selected element (see `connect-tool.ts`), and the
 * attachment point stays anywhere on the outline — section 4.6 forbids quantising an outline into
 * fixed ports, and nothing here introduces any.
 *
 * Validation runs live, during the drag, against the shared rule table. `markAvailable` then greys
 * out every shape the rules reject, so an illegal target never looks droppable.
 */
export class ConnectController {
  readonly #grid: GridOptionsContract;
  readonly #routing: RoutingOptionsContract;
  readonly #strings: CanvasStringsContract;
  readonly #targetMessages = new Map<dia.CellView, string>();
  #tooltip: HTMLDivElement | null = null;

  #paper: dia.Paper | null = null;
  #callbacks: ConnectCallbacks | null = null;

  /** The last refusal, so a rejected release can say which rule stopped it. */
  #lastRejection: RuleViolation | null = null;

  /**
   * Takes the last refusal, clearing it.
   *
   * The same `validateConnection` judges a new connector and a reconnected end, so this is where
   * both find out why a release was refused. Reading it clears it, because a refusal belongs to
   * one gesture: leaving it behind would let the next successful drag report an error from the
   * previous failed one.
   */
  public takeLastRejection(): RuleViolation | null {
    const rejection = this.#lastRejection;
    this.#lastRejection = null;
    return rejection;
  }

  /**
   * Whether the gesture in flight ended on a legal target.
   *
   * Needed because both outcomes end the same way — the provisional link is removed — and only
   * this distinguishes "reported as a command" from "refused and should say why".
   */
  #committed = false;

  /** The shape the gesture started on, so a refusal can point at something on the canvas. */
  #sourceId: string | null = null;

  /**
   * @param grid
   * The accepted grid options, because section 4.6 makes the committed endpoint depend on them:
   * the pointer is snapped before it is projected onto the outline.
   */
  public constructor(grid: GridOptionsContract, routing: RoutingOptionsContract, strings: CanvasStringsContract) {
    this.#grid = grid;
    this.#routing = routing;
    this.#strings = strings;
  }

  /**
   * Binds the gesture events.
   *
   * Separate from construction because the paper needs this object's validator and
   * provisional-link factory *at* construction time — configuring them afterwards would leave a
   * window in which a connector drag was judged by nothing. So the policy exists first and the
   * events are attached once the paper does.
   */
  public attach(paper: dia.Paper, callbacks: ConnectCallbacks): this {
    this.#paper = paper;
    this.#callbacks = callbacks;

    paper.on('link:connect', this.#handleConnected);
    paper.el.ownerDocument.addEventListener('pointermove', this.#showTooltip);
    paper.el.ownerDocument.addEventListener('pointerup', this.#clearTooltip);
    paper.el.ownerDocument.addEventListener('pointercancel', this.#clearTooltip);

    return this;
  }

  public destroy(): void {
    this.#paper?.el.ownerDocument.removeEventListener('pointermove', this.#showTooltip);
    this.#paper?.el.ownerDocument.removeEventListener('pointerup', this.#clearTooltip);
    this.#paper?.el.ownerDocument.removeEventListener('pointercancel', this.#clearTooltip);
    this.#clearTooltip();
    this.#paper?.off('link:connect', this.#handleConnected);
    this.#paper = null;
    this.#callbacks = null;
  }

  /**
   * The paper's `validateConnection`, called continuously while a connector is dragged.
   *
   * Returns a boolean because that is what the paper expects; the reason is kept so the release
   * can report it. Refusing here is what strict mode means — the drop is blocked, not warned about.
   */
  public readonly validate = (
    sourceView: dia.CellView,
    _sourceMagnet: SVGElement | null,
    targetView: dia.CellView,
    _targetMagnet: SVGElement | null,
  ): boolean => {
    /*
     * The shape the gesture started on is offered as a snap target, because the whole body is a
     * magnet and the pointer begins right next to it. A release that lands back on the source is
     * almost always an abandoned drag, so it is refused here — in the gesture, not in the rule
     * table, because BPMN itself permits a Flow Node to loop to itself and the table states the
     * notation, not the interaction.
     */
    if (sourceView === targetView) return false;

    const source = endpointFor(sourceView.model);
    const target = endpointFor(targetView.model);
    if (source === null || target === null) return false;

    // Only annotations offer flow-line targets; other snap candidates must not replace a shape's refusal.
    if ((sourceView.model.isLink() || targetView.model.isLink())
      && source.elementType !== 'textAnnotation' && target.elementType !== 'textAnnotation') return false;

    const kind = inferConnectorKind(source, target);
    const result = validateConnection(kind, source, target);

    this.#lastRejection = result.isValid
      ? null
      : result.violations.find((violation) => violation.severity === 'error') ?? null;

    if (result.isValid) {
      this.#targetMessages.delete(targetView);
      this.#targetMessages.delete(sourceView);
    } else {
      const message = result.violations
        .filter((violation) => violation.severity === 'error')
        .map((violation) => this.#strings.values[`Rule.${violation.id}`]
          ?? canvasText(this.#strings, 'ConnectionNotAllowed'))
        .join('\n');
      this.#targetMessages.set(targetView, message);
      this.#targetMessages.set(sourceView, message);
    }

    return result.isValid;
  };

  readonly #showTooltip = (event: PointerEvent): void => {
    const paper = this.#paper;
    if (paper === null || event.buttons === 0 || !(event.target instanceof Element)) {
      this.#tooltip?.remove();
      this.#tooltip = null;
      return;
    }

    // Pointer capture keeps event.target on the dragged handle rather than the hovered endpoint.
    const target = paper.el.ownerDocument.elementFromPoint(event.clientX, event.clientY) ?? event.target;
    const view = paper.findView(target) as dia.CellView | undefined;
    const message = view === undefined ? undefined : this.#targetMessages.get(view);
    if (message === undefined) {
      this.#tooltip?.remove();
      this.#tooltip = null;
      return;
    }

    const tooltip = this.#tooltip ?? paper.el.ownerDocument.createElement('div');
    tooltip.className = 'bpmn-connection-tooltip';
    tooltip.setAttribute('role', 'tooltip');
    tooltip.textContent = message;
    Object.assign(tooltip.style, {
      position: 'fixed', left: `${String(event.clientX + 12)}px`, top: `${String(event.clientY + 12)}px`,
      pointerEvents: 'none', whiteSpace: 'pre-line', zIndex: '1000', maxWidth: '24rem',
      padding: '0.4rem 0.6rem', borderRadius: '4px',
      background: 'var(--bpmn-canvas-background, white)', color: 'var(--bpmn-label-color, black)',
      border: '1px solid var(--bpmn-selection-color, currentColor)',
    });
    if (this.#tooltip === null) paper.el.append(tooltip);
    this.#tooltip = tooltip;
  };

  readonly #clearTooltip = (): void => {
    this.#tooltip?.remove();
    this.#tooltip = null;
    this.#targetMessages.clear();
  };

  /**
   * The provisional link a connector drag draws.
   *
   * Accented and dashed so it never reads as committed notation, and not added to the graph here —
   * the paper owns its lifetime, and adding it twice leaves one behind when the gesture is
   * abandoned.
   *
   * Also where the gesture is known to have started, which is the only moment the paper can be
   * told a connector is in flight — see `#beginGesture`.
   */
  public readonly defaultLink = (cellView: dia.CellView): dia.Link => {
    this.#beginGesture(cellView);
    this.#committed = false;

    const link = new shapes.standard.Link();
    const annotation = inferredKindFor(cellView) === 'association';

    link.attr({
      line: {
        stroke: 'var(--bpmn-selection-color)',
        strokeWidth: 2,
        strokeDasharray: annotation ? DASH_ASSOCIATION : '5 4',
        targetMarker: annotation ? null : { type: 'path', d: 'M 10 -5 0 0 10 5 z', fill: 'var(--bpmn-selection-color)' },
      },
    });

    /*
     * Routed, which is what makes the preview honest: the line the user drags is the line the
     * connector will be, because the route committed at release is read off this very link. The
     * alternative — previewing straight and storing orthogonal — shows one thing and saves another.
     */
    const route = routeFor(inferredKindFor(cellView), this.#routing, axisOf(cellView.model));
    link.router(route.name, route.args as Record<string, unknown> | undefined);

    // Marked so the provisional link is never mistaken for a BPMN connector — by the selection
    // model, by the accessibility labeller, or by a later save. See `provisional.ts` for why each
    // of those matters.
    link.set(PROVISIONAL_FLAG, true);

    /*
     * The only reliable end-of-gesture signal.
     *
     * JointJS refuses an illegal release by removing the link (`whenNotAllowed: 'remove'`) without
     * firing `link:connect` or `link:disconnect`, and a release on blank paper removes it the same
     * silent way. So the refusal is reported from the removal itself, which happens in every case,
     * rather than from an event that only fires on success.
     */
    link.once('remove', () => { this.#handleGestureEnded(); });

    return link;
  };

  /**
   * Marks the paper as connecting, so the stylesheet can grey out illegal targets.
   *
   * `markAvailable` adds `available-cell` to every shape the rules accept and leaves the rest
   * unmarked, which is the opposite of what the specification asks for: section 7 wants the
   * rejected shapes dimmed, so that an illegal target never looks droppable. Two classes turn the
   * positive marking into the negative one — the paper says a gesture is in flight, and the source
   * is exempted so the shape being dragged from does not dim itself.
   *
   * Cleared on pointer-up rather than on `link:connect`, because a gesture that ends on blank
   * paper fires neither connect nor disconnect and the dimming would stick.
   */
  #beginGesture(cellView: dia.CellView): void {
    const paperEl = cellView.paper?.el;
    const sourceEl = cellView.el;
    if (paperEl === undefined) return;

    this.#sourceId = String(cellView.model.id);
    paperEl.classList.add(CONNECTING_CLASS);
    sourceEl.classList.add(SOURCE_CLASS);

    paperEl.ownerDocument.addEventListener(
      'pointerup',
      () => {
        paperEl.classList.remove(CONNECTING_CLASS);
        sourceEl.classList.remove(SOURCE_CLASS);
      },
      { once: true },
    );
  }

  readonly #handleConnected = (linkView: dia.LinkView, event: dia.Event): void => {
    const link = linkView.model;
    if (!isProvisional(link)) return;

    const sourceId = link.source().id;
    const targetId = link.target().id;
    const graph = link.graph;

    /*
     * Discarded either way: on success the host sends back a document containing the real
     * connector, and leaving this one would draw the edge twice. Marked committed first, so the
     * removal below is not mistaken for a refusal.
     */
    this.#committed = true;

    // Read before the removal: this is the only moment the router's own output exists, and a
    // connector stored as two points would be drawn straight on the next render (sections 10.6 and 11).
    const routed = bendsOf(linkView);
    const drawnEnd = { x: linkView.targetPoint.x, y: linkView.targetPoint.y };

    link.remove();
    this.#lastRejection = null;

    if (sourceId === undefined || targetId === undefined) return;

    const source = endpointFor(findCell(graph, String(sourceId)));
    const target = endpointFor(findCell(graph, String(targetId)));
    if (source === null || target === null) return;

    const kind = inferConnectorKind(source, target);
    const result = validateConnection(kind, source, target);

    if (!result.isValid) {
      const violation = result.violations.find((candidate) => candidate.severity === 'error');
      this.#callbacks?.connectorRejected(
        violation?.id ?? 'SF-000',
        violation?.message ?? 'This connection is not allowed.',
        String(sourceId),
      );
      return;
    }

    this.#callbacks?.connectorRequested(
      kind,
      String(sourceId),
      String(targetId),
      this.#routeTo(graph, String(sourceId), String(targetId), event, routed, drawnEnd),
    );
  };

  /**
   * A gesture that ended without producing a connector.
   *
   * Silence would be the wrong answer when the user aimed at something and was told no: the drag
   * simply vanishing reads as the editor having missed the gesture. A drag that ended on blank
   * paper has no refusal to report, and says nothing.
   */
  /** The route for a finished gesture, anchored where the pointer released. */
  #routeTo(
    graph: dia.Graph,
    sourceId: string,
    targetId: string,
    event: dia.Event,
    bends: readonly { x: number; y: number }[],
    drawnEnd: { x: number; y: number } | null,
  ): readonly { x: number; y: number }[] {
    const paper = this.#paper;
    const release = paper === null || event.clientX === undefined || event.clientY === undefined
      ? null
      : paper.clientToLocalPoint({ x: event.clientX, y: event.clientY });

    if (paper !== null) {
      const source = findCell(graph, sourceId);
      const target = findCell(graph, targetId);
      if (source?.isElement() === true && target?.isLink() === true) {
        const view = paper.findViewByModel(target) as dia.LinkView | undefined;
        if (view === undefined) throw new Error('Association flow endpoint is not rendered.');
        const to = view.getClosestPoint(release ?? drawnEnd ?? centerOf(outlineFor(source).bounds));
        return [attachmentPoint(to, source, this.#grid), to];
      }
      if (source?.isLink() === true && target?.isElement() === true) {
        const view = paper.findViewByModel(source) as dia.LinkView | undefined;
        if (view === undefined) throw new Error('Association flow endpoint is not rendered.');
        const to = attachmentPoint(release ?? drawnEnd ?? view.getPointAtRatio(0.5), target, this.#grid);
        return [view.getClosestPoint(to), to];
      }
    }

    return routeBetween(graph, sourceId, targetId, release, this.#grid, this.#routing, bends, drawnEnd);
  }

  #handleGestureEnded(): void {
    const rejection = this.#lastRejection;
    const committed = this.#committed;

    this.#lastRejection = null;
    this.#committed = false;

    if (committed || rejection === null) return;

    this.#callbacks?.connectorRejected(rejection.id, rejection.message, this.#sourceId ?? '');
  }
}

/** On the paper while a connector is being dragged. */
const CONNECTING_CLASS = 'bpmn-connecting';

/** On the shape the connector is being dragged from, which must not dim itself. */
const SOURCE_CLASS = 'bpmn-connect-source';

/**
 * A two-point route, both ends on an outline.
 *
 * Two points and no more: DI-001 needs at least two waypoints, and an orthogonal route would
 * duplicate the router that arrives with connector routing.
 *
 * The target end is where the user released, snapped to the grid and then projected — section 4.6
 * requires creation, reconnection and endpoint dragging to use one pipeline, so this is the same
 * `attachmentPoint` the link tools call. The source end was never pointed at during this gesture,
 * so it is derived: the point on the source outline nearest the target attachment.
 *
 * What neither end may be is the shape *centre*. Section 4.6 puts each end on the shape's
 * outline, and a centre-to-centre route would draw the connector straight through both shapes.
 */
function routeBetween(
  graph: dia.Graph,
  sourceId: string,
  targetId: string,
  release: { x: number; y: number } | null,
  grid: GridOptionsContract,
  routing: RoutingOptionsContract,
  bends: readonly { x: number; y: number }[],
  drawnEnd: { x: number; y: number } | null,
): readonly { x: number; y: number }[] {
  const source = findElement(graph, sourceId);
  const target = findElement(graph, targetId);
  if (source === undefined || target === undefined) return [];

  const sourceOutline = outlineFor(source);
  const targetOutline = outlineFor(target);

  /*
   * The preview's end is where the router aimed its bends; the preview quantises the pointer through
   * the paper grid, so re-projecting the raw release point can pick the opposite edge on a tie
   * (a release at the centre) and join the bends to it straight through the shape.
   */
  const to = drawnEnd !== null
    ? projectToOutline(drawnEnd, targetOutline)
    : release === null
      ? outlineIntersection(targetOutline, centerOf(sourceOutline.bounds))
      : attachmentPoint(release, target, grid);

  const from = projectToOutline(to, sourceOutline);

  /*
   * Pulled clear of whatever already runs between these two shapes. Two Sequence Flows from one
   * Gateway to one Task are legal and not unusual, and routed identically they draw as one line —
   * the reader then sees one connector where the document has two.
   *
   * Squared up first, then pulled clear. The order matters: separation moves the route's interior,
   * and the interior is only defined once the ends have been reconciled with the router's bends.
   *
   * The ends themselves do not move. Section 4.6 puts them on the outline, and a connector's
   * attachment is on the outline or it is nothing — so the corridor between them is the only thing
   * left to separate, which is what the implementation plan means by "route offsets".
   */
  return separate(
    alignToEndpoints(from, bends, to),
    peersBetween(graph, sourceId, targetId, isProvisional),
    routing.parallelSeparation,
  );
}

/**
 * The bends the router put in, without the ends it anchored them to.
 *
 * JointJS anchors a connected end wherever its own anchor policy says; section 4.6 says the end is
 * the pointer's position snapped to the grid and projected onto the outline. So the router's
 * interior is kept and its two ends are replaced — which is also why the route is read here rather
 * than simply trusted whole.
 */
function bendsOf(view: dia.LinkView): readonly { x: number; y: number }[] {
  // The route, not the path: a rounded connector's path is curves, which are not bends.
  const { route } = view as unknown as { route?: readonly { x: number; y: number }[] };

  return (route ?? []).map((point) => ({ x: Math.round(point.x * 100) / 100, y: Math.round(point.y * 100) / 100 }));
}

/**
 * The connector kind a gesture from this shape will produce, guessed for the preview's router.
 *
 * A guess is all the preview can have — the kind is decided by the *pair*, and the target is not
 * known until release. An annotation always produces an Association; otherwise Sequence Flow is
 * the overwhelmingly common case. Guessing wrong then costs a preview with right angles for a
 * line that will be committed straight.
 */
function inferredKindFor(source: dia.CellView): ConnectorKind {
  return endpointFor(source.model)?.elementType === 'textAnnotation' ? 'association' : 'sequenceFlow';
}

/**
 * The axis the flow runs along where this element sits.
 *
 * Section 1.5.3 makes auto-routing depend on the Pool's `isHorizontal`, and this is the only place
 * that knows which Pool a connector is being drawn in. The value travels on the swimlane cell
 * because routing has a graph rather than a document.
 *
 * A Process with no Pool at all has no declared orientation, and BPMN's default reading order is
 * left to right — the same default section 10.4 gives a Pool that omits the attribute.
 */
export function axisOf(cell: dia.Cell): FlowAxis {
  const poolId = (cell.get('bpmn') as { readonly poolId?: string | null } | undefined)?.poolId;
  if (poolId === undefined || poolId === null) return 'horizontal';

  const pool = findCell(cell.graph, poolId);
  const isHorizontal = (pool?.get('bpmn') as { readonly isHorizontal?: boolean | null } | undefined)?.isHorizontal;

  return isHorizontal === false ? 'vertical' : 'horizontal';
}

/** The BPMN facts for a cell, read from the metadata the shape factories stored. */
function endpointFor(cell: dia.Cell | undefined): Endpoint | null {
  if (cell === undefined) return null;

  const bpmn = cell.get('bpmn') as {
    readonly elementType?: string;
    readonly containerId?: string | null;
    readonly poolId?: string | null;
    readonly eventDefinition?: string | null;
    readonly isForCompensation?: boolean;
  } | undefined;

  if (bpmn?.elementType === undefined) return null;

  return endpointOf(String(cell.id), bpmn.elementType, {
    containerId: bpmn.containerId ?? null,
    poolId: bpmn.poolId ?? null,
    eventDefinitionType: bpmn.eventDefinition ?? null,
    isForCompensation: bpmn.isForCompensation ?? false,
  });
}

/** Where a stencil drop landed, and what would own the created element. */
export interface DropTarget {
  readonly bounds: GeometryBounds;
  readonly containerId: string | null;
  readonly poolId: string | null;
}
