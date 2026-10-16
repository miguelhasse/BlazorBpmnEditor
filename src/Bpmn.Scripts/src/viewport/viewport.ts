import type { dia, g } from '@joint/core';

import type { AcceptedConfig } from '../config/accepted-config.js';
import type { ViewportChangedPayload, ViewportCommand } from '../interop/contracts.js';
import { GestureTracker } from './gestures.js';

/**
 * Zoom, pan and fit, built entirely on `dia.Paper`'s own transform API.
 *
 * Wheel, drag and touch handling live here rather than in the Blazor layer because the pointer
 * must never round-trip through interop; the resulting transform is reported back through the
 * coalesced `viewportChanged` event instead.
 *
 * The canvas is unbounded: panning is a translate with no clamp, so there is no edge to reach and
 * no content extent to keep in sync. Fit-to-content is the only operation that consults the
 * drawn area, and it asks the paper rather than tracking it.
 */
export class ViewportController {
  readonly #paper: dia.Paper;

  /** The area the last visibility pass mounted for, so the next one can be skipped. */
  #covered: g.Rect | null = null;
  readonly #config: AcceptedConfig;
  readonly #onChanged: (payload: ViewportChangedPayload) => void;
  readonly #abort = new AbortController();

  readonly #gestures = new GestureTracker();

  #panning = false;

  /*
   * Wheel and pointer input is accumulated and applied once per animation frame.
   *
   * A trackpad or a high-rate mouse delivers wheel and pointermove events faster than the screen refreshes, and each applied change
   * writes the paper's transform and re-decides visibility. Doing that per event meant several full passes per frame, which is
   * where the worst zoom frames came from. Pending input is a screen-space pan, a product of zoom factors and the latest focus point.
   */
  #pendingDx = 0;
  #pendingDy = 0;
  #pendingFactor = 1;
  #pendingFocus: { x: number; y: number } | null = null;
  #frame = 0;
  #destroyed = false;

  /** The paper element's rectangle, read once per frame rather than once per event (it forces layout). */
  #rect: DOMRect | null = null;

