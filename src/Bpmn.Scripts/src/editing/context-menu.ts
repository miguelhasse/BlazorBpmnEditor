import type { dia } from '@joint/core';

import type {
  ContextMenuRequestPayload,
  ContextMenuSource,
  ContextMenuTarget,
} from '../interop/contracts.js';
import { findCell, findView } from '../graph/cells.js';
import { AnchorResolver } from './anchor.js';

/** What the controller tells the host when a menu is asked for. */
export interface ContextMenuCallbacks {
  readonly requested: (payload: ContextMenuRequestPayload) => void;

  /**
   * The current selection, read at the moment of the request.
   *
   * A supplier rather than a value: the menu's commands act on what is selected now, and a
   * right-click is the one gesture that can arrive at any point between two selection changes.
   */
  readonly selection: () => readonly string[];
}

/**
 * Suppresses the browser's context menu inside the canvas and routes the request to Blazor.
 *
 * Section 7 is unusually specific here, and for a reason: a BPMN canvas is covered in things that
 * are not ordinary HTML — SVG cells, tool layers, highlight masks, editing overlays — and each one
 * is a place the native menu could appear through a gap in the suppression. So the handler is
 * registered on the canvas root in the capture phase and calls `preventDefault()` every time,
 * before anything inside has a chance to decide otherwise. It is bound in Readonly mode too: the
 * menu's inspection commands are exactly what Readonly keeps.
 *
 * The listener is on the canvas root and nowhere else, so nothing outside it is affected — a host
 * page keeps its own context menu everywhere except over the editor.
 *
 * Right-click, the Context Menu key, Shift+F10 and a configured touch long-press all end in the
 * same request. A request is always produced: a right-click on blank paper resolves to the canvas
 * with a zero-size anchor at the point, because section 7 forbids ever falling back to the
 * browser's menu, and "no target-specific commands" is the Blazor surface's problem to present,
 * not a reason for the canvas to stay silent.
 */
export class ContextMenuController {
  readonly #host: HTMLElement;
  readonly #paper: dia.Paper;
  readonly #anchors: AnchorResolver;
  readonly #callbacks: ContextMenuCallbacks;
  readonly #longPressMs: number;

  #longPressTimer: ReturnType<typeof setTimeout> | null = null;
  #longPressOrigin: { x: number; y: number; target: EventTarget | null } | null = null;
  #attached = false;
  #request: ContextMenuRequestPayload | null = null;
  #observer: MutationObserver | null = null;
  #resizeObserver: ResizeObserver | null = null;
  #frame: number | null = null;

  /**
   * @param host The canvas root. Suppression is scoped to this element's subtree.
   * @param paper The paper, for converting between client and paper coordinates.
   * @param longPressMs How long a touch must be held; zero disables the gesture.
   * @param callbacks What to tell the host.
   */
  public constructor(
    host: HTMLElement,
    paper: dia.Paper,
    longPressMs: number,
    callbacks: ContextMenuCallbacks,
  ) {
    this.#host = host;
    this.#paper = paper;
    this.#anchors = new AnchorResolver(host, paper);
    this.#longPressMs = longPressMs;
    this.#callbacks = callbacks;
  }

  /** Binds the listeners. */
  public attach(): this {
    if (this.#attached) return this;
    this.#attached = true;

    // Capture phase: this runs before any handler inside the canvas, so no descendant can let the
    // native menu through by handling the event first.
    this.#host.addEventListener('contextmenu', this.#handleContextMenu, { capture: true });
    this.#host.addEventListener('keydown', this.#handleKeyDown);
    this.#host.addEventListener('touchstart', this.#handleTouchStart, { passive: true });
    this.#host.addEventListener('touchend', this.#cancelLongPress);
    this.#host.addEventListener('touchmove', this.#cancelLongPress, { passive: true });
    this.#host.addEventListener('touchcancel', this.#cancelLongPress);
    this.#paper.on('translate scale resize render:done', this.#schedule);
    this.#paper.model.on('change:position change:size change:vertices change:source change:target', this.#schedule);
    window.addEventListener('scroll', this.#schedule, true);
    window.addEventListener('resize', this.#schedule);
    this.#observer = new MutationObserver(this.#schedule);
    this.#observer.observe(this.#host, {
      childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'data-bpmn-element'],
    });
    this.#resizeObserver = new ResizeObserver(this.#schedule);
    this.#resizeObserver.observe(this.#host);

    return this;
  }

