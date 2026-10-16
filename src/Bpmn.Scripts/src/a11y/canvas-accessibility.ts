import type { dia } from '@joint/core';

import { isProvisional } from '../editing/provisional.js';
import type { CanvasStringsContract } from '../interop/contracts.js';
import { canvasText } from './canvas-text.js';

/**
 * The base accessibility skeleton for the canvas.
 *
 * Deliberately a skeleton. An SVG diagram is not navigable by a screen reader on its own, and the
 * full answer — a parallel off-screen tree with graphics roles, plus arrow-key traversal along
 * edges — is scheduled as its own effort. What is here is the part that is cheap, correct, and
 * wrong to leave out: every drawn cell carries an accessible name and a graphics role, and the
 * canvas exposes a one-line summary of what it is showing.
 *
 * Shipping this much now also means the later work is an extension rather than a retrofit: the
 * labels it will read already exist and are produced from the semantic model, not from the SVG.
 */
export class CanvasAccessibility {
  readonly #paper: dia.Paper;
  readonly #summary: HTMLElement;
  readonly #strings: CanvasStringsContract;

  /**
   * The label last computed for a cell, with the inputs it came from.
   *
   * The pass below runs on every `render:done` over every cell, and computing a label is a string lookup plus a whitespace
   * regex plus a formatted message. The inputs almost never change, so the string is reused until they do. The DOM comparison
   * in `#label` stays, because a view the paper has just remounted is a new element with no label on it.
   */
  readonly #labels = new WeakMap<dia.Cell, { readonly kind: string; readonly name: string | undefined; readonly label: string }>();

  public constructor(paper: dia.Paper, hostElement: HTMLElement, strings: CanvasStringsContract) {
    this.#paper = paper;
    this.#strings = strings;

    // A visually hidden element rather than an aria-label on the container: the summary changes
    // when a document loads, and a live region is how that reaches a screen reader without
    // stealing focus.
    this.#summary = document.createElement('p');
    this.#summary.className = 'bpmn-canvas__summary';
    this.#summary.setAttribute('aria-live', 'polite');
    hostElement.appendChild(this.#summary);

    this.#paper.on('render:done', this.#labelCells);
  }

  /** Announces what the canvas is now showing. */
  public describe(diagramName: string | null, shapeCount: number, edgeCount: number): void {
    const name = diagramName === null || diagramName.length === 0 ? canvasText(this.#strings, 'Diagram') : diagramName;
    const shapes = canvasText(this.#strings, shapeCount === 1 ? 'Shape.One' : 'Shape.Many', shapeCount);
    const edges = canvasText(this.#strings, edgeCount === 1 ? 'Connector.One' : 'Connector.Many', edgeCount);

    this.#summary.textContent = canvasText(this.#strings, 'Summary', name, shapes, edges);
  }

  public destroy(): void {
    this.#paper.off('render:done', this.#labelCells);
    this.#summary.remove();
  }

  /**
   * Gives every rendered cell an accessible name and a graphics role.
   *
   * Run after render rather than on cell add, because a cell's view does not exist until the
   * asynchronous paper has drawn it, and labelling the model would have nothing to attach to.
   */
  readonly #labelCells = (): void => {
    /*
     * Every cell, not the ones currently in view.
     *
     * Labelling only `findElementViewsInArea(getArea())` left six of the twenty-four shapes in
     * pizza-store.bpmn with no accessible name at all, because they were outside the viewport when
     * the first render finished and nothing labels a shape again when the viewport changes. A
     * screen reader walks the DOM, not the visible rectangle. Two setAttribute calls per cell is
     * not the expensive part of drawing a diagram.
     */
    for (const cell of this.#paper.model.getCells()) {
      // A cell that has not been drawn yet has no view, which the JointJS signature does not admit. The link a connector gesture is
      // drawing is skipped too: it is scaffolding, not notation, and announcing it would tell a screen-reader user about a
      // connector that does not exist.
      if (isProvisional(cell)) continue;

      const view = cell.findView(this.#paper) as dia.CellView | null | undefined;
      if (view !== null && view !== undefined) this.#label(view);
    }
  };

  #label(view: dia.CellView): void {
    const cell = view.model;
    // The shape factories store the BPMN type as `bpmn.elementType`; reading `bpmn.type` instead
    // silently falls back for every cell, which reads as "Element: Start" rather than
    // "Start Event: Start".
    const bpmn = cell.get('bpmn') as { readonly elementType?: string; readonly name?: string | null } | undefined;
    // The cell's own name, not the drawn label. They were the same thing until an expanded
    // Sub-Process stopped drawing one, and a name that disappears when the notation changes is a
    // name that was never really being read from the model.
    const name = (cell.attr('label/text') as string | undefined) ?? bpmn?.name ?? undefined;
    const kind = bpmn?.elementType ?? (cell.isLink() ? 'connector' : 'element');

    const known = this.#labels.get(cell);
    let label: string;
    if (known?.kind === kind && known.name === name) {
      label = known.label;
    } else {
      // Read as "User Task: Approve order" — the type first, because the type is what tells a
      // listener what the thing is, and many BPMN elements legitimately have no name at all.
      const readable = this.#strings.values[`Type.${kind}`]
        ?? canvasText(this.#strings, cell.isLink() ? 'Type.connector' : 'Type.element');
      label = name === undefined || name.length === 0
        ? readable
        : canvasText(this.#strings, 'ElementName', readable, name.replace(/\s+/g, ' ').trim());
      this.#labels.set(cell, { kind, name, label });
    }

    /*
     * Written only when it changed. This runs on every `render:done` — which fires on every pan,
     * zoom and edit — and on a large diagram that is a thousand DOM writes per frame for a label
     * that almost never differs from the one already there.
     */
    if (view.el.getAttribute('aria-label') === label) return;

    view.el.setAttribute('role', 'graphics-symbol');
    view.el.setAttribute('aria-label', label);
  }
}
