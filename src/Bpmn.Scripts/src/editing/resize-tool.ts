import { dia, elementTools } from '@joint/core';

import type { GeometryBounds } from '../interop/contracts.js';

/*
 * Destructured for the same reason `connect-tool.ts` destructures ToolsView: `dia.ToolsView` is
 * declared as both a class and a namespace, and reading it through the namespace makes the lint
 * rule resolve `dia` as a type-only import, which then fails to compile.
 */
const { ToolsView } = dia;

/** Which corner or edge a handle drags. */
export type ResizeAnchor = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w';

/** What a finished resize asks the host to do. */
export interface ResizeCallbacks {
  readonly resizeCommitted: (elementId: string, bounds: GeometryBounds) => void;
}

/**
 * Outline-border resize handles.
 *
 * Built on `elementTools.Control`, which is JointJS's own draggable control point: it owns the
 * pointer capture, the document-level move tracking and the batching, so what is added here is the
 * arithmetic of which edge moves and the decision about what to do with the result.
 *
 * **The model is never resized optimistically.** A drag draws a preview outline and reports the
 * final bounds; the host applies the command and sends back a document, which re-renders. The
 * alternative — resize now, undo it if the host refuses — needs a rollback path for a refusal that
 * only the host can detect, because whether a container would strand its contents is a fact about
 * the document, not about the canvas.
 */
export class ResizeController {
  readonly #callbacks: ResizeCallbacks;

  #preview: SVGRectElement | null = null;

  /**
   * The bounds the preview is showing.
   *
   * Kept here rather than read back off the rectangle's attributes at commit time: the DOM is the
   * drawing, not the state, and round-tripping numbers through strings invites exactly the kind of
   * rounding difference that makes a shape drift by a fraction on every drag.
   */
  #pending: { elementId: string; bounds: GeometryBounds } | null = null;

  public constructor(callbacks: ResizeCallbacks) {
    this.#callbacks = callbacks;
  }

  /**
   * The handles for one element.
   *
   * Only the anchors that mean something are offered; a handle that visibly does nothing is
   * worse than no handle.
   */
  public toolsFor(anchors: readonly ResizeAnchor[]): dia.ToolsView {
    return new ToolsView({
      name: 'bpmn-resize',
      tools: anchors.map((anchor) => this.#handle(anchor)),
    });
  }

  public destroy(): void {
    this.#clearPreview();
  }

  #handle(anchor: ResizeAnchor): dia.ToolView {
    const [ax, ay] = ANCHORS[anchor];

    /*
     * Arrow functions rather than a captured `this`: JointJS's `extend` takes a plain object whose
     * methods are called with the tool as their receiver, so the controller has to come in through
     * the closure. Binding it as a variable would alias `this` to two different things in one
     * scope, which is exactly what makes this pattern hard to read.
     */
    const preview = (view: dia.ElementView, bounds: GeometryBounds): void => {
      this.#showPreview(view, bounds);
    };

    const commit = (view: dia.ElementView): void => { this.#commit(view); };

    const Handle = elementTools.Control.extend({
      name: `bpmn-resize-${anchor}`,

      children: [{
        tagName: 'rect',
        selector: 'handle',
        attributes: {
          'x': -4,
          'y': -4,
          'width': 8,
          'height': 8,
          'fill': 'var(--bpmn-selection-color)',
          'stroke': 'var(--bpmn-fill)',
          'stroke-width': 1.5,
          'cursor': `${anchor}-resize`,
          'class': 'bpmn-resize-handle',
        },
      }],

      getPosition(view: dia.ElementView) {
        const { width, height } = view.model.size();
        return { x: width * ax, y: height * ay };
      },

      setPosition(view: dia.ElementView, coordinates: { x: number; y: number }) {
        preview(view, resizeTo(view, anchor, coordinates));
      },

      /*
       * The base class restores the paper's own event handling and closes the batch, so it has to
       * run; skipping it leaves the paper deaf to every later gesture. It is declared protected,
       * which `extend` has no way to express, hence the one cast.
       */
      onPointerUp(this: dia.ToolView, event: dia.Event) {
        const base = elementTools.Control.prototype as unknown as {
          onPointerUp: (this: dia.ToolView, event: dia.Event) => void;
        };

        base.onPointerUp.call(this, event);
        commit(this.relatedView as dia.ElementView);
      },
    }) as new () => dia.ToolView;

    return new Handle();
  }

  #showPreview(view: dia.ElementView, bounds: GeometryBounds): void {
    const paper = view.paper;
    if (paper === null) return;

    this.#preview ??= createPreview(paper);
    this.#preview.setAttribute('x', String(bounds.x));
    this.#preview.setAttribute('y', String(bounds.y));
    this.#preview.setAttribute('width', String(bounds.width));
    this.#preview.setAttribute('height', String(bounds.height));

    this.#pending = { elementId: String(view.model.id), bounds };
  }

