import type { dia } from '@joint/core';

import { snapToGrid } from '../geometry/outline.js';
import type { GridOptionsContract, PointContract } from '../interop/contracts.js';
import { dropRepeats } from '../routing/align.js';
import { drawnRoute } from '../routing/live-router.js';
import { isOrthogonal, type ConnectorKind } from '../routing/router.js';
import {
  isVertical,
  nearestSegment,
  slideWithin,
  type SlideBox,
  type SlideEnvironment,
} from '../routing/slide.js';
import { isProvisional } from './provisional.js';

/** What the controller tells the host. */
export interface SegmentDragCallbacks {
  /** A flow was slid and its new route should be stored. */
  readonly rerouted: (connectorId: string, waypoints: readonly PointContract[]) => void;
}

/** How far the pointer has to travel, in screen pixels, before a press on a line becomes a drag. */
const DRAG_THRESHOLD = 3;

/** How far an end run leaves along itself before stepping sideways, in paper units. */
const STUB = 10;

/** One gesture in progress. */
interface Gesture {
  readonly link: dia.Link;
  readonly view: dia.LinkView;

  /** The route when the pointer went down, ends included. */
  readonly route: readonly PointContract[];

  /** The vertices the link had, for putting back. */
  readonly vertices: readonly PointContract[];

  /** Which run is being slid. */
  readonly index: number;

  /** Where the pointer went down, on the paper and on the screen. */
  readonly press: PointContract;
  readonly pressClient: PointContract;

  readonly environment: SlideEnvironment;

  started: boolean;
  current: readonly PointContract[];
}

/**
 * Drag a connector by any point on its line.
 *
 * No handle is drawn and none is needed: the line already has a ten-unit transparent hit band, so a
 * press anywhere on it is a press on the connector. A press that stays put is only a selection (the
 * interaction controller does that); one that travels takes hold of the run under the pointer and
 * slides it sideways, so the line follows the pointer and stays right-angled.
 *
 * What it will not do is as much of the design as what it does. The ends stay on their shapes; the
 * route never lies along itself or another connector, and never runs through its own shapes. A drag
 * that would is stopped against what is in the way (`slideWithin`) rather than refused or let
 * through. Associations are straight by notation and are not slid.
 *
 * Nothing is committed while the pointer is down. The link's vertices are set freely so the line
 * looks live, and the document hears once, on release — which is also what makes one undo step undo
 * one slide.
 */
export class SegmentDragController {
  readonly #grid: GridOptionsContract;
  readonly #callbacks: SegmentDragCallbacks;

  #paper: dia.Paper | null = null;
  #gesture: Gesture | null = null;

  public constructor(grid: GridOptionsContract, callbacks: SegmentDragCallbacks) {
    this.#grid = grid;
    this.#callbacks = callbacks;
  }

