import type { dia } from '@joint/core';

import * as constants from '../geometry/constants.js';
import type { LabelEditRequestPayload } from '../interop/contracts.js';
import { findCell, findView } from '../graph/cells.js';
import { AnchorResolver } from './anchor.js';
import { collapseTargetOf } from './expand-target.js';
import { isProvisional } from './provisional.js';

/** What the controller tells the host when a label should be edited. */
export interface LabelEditCallbacks {
  readonly editRequested: (payload: LabelEditRequestPayload) => void;

  /**
   * The text the document holds for an element, or null if it has none to edit.
   *
   * The canvas cannot answer this itself. What is drawn is the wrapped text, with line breaks this
   * side inserted; what the user is editing is the string in the document. Editing the wrapped form
   * would bake the current box width into the name.
   */
  readonly textOf: (elementId: string) => string | null;
}

/**
 * Opens the inline label editor.
 *
 * Section 4.5 is categorical: label editing is always in place, never a popup, dialog or modal. So
 * this does not edit anything — it works out where the label is, what it says and how the canvas
 * drew it, and hands that to the Blazor overlay that renders on top. The canvas keeps the geometry,
 * Blazor keeps the text input, and neither has to reimplement the other.
 *
 * The metrics are read off the rendered text node rather than recomputed. Section 4.5 wants the
 * editor's wrapping to match the canvas's, and the only measurement guaranteed to match what the
 * canvas did is the one the canvas left behind.
 */
export class LabelEditController {
  readonly #paper: dia.Paper;
  readonly #anchors: AnchorResolver;
  readonly #callbacks: LabelEditCallbacks;
  #attached = false;

  public constructor(host: HTMLElement, paper: dia.Paper, callbacks: LabelEditCallbacks) {
    this.#paper = paper;
    this.#anchors = new AnchorResolver(host, paper);
    this.#callbacks = callbacks;
  }

  public attach(): this {
    if (this.#attached) return this;
    this.#attached = true;

    this.#paper.on('element:pointerdblclick', this.#handleDoubleClick);
    this.#paper.on('link:pointerdblclick', this.#handleDoubleClick);

    return this;
  }

  public dispose(): void {
    if (!this.#attached) return;
    this.#attached = false;

    this.#paper.off('element:pointerdblclick', this.#handleDoubleClick);
    this.#paper.off('link:pointerdblclick', this.#handleDoubleClick);
  }

  /** Opens the editor for an element the host named — the keyboard path (F2), from outside. */
  public edit(elementId: string): boolean {
    const cell = findCell(this.#paper.model, elementId);
    if (cell === undefined) return false;

    const view = findView(this.#paper, cell);
    return view === undefined ? false : this.#emit(view);
  }

  readonly #handleDoubleClick = (view: dia.CellView, event: dia.Event): void => {
    if (isProvisional(view.model)) return;

    // An expanded Sub-Process's own body is a collapse, not a label edit. Both controllers ask the
    // same predicate rather than testing for each other, so neither has to know the other is
    // listening and they cannot drift into both claiming the click.
    if (collapseTargetOf(view, event) !== null) return;

    this.#emit(view);
  };

  #emit(view: dia.CellView): boolean {
    const elementId = String(view.model.id);

    const text = this.#callbacks.textOf(elementId);
    if (text === null) return false;

    const label = labelNodeOf(view);
    const anchor = label === null
      ? this.#anchors.ofRect(view.el.getBoundingClientRect())
      : this.#anchors.ofRect(label.getBoundingClientRect());

    const style = label === null ? null : getComputedStyle(label);

    // Font size is read in CSS pixels, which is the size after the paper transform; the overlay is
    // told the unscaled size and the scale separately so it can lay text out in paper units the way
    // the canvas does and then be scaled as a whole.
    const scale = this.#anchors.scale;
    const renderedSize = style === null ? Number.NaN : Number.parseFloat(style.fontSize);
    const fontSize = Number.isFinite(renderedSize) && scale > 0
      ? renderedSize / scale
      : constants.LABEL_FONT_SIZE;

    this.#callbacks.editRequested({
      elementId,
      anchor,
      text,
      fontFamily: style?.fontFamily ?? constants.LABEL_FONT_FAMILY,
      fontSize,
      lineHeight: fontSize * constants.LABEL_LINE_HEIGHT,
      textAlign: alignmentOf(label),
      scale,
    });

    return true;
  }
}

/** The rendered label, or null when the element has none drawn — an unnamed shape, typically. */
function labelNodeOf(view: dia.CellView): SVGGraphicsElement | null {
  const node = view.el.querySelector('[joint-selector="label"], .bpmn-label, .label');
  if (!(node instanceof SVGGraphicsElement)) return null;

  // An empty text node still has a box, but it is a zero-width sliver in the middle of the shape.
  // Better to fall back to the shape itself than to open a one-pixel editor.
  const box = node.getBoundingClientRect();
  return box.width > 1 && box.height > 1 ? node : null;
}

function alignmentOf(label: SVGGraphicsElement | null): 'left' | 'center' | 'right' {
  const anchor = label?.getAttribute('text-anchor') ?? 'middle';

  if (anchor === 'start') return 'left';
  if (anchor === 'end') return 'right';
  return 'center';
}