  public dispose(): void {
    if (!this.#attached) return;
    this.#attached = false;
    this.#cancelLongPress();

    this.#host.removeEventListener('contextmenu', this.#handleContextMenu, { capture: true });
    this.#host.removeEventListener('keydown', this.#handleKeyDown);
    this.#host.removeEventListener('touchstart', this.#handleTouchStart);
    this.#host.removeEventListener('touchend', this.#cancelLongPress);
    this.#host.removeEventListener('touchmove', this.#cancelLongPress);
    this.#host.removeEventListener('touchcancel', this.#cancelLongPress);
    this.#paper.off('translate scale resize render:done', this.#schedule);
    this.#paper.model.off('change:position change:size change:vertices change:source change:target', this.#schedule);
    window.removeEventListener('scroll', this.#schedule, true);
    window.removeEventListener('resize', this.#schedule);
    this.#observer?.disconnect();
    this.#resizeObserver?.disconnect();
    if (this.#frame !== null) cancelAnimationFrame(this.#frame);
    this.#frame = null;
    this.#request = null;
  }

  /** Opens the menu for a target the host resolved itself — the keyboard path, from outside. */
  public request(elementId: string | null, source: ContextMenuSource): void {
    const cell = elementId === null ? undefined : findCell(this.#paper.model, elementId);
    this.#emit(cell ?? null, this.#anchors.centre(), source);
  }

  readonly #handleContextMenu = (event: MouseEvent): void => {
    // Unconditional, in both Edit and Readonly. Section 7 allows no case in which the browser's
    // own menu is reachable from inside the canvas.
    event.preventDefault();

    // Stopped as well as prevented: an application-level handler further up would otherwise open
    // its own menu alongside the editor's, which is the same failure in a different place.
    event.stopPropagation();

    this.#cancelLongPress();
    this.#emit(this.#cellAt(event.target), { x: event.clientX, y: event.clientY }, 'pointer');
  };

  readonly #handleKeyDown = (event: KeyboardEvent): void => {
    const asked = event.key === 'ContextMenu' || (event.key === 'F10' && event.shiftKey);
    if (!asked) return;

    event.preventDefault();
    event.stopPropagation();

    /*
     * A keyboard request has no pointer, so the target is whatever has focus — and when that is
     * the canvas itself, the single selected element, which is what the user was working on. The
     * point handed in only matters when neither answers, and then the menu belongs to the canvas
     * and opens at its centre.
     */
    this.#emit(
      this.#cellAt(document.activeElement) ?? this.#selectedCell(),
      this.#anchors.centre(),
      'keyboard',
    );
  };

  readonly #handleTouchStart = (event: TouchEvent): void => {
    this.#cancelLongPress();
    if (this.#longPressMs <= 0 || event.touches.length !== 1) return;

    const touch = event.touches[0];
    if (touch === undefined) return;

    this.#longPressOrigin = { x: touch.clientX, y: touch.clientY, target: event.target };
    this.#longPressTimer = setTimeout(() => {
      const origin = this.#longPressOrigin;
      this.#cancelLongPress();
      if (origin === null) return;

      this.#emit(this.#cellAt(origin.target), { x: origin.x, y: origin.y }, 'longPress');
    }, this.#longPressMs);
  };

  readonly #cancelLongPress = (): void => {
    if (this.#longPressTimer !== null) clearTimeout(this.#longPressTimer);
    this.#longPressTimer = null;
    this.#longPressOrigin = null;
  };

  /**
   * Resolves whatever was under the pointer to the BPMN element that owns it.
   *
   * Section 7: "overlay/tool targets resolve to their owning BPMN/DI object or the canvas". A
   * selection halo, a resize handle and a link's vertex tool are all drawn outside the cell they
   * belong to, but JointJS stamps `model-id` on the tools layer as well as on the cell, so walking
   * up to the nearest one answers for every case with a single rule. Blazor's own overlays carry
   * `data-bpmn-element` for the same reason.
   */
  #cellAt(target: EventTarget | null): dia.Cell | null {
    if (!(target instanceof Element)) return null;
    if (!this.#host.contains(target)) return null;

    const owner = target.closest('[model-id], [data-bpmn-element]');
    if (owner === null) return null;

    const id = owner.getAttribute('model-id') ?? owner.getAttribute('data-bpmn-element');
    if (id === null || id.length === 0) return null;

    return findCell(this.#paper.model, id) ?? null;
  }

  /** The one selected cell, or null when nothing or several things are selected. */
  #selectedCell(): dia.Cell | null {
    const selection = this.#callbacks.selection();
    const only = selection.length === 1 ? selection[0] : undefined;

    return only === undefined ? null : findCell(this.#paper.model, only) ?? null;
  }

  #emit(cell: dia.Cell | null, at: { x: number; y: number }, source: ContextMenuSource): void {
    const bpmn = cell?.get('bpmn') as { readonly elementType?: string } | undefined;
    if (bpmn?.elementType === 'group') return;

    const view = cell === null ? undefined : findView(this.#paper, cell);

    const anchor = view?.el instanceof SVGElement
      ? this.#anchors.ofRect(view.el.getBoundingClientRect())
      : this.#anchors.ofPoint(at);

    const target: ContextMenuTarget = cell === null
      ? 'canvas'
      : cell.isLink() ? 'link' : 'element';

    const request: ContextMenuRequestPayload = {
      target,
      elementId: cell === null ? null : String(cell.id),
      anchor,
      source,
      selection: [...this.#callbacks.selection()],
    };
    this.#request = request;
    this.#callbacks.requested(request);
    this.#schedule();
  }

  readonly #schedule = (): void => {
    if (this.#frame !== null || this.#request === null || this.#host.querySelector('.bpmn-menu') === null) return;
    this.#frame = requestAnimationFrame(() => {
      this.#frame = null;
      this.#layout();
    });
  };

  #layout(): void {
    const menu = this.#host.querySelector<HTMLElement>('.bpmn-menu');
    const request = this.#request;
    if (menu === null || request === null) return;
    if ((menu.dataset.bpmnElement ?? null) !== request.elementId) return;

    const cell = request.elementId === null ? undefined : findCell(this.#paper.model, request.elementId);
    const view = cell === undefined ? undefined : findView(this.#paper, cell);
    const anchor = view?.el instanceof SVGElement
      ? this.#anchors.ofRect(view.el.getBoundingClientRect())
      : this.#anchors.ofLocalRect(request.anchor.bounds);
    const left = `${(anchor.client.x + this.#host.scrollLeft).toString()}px`;
    const top = `${(anchor.client.y + anchor.client.height + this.#host.scrollTop).toString()}px`;

    // Blazor may restore the initial inline style while rendering a property edit.
    if (menu.style.left !== left) menu.style.left = left;
    if (menu.style.top !== top) menu.style.top = top;
  }
}
