import type { dia } from '@joint/core';

import { findView } from '../graph/cells.js';
import type { MarkerPlacement } from '../markers/activity-markers.js';
import type { CanvasStringsContract } from '../interop/contracts.js';
import { canvasText } from '../a11y/canvas-text.js';
import { collapseTargetOf } from './expand-target.js';

/**
 * The button that expands a collapsed Sub-Process (specification section 1.2.1).
 *
 * Section 1.2.1 asks for the "+" itself to be "an interactive expand control" with "its own hit
 * target, accessible name ('Expand sub-process'), keyboard activation (Enter/Space)". An SVG node
 * cannot be that here: the paper is rendered `aria-hidden="true"` so the accessible tree beside it
 * can be the one thing assistive technology walks, and ARIA gives a descendant no way out of an
 * aria-hidden ancestor. `role="button" tabindex="0"` inside it would be focusable but
 * unannounceable — a control in the tab order that a screen reader cannot name, which is worse
 * than not having one.
 *
 * So the control is a real `<button>`, drawn over the glyph and outside the hidden subtree. Every
 * capability that paragraph asks for comes free with the element: click, Enter, Space, a focus
 * ring, and a name. Only the element type differs, and that is recorded as a deviation.
 *
 * It is also simpler than a hit target inside the paper. The press never reaches JointJS, so no
 * drag begins and no selection changes, with no `stopPropagation`, no ordering against the
 * interaction controller and no arm-on-down/fire-on-up dance to turn a `pointerdown` into a click.
 */

/** The `bpmn` metadata this module reads off a cell. */
interface CellFacts {
  readonly isExpanded?: boolean | null;
  readonly name?: string | null;
  readonly expandMarker?: MarkerPlacement | null;
}

export interface ExpandControlCallbacks {
  /** Raised when the user asks for a Sub-Process to be drawn expanded. */
  readonly expandRequested: (elementId: string) => void;

  /** Raised when the user asks for one to be closed again. */
  readonly collapseRequested: (elementId: string) => void;
}

/** How much larger than the 14px glyph the pressable area is, for a usable target. */
const TARGET_SIZE = 24;

export class ExpandControls {
  readonly #paper: dia.Paper;
  readonly #host: HTMLElement;
  readonly #callbacks: ExpandControlCallbacks;
  readonly #strings: CanvasStringsContract;

  readonly #buttons = new Map<string, HTMLButtonElement>();

  #frame: number | null = null;
  #attached = false;

  public constructor(paper: dia.Paper, host: HTMLElement, callbacks: ExpandControlCallbacks, strings: CanvasStringsContract) {
    this.#paper = paper;
    this.#host = host;
    this.#callbacks = callbacks;
    this.#strings = strings;
  }

  public attach(): this {
    if (this.#attached) return this;

    // Everything that can move a marker: the cells themselves, and the paper under them.
    this.#paper.model.on('add remove reset change:position change:size change:bpmn', this.#schedule);
    this.#paper.on('translate scale resize', this.#schedule);

    // The other half of the gesture. Both directions live here so the two cannot disagree about
    // which shapes are expandable — the button's presence and the double-click's target are the
    // same question asked of the same metadata.
    this.#paper.on('element:pointerdblclick', this.#handleDoubleClick);

    this.#attached = true;
    this.#schedule();

    return this;
  }

  public dispose(): void {
    if (!this.#attached) return;

    this.#paper.model.off('add remove reset change:position change:size change:bpmn', this.#schedule);
    this.#paper.off('translate scale resize', this.#schedule);
    this.#paper.off('element:pointerdblclick', this.#handleDoubleClick);

    if (this.#frame !== null) cancelAnimationFrame(this.#frame);
    this.#frame = null;

    for (const button of this.#buttons.values()) button.remove();
    this.#buttons.clear();

    this.#attached = false;
  }

  /**
   * Coalesced to one pass per frame.
   *
   * A drag fires `change:position` for every pointer move, and each pass reads layout — so doing
   * the work per event is the difference between a button that follows the shape and a drag that
   * stutters.
   */
  readonly #schedule = (): void => {
    if (this.#frame !== null) return;

    this.#frame = requestAnimationFrame(() => {
      this.#frame = null;
      this.#render();
    });
  };

  readonly #handleDoubleClick = (view: dia.ElementView, event: dia.Event): void => {
    const elementId = collapseTargetOf(view, event);
    if (elementId === null) return;

    this.#callbacks.collapseRequested(elementId);
  };

  #render(): void {
    const seen = new Set<string>();
    const origin = this.#host.getBoundingClientRect();

    for (const element of this.#paper.model.getElements()) {
      const facts = element.get('bpmn') as CellFacts | undefined;
      const marker = facts?.expandMarker;
      if (marker === null || marker === undefined) continue;

      // A view the paper has culled is off screen; a button for it would sit at the edge of the
      // viewport pointing at nothing.
      if (findView(this.#paper, element) === undefined) continue;

      const id = String(element.id);
      seen.add(id);

      const centre = this.#paper.localToClientPoint({
        x: element.position().x + marker.x + marker.size / 2,
        y: element.position().y + marker.y + marker.size / 2,
      });

      const button = this.#buttons.get(id) ?? this.#create(id, facts?.name ?? null);
      this.#label(button, facts?.name ?? null);

      button.style.left = `${(centre.x - origin.left - TARGET_SIZE / 2).toString()}px`;
      button.style.top = `${(centre.y - origin.top - TARGET_SIZE / 2).toString()}px`;
    }

    for (const [id, button] of this.#buttons) {
      if (seen.has(id)) continue;

      button.remove();
      this.#buttons.delete(id);
    }
  }

  #create(elementId: string, name: string | null): HTMLButtonElement {
    const button = document.createElement('button');

    button.type = 'button';
    button.className = 'bpmn-expand';
    button.style.width = `${TARGET_SIZE.toString()}px`;
    button.style.height = `${TARGET_SIZE.toString()}px`;

    // Named after what it does and what it does it to, because a page full of buttons all called
    // "Expand sub-process" tells a reader which control they are on and nothing about which shape.
    this.#label(button, name);

    button.addEventListener('click', (event) => {
      event.preventDefault();
      this.#callbacks.expandRequested(elementId);
    });

    this.#host.append(button);
    this.#buttons.set(elementId, button);

    return button;
  }

  #label(button: HTMLButtonElement, name: string | null): void {
    const label = name === null || name.length === 0
      ? canvasText(this.#strings, 'Expand')
      : canvasText(this.#strings, 'ExpandNamed', name);
    button.setAttribute('aria-label', label);
    button.title = label;
  }
}