  #commit(view: dia.ElementView): void {
    const pending = this.#pending;
    this.#clearPreview();

    // Nothing pending means the pointer went down and up without moving: a click on a handle,
    // which is not a resize and must not report a command that changes nothing.
    if (pending?.elementId !== String(view.model.id)) return;

    this.#callbacks.resizeCommitted(pending.elementId, pending.bounds);
  }

  #clearPreview(): void {
    this.#preview?.remove();
    this.#preview = null;
    this.#pending = null;
  }
}

/**
 * The bounds a drag implies.
 *
 * `coordinates` are relative to the element's own origin, which is what `Control` hands over. An
 * anchor on the left or top moves the origin and takes the difference out of the size; one on the
 * right or bottom leaves the origin alone and sets the size directly; a mid-edge anchor leaves its
 * other axis untouched.
 */
export function resizeTo(
  view: dia.ElementView,
  anchor: ResizeAnchor,
  coordinates: { x: number; y: number },
): GeometryBounds {
  const [ax, ay] = ANCHORS[anchor];
  const { x, y } = view.model.position();
  const { width, height } = view.model.size();

  const horizontal = axis(x, width, ax, coordinates.x);
  const vertical = axis(y, height, ay, coordinates.y);

  return {
    x: horizontal.start,
    y: vertical.start,
    width: horizontal.extent,
    height: vertical.extent,
  };
}

/** One axis of a resize: where the edge went, clamped so a shape cannot be turned inside out. */
function axis(
  start: number,
  extent: number,
  anchor: number,
  moved: number,
): { start: number; extent: number } {
  if (anchor === 0.5) return { start, extent };

  if (anchor === 0) {
    const clamped = Math.min(moved, extent - MINIMUM);
    return { start: start + clamped, extent: extent - clamped };
  }

  return { start, extent: Math.max(moved, MINIMUM) };
}

/** Matches the document editor's own floor, so the canvas never offers a size it would refuse. */
const MINIMUM = 20;

/** Where each anchor sits on the shape, as a fraction of its size. */
const ANCHORS: Readonly<Record<ResizeAnchor, readonly [number, number]>> = {
  nw: [0, 0],
  n: [0.5, 0],
  ne: [1, 0],
  e: [1, 0.5],
  se: [1, 1],
  s: [0.5, 1],
  sw: [0, 1],
  w: [0, 0.5],
};

/**
 * Which handles an element should offer.
 *
 * A Lane offers none: it fills its Pool (section 1.5.3) and is sized with the splitters between
 * Lanes. Everything that is not a container offers nothing either: BPMN gives Events and Gateways
 * fixed notation sizes, and a resized Task is a layout choice this milestone has no command for.
 */
export function anchorsFor(elementType: string): readonly ResizeAnchor[] {
  switch (elementType) {
    case 'pool':
    case 'subProcess':
    case 'eventSubProcess':
    case 'transaction':
    case 'adHocSubProcess':
    case 'textAnnotation':
    case 'group':
      return ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];

    // A Lane always fills its Pool, so it has no handles of its own: the splitters between Lanes
    // are the only way to size one.
    case 'lane':
    default:
      return [];
  }
}

function createPreview(paper: dia.Paper): SVGRectElement {
  const preview = paper.el.ownerDocument.createElementNS('http://www.w3.org/2000/svg', 'rect');
  preview.setAttribute('class', 'bpmn-resize-preview');
  preview.setAttribute('fill', 'none');

  // On the paper's front layer, so it is drawn in diagram coordinates — the transform comes for
  // free rather than needing the zoom applied by hand — and above the shape being resized.
  paper.getLayerView(dia.Paper.Layers.FRONT).el.appendChild(preview);
  return preview;
}
