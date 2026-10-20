import type { dia, g } from '@joint/core';

import * as constants from '../geometry/constants.js';
import type { AnchorContract, LabelEditRequestPayload } from '../interop/contracts.js';
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
  readonly #host: HTMLElement;
  readonly #paper: dia.Paper;
  readonly #anchors: AnchorResolver;
  readonly #callbacks: LabelEditCallbacks;
  #attached = false;
  #observer: MutationObserver | null = null;
  #resizeObserver: ResizeObserver | null = null;
  #frame: number | null = null;
  #request: LabelEditRequestPayload | null = null;
  #lastAnchor: AnchorContract | null = null;

  public constructor(host: HTMLElement, paper: dia.Paper, callbacks: LabelEditCallbacks) {
    this.#host = host;
    this.#paper = paper;
    this.#anchors = new AnchorResolver(host, paper);
    this.#callbacks = callbacks;
  }

  public attach(): this {
    if (this.#attached) return this;
    this.#attached = true;

    this.#paper.on('element:pointerdblclick', this.#handleDoubleClick);
    this.#paper.on('link:pointerdblclick', this.#handleDoubleClick);
    this.#paper.on('translate scale resize render:done', this.#schedule);
    this.#paper.model.on('change:position change:size', this.#schedule);
    this.#host.addEventListener('input', this.#schedule);
    window.addEventListener('scroll', this.#schedule, true);
    window.addEventListener('resize', this.#schedule);
    this.#observer = new MutationObserver(this.#schedule);
    this.#observer.observe(this.#host, { childList: true, subtree: true });
    this.#resizeObserver = new ResizeObserver(this.#schedule);
    this.#resizeObserver.observe(this.#host);

    return this;
  }

  public dispose(): void {
    if (!this.#attached) return;
    this.#attached = false;

    this.#paper.off('element:pointerdblclick', this.#handleDoubleClick);
    this.#paper.off('link:pointerdblclick', this.#handleDoubleClick);
    this.#paper.off('translate scale resize render:done', this.#schedule);
    this.#paper.model.off('change:position change:size', this.#schedule);
    this.#host.removeEventListener('input', this.#schedule);
    window.removeEventListener('scroll', this.#schedule, true);
    window.removeEventListener('resize', this.#schedule);
    this.#observer?.disconnect();
    this.#resizeObserver?.disconnect();
    if (this.#frame !== null) cancelAnimationFrame(this.#frame);
    this.#frame = null;
    this.#request = null;
    this.#lastAnchor = null;
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
    const request = this.#measure(view);
    if (request === null) return false;

    this.#request = request;
    this.#lastAnchor = request.anchor;
    this.#callbacks.editRequested(request);
    return true;
  }

  #measure(view: dia.CellView): LabelEditRequestPayload | null {
    const elementId = String(view.model.id);

    const text = this.#callbacks.textOf(elementId);
    if (text === null) return null;

    const label = labelNodeOf(view);
    const style = label === null ? null : getComputedStyle(label);

    // Computed SVG font sizes are in user units, before the paper's transform.
    const scale = this.#anchors.scale;
    const renderedSize = style === null ? Number.NaN : Number.parseFloat(style.fontSize);
    const fontSize = Number.isFinite(renderedSize) && renderedSize > 0
      ? renderedSize
      : constants.LABEL_FONT_SIZE;
    const lineHeight = fontSize * constants.LABEL_LINE_HEIGHT;
    const rect = label?.getBoundingClientRect();
    let anchor: AnchorContract;

    if (rect !== undefined && rect.width > 1 && rect.height > 1) {
      anchor = this.#anchors.ofRect(rect);
    } else if (view.model.isElement()) {
      const position = view.model.position();
      const x: unknown = view.model.attr('label/x');
      const y: unknown = view.model.attr('label/y');
      const availableWidth: unknown = view.model.attr('label/data-bpmn-label-width');
      const width = Math.max(80, Math.min(
        typeof availableWidth === 'number' ? availableWidth : constants.EXTERNAL_LABEL_WIDTH,
        constants.EXTERNAL_LABEL_WIDTH,
      ));
      const pointX = typeof x === 'number' ? x : view.model.size().width / 2;
      const pointY = typeof y === 'number' ? y : view.model.size().height / 2;
      const horizontal: unknown = view.model.attr('label/textAnchor');
      const vertical: unknown = view.model.attr('label/textVerticalAnchor');
      anchor = this.#anchors.ofLocalRect({
        x: position.x + pointX - (horizontal === 'start' ? 0 : horizontal === 'end' ? width : width / 2),
        y: position.y + pointY - (vertical === 'top' ? 0 : vertical === 'bottom' ? lineHeight : lineHeight / 2),
        width,
        height: lineHeight,
      });
    } else {
      // A route midpoint is a point on the path, not the centre of its bounding rectangle.
      const point = routeMidpointOf(view as dia.LinkView);
      if (point === null) return null;
      anchor = this.#anchors.ofLocalRect({
        x: point.x - constants.EXTERNAL_LABEL_WIDTH / 2,
        y: point.y - lineHeight / 2,
        width: constants.EXTERNAL_LABEL_WIDTH,
        height: lineHeight,
      });
    }

    return {
      elementId,
      anchor,
      text,
      fontFamily: style?.fontFamily ?? constants.LABEL_FONT_FAMILY,
      fontSize,
      lineHeight,
      textAlign: alignmentOf(label),
      scale,
    };
  }

  readonly #schedule = (): void => {
    if (this.#frame !== null || this.#request === null || this.#host.querySelector('.bpmn-label-editor') === null) return;
    this.#frame = requestAnimationFrame(() => {
      this.#frame = null;
      this.#layout();
    });
  };

  #layout(): void {
    const box = this.#host.querySelector<HTMLElement>('.bpmn-label-editor');
    const input = box?.querySelector<HTMLTextAreaElement>('textarea');
    const request = this.#request;
    if (box === null || input == null || request === null || box.dataset.bpmnElement !== request.elementId) return;

    const cell = findCell(this.#paper.model, request.elementId);
    const view = cell === undefined ? undefined : findView(this.#paper, cell);
    const current = view === undefined ? null : this.#measure(view);
    if (current !== null) this.#lastAnchor = current.anchor;
    const scale = this.#anchors.scale;
    const anchor = current?.anchor ?? this.#anchors.ofLocalRect((this.#lastAnchor ?? request.anchor).bounds);
    const width = Math.max(anchor.client.width, 80 * scale);
    const lineHeight = request.lineHeight * scale;

    box.style.width = `${width.toString()}px`;
    input.style.fontSize = `${(request.fontSize * scale).toString()}px`;
    input.style.lineHeight = `${lineHeight.toString()}px`;
    input.style.height = '0px';
    const height = Math.max(lineHeight, input.scrollHeight);
    input.style.height = `${height.toString()}px`;
    box.style.height = `${height.toString()}px`;

    const vertical: unknown = cell?.isElement() === true ? cell.attr('label/textVerticalAnchor') : 'middle';
    const top = vertical === 'top' ? anchor.client.y
      : vertical === 'bottom' ? anchor.client.y + anchor.client.height - height
        : anchor.client.y + (anchor.client.height - height) / 2;
    const left = request.textAlign === 'left' ? anchor.client.x
      : request.textAlign === 'right' ? anchor.client.x + anchor.client.width - width
        : anchor.client.x + (anchor.client.width - width) / 2;
    box.style.left = `${left.toString()}px`;
    box.style.top = `${top.toString()}px`;
  }
}

/** JointJS returns null until a path is drawn, although its declaration excludes that case. */
function routeMidpointOf(view: dia.LinkView): g.Point | null {
  return view.getPointAtRatio(0.5);
}

/** Hidden shape labels retain their default geometry; links use a different text selector. */
function labelNodeOf(view: dia.CellView): SVGGraphicsElement | null {
  const node = view.el.querySelector('[joint-selector="label"], .bpmn-label, [label-idx] [joint-selector="text"]');
  if (!(node instanceof SVGGraphicsElement)) return null;
  return node;
}

function alignmentOf(label: SVGGraphicsElement | null): 'left' | 'center' | 'right' {
  const anchor = label?.getAttribute('text-anchor') ?? 'middle';

  if (anchor === 'start') return 'left';
  if (anchor === 'end') return 'right';
  return 'center';
}