  public attach(paper: dia.Paper): this {
    this.#paper = paper;

    paper.on('link:pointerdown', this.#handleDown);
    paper.el.addEventListener('pointermove', this.#handleHover);

    return this;
  }

  public destroy(): void {
    this.#cancel();
    this.#paper?.off('link:pointerdown', this.#handleDown);
    this.#paper?.el.removeEventListener('pointermove', this.#handleHover);
    this.#paper = null;
  }

  readonly #handleDown = (view: dia.LinkView, event: dia.Event, x: number, y: number): void => {
    const paper = this.#paper;
    const target = event.target as Element | null;

    // The tools have their own gestures: the end handles drag an end, the button deletes.
    if (paper === null || this.#gesture !== null || target?.closest('.joint-tools') != null) return;
    if ((event.originalEvent as MouseEvent | undefined)?.button !== undefined
      && (event.originalEvent as MouseEvent).button !== 0) return;

    const link = view.model;
    const route = this.#routeOf(link);
    if (route === null) return;

    const index = nearestSegment(route, { x, y });
    if (index === null) return;

    this.#gesture = {
      link,
      view,
      route,
      vertices: [...link.vertices()],
      index,
      press: { x, y },
      pressClient: { x: event.clientX ?? 0, y: event.clientY ?? 0 },
      environment: this.#environmentFor(link),
      started: false,
      current: route,
    };

    const document = paper.el.ownerDocument;
    document.addEventListener('pointermove', this.#handleMove);
    document.addEventListener('pointerup', this.#handleUp);
    document.addEventListener('keydown', this.#handleKey);
  };

  readonly #handleMove = (event: PointerEvent): void => {
    const gesture = this.#gesture;
    const paper = this.#paper;
    if (gesture === null || paper === null) return;

    if (!gesture.started) {
      const travelled = Math.hypot(event.clientX - gesture.pressClient.x, event.clientY - gesture.pressClient.y);
      if (travelled < DRAG_THRESHOLD) return;

      gesture.started = true;
    }

    const from = gesture.route[gesture.index];
    const to = gesture.route[gesture.index + 1];
    if (from === undefined || to === undefined) return;

    const local = paper.clientToLocalPoint({ x: event.clientX, y: event.clientY });
    const vertical = isVertical(from, to);

    // Where the run would sit, snapped the way everything else on the canvas is, and so how far it moved.
    const wanted = vertical ? from.x + (local.x - gesture.press.x) : from.y + (local.y - gesture.press.y);
    const placed = this.#grid.snapEnabled
      ? (vertical ? snapToGrid({ x: wanted, y: 0 }, this.#grid.size).x : snapToGrid({ x: 0, y: wanted }, this.#grid.size).y)
      : wanted;
    const offset = placed - (vertical ? from.x : from.y);

    gesture.current = slideWithin(gesture.route, gesture.index, offset, gesture.environment);
    gesture.link.vertices(gesture.current.slice(1, -1).map((point) => ({ x: point.x, y: point.y })));

    event.preventDefault();
  };

  readonly #handleUp = (): void => {
    const gesture = this.#gesture;
    this.#release();
    if (!gesture?.started) return;

    if (!changed(gesture.route, gesture.current)) {
      gesture.link.vertices([...gesture.vertices]);
      return;
    }

    this.#callbacks.rerouted(String(gesture.link.id), gesture.current);
  };

  readonly #handleKey = (event: KeyboardEvent): void => {
    if (event.key === 'Escape') this.#cancel();
  };

  /** Puts the line back and ends the gesture. */
  #cancel(): void {
    const gesture = this.#gesture;
    this.#release();

    if (gesture?.started === true) gesture.link.vertices([...gesture.vertices]);
  }

  #release(): void {
    const document = this.#paper?.el.ownerDocument;
    document?.removeEventListener('pointermove', this.#handleMove);
    document?.removeEventListener('pointerup', this.#handleUp);
    document?.removeEventListener('keydown', this.#handleKey);

    this.#gesture = null;
  }

  /**
   * Which way a press on the line would slide it, shown as the cursor.
   *
   * With no handle to look at, the cursor is how the line says it can be taken: it points across
   * the run under it, the way the run would move.
   */
  readonly #handleHover = (event: PointerEvent): void => {
    const paper = this.#paper;
    const target = event.target as SVGElement | null;
    if (paper === null || this.#gesture !== null || event.buttons !== 0 || target === null) return;
    if (target.closest('.joint-tools') != null) return;

    const element = target.closest('.joint-link');
    const id = element?.getAttribute('model-id');
    if (id === null || id === undefined) return;

    const link = paper.model.getCell(id);
    const route = link.isLink() ? this.#routeOf(link) : null;
    if (route === null) return;

    const local = paper.clientToLocalPoint({ x: event.clientX, y: event.clientY });
    const index = nearestSegment(route, local);
    const from = index === null ? undefined : route[index];
    const to = index === null ? undefined : route[index + 1];

    // On the whole connector, not on what is under the pointer: that is the thin line or the wide
    // transparent band around it, depending on where exactly the pointer is.
    if (from !== undefined && to !== undefined && element instanceof SVGElement) {
      element.style.cursor = isVertical(from, to) ? 'ew-resize' : 'ns-resize';
    }
  };

  /** A connector's drawn route, if it is one that can be slid: right-angled, real, not scaffolding. */
  #routeOf(link: dia.Link): readonly PointContract[] | null {
    if (isProvisional(link)) return null;

    const type = (link.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType;
    if (!isOrthogonal((type ?? 'sequenceFlow') as ConnectorKind)) return null;

    const drawn = drawnRoute(this.#paper, link);
    if (drawn === null) return null;

    const route = dropRepeats(drawn);

    return route.length >= 2 ? route : null;
  }

  /** The other connectors' routes and the two end shapes, which a slide has to keep clear of. */
  #environmentFor(link: dia.Link): SlideEnvironment {
    const graph = link.graph as dia.Graph | null;

    const others = (graph?.getLinks() ?? [])
      .filter((other) => other !== link && !isProvisional(other))
      .map((other) => drawnRoute(this.#paper, other))
      .filter((route): route is PointContract[] => route !== null);

    const boxes: SlideBox[] = [];

    for (const end of [link.source(), link.target()]) {
      const cell = end.id === undefined ? undefined : graph?.getCell(end.id);
      if (cell?.isElement() === true) boxes.push(cell.getBBox());
    }

    return { others, boxes, stub: STUB };
  }
}

/** Whether a slid route is not the one the flow had. */
function changed(before: readonly PointContract[], after: readonly PointContract[]): boolean {
  if (before.length !== after.length) return true;

  return before.some((point, index) => {
    const candidate = after[index];
    return candidate === undefined || Math.abs(point.x - candidate.x) > 0.01 || Math.abs(point.y - candidate.y) > 0.01;
  });
}