  public constructor(
    paper: dia.Paper,
    config: AcceptedConfig,
    onChanged: (payload: ViewportChangedPayload) => void,
  ) {
    this.#paper = paper;
    this.#config = config;
    this.#onChanged = onChanged;

    // Not passive: an unmodified wheel pans the canvas, so the page must not scroll with it.
    this.#paper.el.addEventListener('wheel', this.#handleWheel, {
      passive: false,
      signal: this.#abort.signal,
    });

    if (config.viewport.dragPanEnabled || config.viewport.touchEnabled) {
      this.#paper.el.addEventListener('pointerdown', this.#handlePointerDown, { signal: this.#abort.signal });
      this.#paper.el.addEventListener('pointermove', this.#handlePointerMove, { signal: this.#abort.signal });
      this.#paper.el.addEventListener('pointerup', this.#handlePointerUp, { signal: this.#abort.signal });
      this.#paper.el.addEventListener('pointercancel', this.#handlePointerUp, { signal: this.#abort.signal });
    }

    if (config.viewport.touchEnabled) {
      // The browser's own pinch-zoom and scroll would otherwise consume the gesture before a
      // pointer event is raised for it.
      this.#paper.el.style.touchAction = 'none';
    }
  }

  public execute(command: ViewportCommand): void {
    // A command is ordered after the input that came before it, so what the wheel already asked for lands first.
    this.#flushNow();

    switch (command.kind) {
      case 'zoomIn':
        this.#zoomBy(this.#config.viewport.zoomStep, null);
        break;
      case 'zoomOut':
        this.#zoomBy(1 / this.#config.viewport.zoomStep, null);
        break;
      case 'setZoom':
        if (this.#applyZoom(command.zoom, null)) this.#report();
        break;
      case 'panBy':
        this.#panBy(command.dx, command.dy);
        break;
      case 'reset':
        this.#paper.scale(1, 1);
        this.#paper.translate(0, 0);
        this.#report();
        break;
      case 'setTransform': {
        // Clamped like every other way of changing the scale: a stale view restored into a canvas
        // whose limits have just been reconfigured must land inside the new ones.
        const { minZoom, maxZoom } = this.#config.viewport;
        const zoom = Math.min(maxZoom, Math.max(minZoom, command.zoom));

        this.#paper.scale(zoom, zoom);
        this.#paper.translate(command.dx, command.dy);
        this.#report();
        break;
      }
      case 'fitToContent':
        this.#paper.transformToFitContent({
          padding: this.#config.viewport.fitPadding,
          minScale: this.#config.viewport.minZoom,
          maxScale: this.#config.viewport.maxZoom,
          useModelGeometry: true,
          horizontalAlign: 'middle',
          verticalAlign: 'middle',
        });
        this.#report();
        break;
      case 'revealElement':
        this.#reveal(command.elementId);
        break;
    }
  }

  /**
   * Pans just far enough to bring one element inside the visible area.
   *
   * Only when it is not already there, and never by changing the scale. This is what follows a
   * keyboard reader stepping along a flow, and a canvas that re-centred on every step — or
   * re-zoomed under someone who had chosen a zoom — would be taking the diagram over rather than
   * keeping up with them.
   */
  #reveal(elementId: string): void {
    const cell = this.#paper.model.getCell(elementId) as dia.Cell | undefined;
    if (cell?.isElement() !== true) return;

    const area = this.#paper.getArea();
    const box = cell.getBBox();

    // A margin, so the element arrives inside the viewport rather than flush against its edge.
    const margin = 40 / this.#paper.scale().sx;
    const wanted = box.inflate(margin, margin);

    if (area.containsRect(wanted)) return;

    const dx = wanted.x < area.x
      ? area.x - wanted.x
      : wanted.x + wanted.width > area.x + area.width
        ? area.x + area.width - (wanted.x + wanted.width)
        : 0;

    const dy = wanted.y < area.y
      ? area.y - wanted.y
      : wanted.y + wanted.height > area.y + area.height
        ? area.y + area.height - (wanted.y + wanted.height)
        : 0;

    const { sx, sy } = this.#paper.scale();
    this.#panBy(dx * sx, dy * sy);
  }

  public get state(): ViewportChangedPayload {
    this.#flushNow();
    const { sx } = this.#paper.scale();
    const { tx, ty } = this.#paper.translate();
    return { zoom: sx, translateX: tx, translateY: ty };
  }

  public destroy(): void {
    // What the wheel already asked for is applied and reported first, so a rebuild that restores the viewport restores the one the user left.
    this.#flushNow();
    this.#destroyed = true;
    this.#cancelFrame();
    this.#pendingDx = 0;
    this.#pendingDy = 0;
    this.#pendingFactor = 1;
    this.#pendingFocus = null;
    this.#abort.abort();
    this.#gestures.clear();
  }

  /** Queues a screen-space pan for the next frame. */
  #queuePan(dx: number, dy: number): void {
    if (dx === 0 && dy === 0) return;

    this.#pendingDx += dx;
    this.#pendingDy += dy;
    this.#schedule();
  }

  /** Queues a zoom for the next frame; the latest focus point wins, the factors multiply. */
  #queueZoom(factor: number, focus: { x: number; y: number } | null): void {
    this.#pendingFactor *= factor;
    if (focus !== null) this.#pendingFocus = focus;
    this.#schedule();
  }

  #schedule(): void {
    if (this.#frame === 0 && !this.#destroyed) this.#frame = requestAnimationFrame(this.#flushFrame);
  }

  #cancelFrame(): void {
    if (this.#frame === 0) return;

    cancelAnimationFrame(this.#frame);
    this.#frame = 0;
  }

  /** Applies whatever is pending now, instead of at the next frame. */
  #flushNow(): void {
    if (this.#frame === 0) return;

    this.#cancelFrame();
    this.#applyPending();
  }

  readonly #flushFrame = (): void => {
    this.#frame = 0;
    this.#applyPending();
  };

  /** Pan first, so the zoom's focus point is where the pointer is now; one transform write each, one report. */
  #applyPending(): void {
    const dx = this.#pendingDx;
    const dy = this.#pendingDy;
    const factor = this.#pendingFactor;
    const focus = this.#pendingFocus;
    this.#pendingDx = 0;
    this.#pendingDy = 0;
    this.#pendingFactor = 1;
    this.#pendingFocus = null;
    this.#rect = null;
    if (this.#destroyed) return;

    let changed = false;
    if (dx !== 0 || dy !== 0) {
      const { tx, ty } = this.#paper.translate();
      this.#paper.translate(tx + dx, ty + dy);
      changed = true;
    }

    if (factor !== 1 && this.#applyZoom(this.#paper.scale().sx * factor, focus)) changed = true;

    if (changed) this.#report();
  }

  /** Translates the paper by a screen-space delta, now. Commands use this; gestures queue instead. */
  #panBy(dx: number, dy: number): void {
    if (dx === 0 && dy === 0) return;

    const { tx, ty } = this.#paper.translate();
    this.#paper.translate(tx + dx, ty + dy);
    this.#report();
  }

  /**
   * An unmodified wheel pans; Ctrl or Cmd always zooms.
   *
   * A trackpad reports two-finger scroll as a wheel event and pinch as Ctrl+wheel, so this one
   * rule covers both devices without sniffing for either.
   */
  readonly #handleWheel = (event: WheelEvent): void => {
    const { wheelMode, wheelPanSpeed, zoomStep } = this.#config.viewport;
    const zooming = event.ctrlKey || event.metaKey || wheelMode === 'zoom';

    event.preventDefault();

    if (zooming) {
      const direction = event.deltaY < 0 ? zoomStep : 1 / zoomStep;
      this.#queueZoom(direction, this.#elementPoint(event.clientX, event.clientY));
      return;
    }

    // Shift makes a vertical wheel scroll horizontally, which is the convention every
    // scrollable surface already follows.
    const delta = this.#wheelDelta(event);
    const dx = event.shiftKey ? -delta.y : -delta.x;
    const dy = event.shiftKey ? 0 : -delta.y;
    this.#queuePan(dx * wheelPanSpeed, dy * wheelPanSpeed);
  };

  /**
   * Normalises a wheel delta to pixels.
   *
   * `deltaMode` varies by browser and device: Firefox reports lines, some mice report pages, and
   * treating either as pixels makes a single notch jump the canvas across the screen.
   */
  #wheelDelta(event: WheelEvent): { x: number; y: number } {
    const factor = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? 400 : 1;
    return { x: event.deltaX * factor, y: event.deltaY * factor };
  }

  readonly #handlePointerDown = (event: PointerEvent): void => {
    const touch = event.pointerType !== 'mouse';
    if (touch && !this.#config.viewport.touchEnabled) return;
    if (!touch && (!this.#config.viewport.dragPanEnabled || event.button !== 0)) return;

    // Blank paper only.
    if (event.target instanceof Element && event.target.closest(NO_PAN_FROM) !== null) return;

    /*
     * Shift belongs to rectangular selection. Both gestures start on blank paper with the primary
     * button, so one of them has to yield, and panning is the one that has an alternative — the
     * arrow keys, the scroll wheel and a plain drag all still pan.
     */
    if (event.shiftKey && !this.#config.isReadOnly) return;

    /*
     * A mouse press on blank paper is a pan, not the start of a text selection or a native
     * drag-and-drop, which is what the browser would otherwise begin as soon as the pointer moved.
     * Cancelling it also cancels the implicit focus, so the canvas is focused by hand: the keyboard
     * shortcuts depend on it.
     */
    if (!touch) {
      event.preventDefault();
      this.#paper.el.closest<HTMLElement>('[tabindex]')?.focus({ preventScroll: true });
    }

    this.#rect = null;
    const point = this.#elementPoint(event.clientX, event.clientY);
    this.#gestures.start({ id: event.pointerId, ...point });

    this.#panning = true;
    this.#capture(event.pointerId);
    this.#paper.el.classList.add('bpmn-canvas--panning');
  };

  readonly #handlePointerMove = (event: PointerEvent): void => {
    if (!this.#panning) return;

    const point = this.#elementPoint(event.clientX, event.clientY);
    const intent = this.#gestures.move({ id: event.pointerId, ...point });

    switch (intent.kind) {
      case 'pan':
        this.#queuePan(intent.dx, intent.dy);
        break;
      case 'pinch':
        // Translation is applied first (see #applyPending) so the zoom's focus point is where the fingers are now,
        // not where they were when the gesture started.
        this.#queuePan(intent.dx, intent.dy);
        this.#queueZoom(intent.scale, { x: intent.centerX, y: intent.centerY });
        break;
      case 'none':
        break;
    }
  };

  readonly #handlePointerUp = (event: PointerEvent): void => {
    this.#gestures.end(event.pointerId);

    try {
      if (this.#paper.el.hasPointerCapture(event.pointerId)) {
        this.#paper.el.releasePointerCapture(event.pointerId);
      }
    } catch {
      // Same reasoning as capture: releasing a pointer that is already gone is not an error
      // worth ending the gesture over.
    }

    // Still panning while any pointer remains down: lifting one finger of a pinch should leave
    // the other one dragging, not end the gesture.
    if (this.#gestures.count > 0) return;

    this.#panning = false;
    this.#paper.el.classList.remove('bpmn-canvas--panning');
  };

  /**
   * Captures the pointer, tolerating the cases where it cannot be.
   *
   * `setPointerCapture` throws `NotFoundError` when the id names no active pointer — a pointer
   * that was cancelled between the event and the handler, or a synthetic one. Without the guard
   * that exception escapes the listener and the gesture is abandoned half-configured.
   */
  #capture(pointerId: number): void {
    try {
      this.#paper.el.setPointerCapture(pointerId);
    } catch {
      // Capture is an improvement, not a requirement: without it a drag that leaves the element
      // stops tracking, which is worse than a thrown error but far better than a broken canvas.
    }
  }

  #elementPoint(clientX: number, clientY: number): { x: number; y: number } {
    const rect = (this.#rect ??= this.#paper.el.getBoundingClientRect());
    return { x: clientX - rect.left, y: clientY - rect.top };
  }

  #zoomBy(factor: number, focus: { x: number; y: number } | null): void {
    if (this.#applyZoom(this.#paper.scale().sx * factor, focus)) this.#report();
  }

  /** Clamps to the configured bounds and keeps the focus point visually stationary. Returns whether the scale changed; the caller reports. */
  #applyZoom(requested: number, focus: { x: number; y: number } | null): boolean {
    const { minZoom, maxZoom } = this.#config.viewport;
    const next = Math.min(maxZoom, Math.max(minZoom, requested));
    const current = this.#paper.scale().sx;
    if (next === current) return false;

    if (focus !== null) {
      const { tx, ty } = this.#paper.translate();
      const ratio = next / current;
      this.#paper.translate(focus.x - (focus.x - tx) * ratio, focus.y - (focus.y - ty) * ratio);
    }

    this.#paper.scale(next, next);
    return true;
  }

  #report(): void {
    this.#refreshVisibility();
    this.#onChanged(this.state);
  }

  /**
   * Re-decides what is mounted, but only when the viewport has left what the last pass covered.
   *
   * The visibility callback is consulted when a cell changes, not when the view moves under it, so
   * a transform has to ask again — but asking on every transform is what made culling a loss
   * rather than a win. Running the pass on every frame of a drag took the benchmark's zoom from
   * 63 frames over budget to 66 and its worst frame from 150ms to 233ms: mounting and unmounting
   * churn cost more than the eight thousand SVG nodes it was saving.
   *
   * So the pass runs when it can change the answer. Cells are mounted for a full viewport in each
   * direction and this re-checks once the viewport has moved half of that, which leaves half a
   * screen of already-built content ahead of the pan in every direction.
   */
  #refreshVisibility(): void {
    const area = this.#paper.getArea();

    if (this.#covered?.containsRect(area) === true) return;

    this.#paper.updateCellsVisibility();
    this.#covered = area.clone().inflate(area.width / 2, area.height / 2);
  }
}

/**
 * Where a pan must never start.
 *
 * Capturing the pointer redirects every following pointermove to the canvas element, so whatever
 * the gesture actually began on never sees the drag. On a cell that silently ate every element
 * drag. On the tools layer it silently ate the connector gesture, whose handle sits outside the
 * shape and so is inside no cell at all. Selection outlines are listed for the same reason.
 */
const NO_PAN_FROM = '.joint-cell, .joint-tools, .joint-highlight-mask';
