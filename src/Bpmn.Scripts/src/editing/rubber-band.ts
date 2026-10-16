import type { dia } from '@joint/core';

/** A rectangle in client coordinates, as the pointer sees it. */
interface ClientRect {
  readonly left: number;
  readonly top: number;
  readonly width: number;
  readonly height: number;
}

/** What the controller asks the host to do when a band closes. */
export interface RubberBandCallbacks {
  /** The elements the band enclosed, replacing the selection. */
  readonly selected: (ids: readonly string[]) => void;
}

/**
 * Rectangular ("lasso") selection.
 *
 * Shift and drag on blank paper. Plain drag still pans, because that is the gesture this canvas has
 * always had and silently repurposing it would break every host that documented it; Shift is the
 * modifier that already means "selection" everywhere else here, so Shift-dragging a rectangle over
 * the diagram reads as one gesture rather than two.
 *
 * **Fully enclosed, not merely touched.** A band selects a shape only when it contains the whole of
 * it. Intersection would be friendlier for grazing a row of tasks, but it also selects every
 * container the band happens to sit inside — draw a small band in the middle of a Pool and you
 * would have selected the Pool. Containment gives pool-scoped selection for free, with no special
 * case: the Pool is not inside the band, so it is not selected, and neither is the plane.
 *
 * The band itself is an HTML element over the canvas rather than an SVG rectangle on the paper. It
 * is transient interface, not notation, so it stays out of the diagram's own coordinate space and
 * cannot be caught by a selector, a highlighter, or an export.
 */
export class RubberBandController {
  readonly #paper: dia.Paper;
  readonly #host: HTMLElement;
  readonly #callbacks: RubberBandCallbacks;
  readonly #abort = new AbortController();

  #origin: { x: number; y: number } | null = null;
  #band: HTMLElement | null = null;
  #pointerId: number | null = null;

  public constructor(paper: dia.Paper, callbacks: RubberBandCallbacks) {
    this.#paper = paper;
    this.#host = paper.el;
    this.#callbacks = callbacks;

    const { signal } = this.#abort;
    this.#host.addEventListener('pointerdown', this.#handleDown, { signal });

    // On the document, not the host: a band dragged past the canvas edge must keep tracking, and
    // a pointer released outside it must still close the gesture rather than leaving it armed.
    this.#host.ownerDocument.addEventListener('pointermove', this.#handleMove, { signal });
    this.#host.ownerDocument.addEventListener('pointerup', this.#handleUp, { signal });
    this.#host.ownerDocument.addEventListener('keydown', this.#handleKey, { signal });
  }

  /** Whether a band is being dragged right now. */
  public get isActive(): boolean {
    return this.#origin !== null;
  }

  public destroy(): void {
    this.#cancel();
    this.#abort.abort();
  }

  readonly #handleDown = (event: PointerEvent): void => {
    if (!isBandGesture(event)) return;

    this.#origin = { x: event.clientX, y: event.clientY };
    this.#pointerId = event.pointerId;

    // The band takes the gesture outright: without this the paper still treats the drag as a
    // blank-paper interaction and clears the selection out from under the result.
    event.preventDefault();
    event.stopPropagation();
  };

  readonly #handleMove = (event: PointerEvent): void => {
    const origin = this.#origin;
    if (origin === null || event.pointerId !== this.#pointerId) return;

    const rect = rectBetween(origin, { x: event.clientX, y: event.clientY });

    // Drawn only once the drag is deliberate. A one-pixel band on every Shift-click would flash a
    // rectangle nobody asked for.
    if (rect.width < THRESHOLD && rect.height < THRESHOLD) return;

    this.#draw(rect);
  };

  readonly #handleUp = (event: PointerEvent): void => {
    const origin = this.#origin;
    if (origin === null || event.pointerId !== this.#pointerId) return;

    const rect = rectBetween(origin, { x: event.clientX, y: event.clientY });
    const drawn = this.#band !== null;
    this.#cancel();

    // A Shift-click that never became a drag is not a selection of everything; it is a click, and
    // reporting an empty band for it would clear the selection the user was adding to.
    if (!drawn) return;

    this.#callbacks.selected(this.#enclosed(rect));
  };

  /** Escape abandons the band without changing the selection. */
  readonly #handleKey = (event: KeyboardEvent): void => {
    if (event.key === 'Escape' && this.#origin !== null) this.#cancel();
  };

  #draw(rect: ClientRect): void {
    const host = this.#host.getBoundingClientRect();

    this.#band ??= this.#host.appendChild(this.#createBand());
    this.#band.style.left = `${String(rect.left - host.left)}px`;
    this.#band.style.top = `${String(rect.top - host.top)}px`;
    this.#band.style.width = `${String(rect.width)}px`;
    this.#band.style.height = `${String(rect.height)}px`;
  }

  #createBand(): HTMLElement {
    const band = this.#host.ownerDocument.createElement('div');
    band.className = 'bpmn-rubber-band';
    band.setAttribute('aria-hidden', 'true');
    return band;
  }

  #cancel(): void {
    this.#band?.remove();
    this.#band = null;
    this.#origin = null;
    this.#pointerId = null;
  }

  /**
   * The elements a band fully contains, with any descendant of another dropped.
   *
   * Selecting a Sub-Process and its children together would move each child twice when the
   * selection is dragged: once for itself and once with its parent.
   */
  #enclosed(rect: ClientRect): readonly string[] {
    const topLeft = this.#paper.clientToLocalPoint({ x: rect.left, y: rect.top });
    const bottomRight = this.#paper.clientToLocalPoint({
      x: rect.left + rect.width,
      y: rect.top + rect.height,
    });

    const inside: dia.Element[] = [];
    for (const element of this.#paper.model.getElements()) {
      const box = element.getBBox();
      if (box.x >= topLeft.x
        && box.y >= topLeft.y
        && box.x + box.width <= bottomRight.x
        && box.y + box.height <= bottomRight.y) inside.push(element);
    }

    const ids = new Set<string>();
    for (const element of inside) ids.add(String(element.id));

    const result: string[] = [];
    for (const element of inside) {
      if (!element.getAncestors().some((ancestor) => ids.has(String(ancestor.id)))) result.push(String(element.id));
    }

    return result;
  }
}

/** How far the pointer must travel before a band is a band rather than a click. */
const THRESHOLD = 4;

/**
 * Whether a pointer-down should start a band.
 *
 * Shift, the primary button, and blank paper. The same layers a pan refuses to start from are
 * refused here for the same reason: a gesture that began on a cell, a tool or a selection outline
 * belongs to whatever it began on.
 */
function isBandGesture(event: PointerEvent): boolean {
  if (!event.shiftKey || event.button !== 0) return false;

  return !(event.target instanceof Element) || event.target.closest(NOT_BLANK) === null;
}

const NOT_BLANK = '.joint-cell, .joint-tools, .joint-highlight-mask';

function rectBetween(a: { x: number; y: number }, b: { x: number; y: number }): ClientRect {
  return {
    left: Math.min(a.x, b.x),
    top: Math.min(a.y, b.y),
    width: Math.abs(a.x - b.x),
    height: Math.abs(a.y - b.y),
  };
}
