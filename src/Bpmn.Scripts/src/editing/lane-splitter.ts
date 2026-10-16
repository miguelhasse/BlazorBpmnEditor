import { dia } from '@joint/core';

import { LANE_MINIMUM_EXTENT } from '../geometry/constants.js';

/** What a finished splitter drag asks the host to do. */
export interface LaneSplitterCallbacks {
  readonly splitterMoved: (upperLaneId: string, position: number) => void;
}

/** One splitter: the shared edge between two adjacent Lanes of a Pool. */
export interface SplitterSpec {
  /** The Lane before the splitter (above it, or to its left in a vertical Pool). */
  readonly upperLaneId: string;
  readonly isHorizontal: boolean;
  /** The splitter's coordinate along the stacking axis. */
  readonly position: number;
  /** Where the line starts and how long it is, across the stacking axis. */
  readonly from: number;
  readonly length: number;
  /** The range the splitter may travel without pushing either Lane under the minimum. */
  readonly min: number;
  readonly max: number;
}

/** The grab area either side of the line, in diagram units. */
const HIT_SIZE = 8;

/**
 * Splitters between the Lanes of each Pool.
 *
 * Lanes always fill their Pool, so a splitter is the only way they are sized against one another:
 * it moves the shared edge and nothing else. Horizontal Pools stack their Lanes top to bottom and
 * get horizontal splitters; vertical Pools get vertical ones.
 *
 * Like resizing, a drag draws a preview and reports the final position. The model is never changed
 * here, because whether the move is allowed is a fact about the document.
 */
export class LaneSplitterController {
  readonly #paper: dia.Paper;
  readonly #callbacks: LaneSplitterCallbacks;

  #layer: SVGGElement | null = null;
  #frame = 0;
  #drag: { spec: SplitterSpec; position: number; line: SVGLineElement } | null = null;

  public constructor(paper: dia.Paper, callbacks: LaneSplitterCallbacks) {
    this.#paper = paper;
    this.#callbacks = callbacks;

    paper.model.on('add remove reset change:position change:size change:parent', this.#schedule);
  }

  /** Redraws the splitters from the graph as it stands. */
  public draw(): void {
    this.#frame = 0;
    if (this.#drag !== null) return;

    this.#layer?.remove();
    this.#layer = null;

    const specs = splittersOf(this.#paper.model);
    if (specs.length === 0) return;

    const layer = this.#paper.el.ownerDocument.createElementNS('http://www.w3.org/2000/svg', 'g');
    layer.setAttribute('class', 'bpmn-lane-splitters');
    this.#paper.getLayerView(dia.Paper.Layers.FRONT).el.appendChild(layer);
    this.#layer = layer;

    for (const spec of specs) {
      layer.appendChild(this.#createSplitter(spec));
    }
  }

  public destroy(): void {
    this.#paper.model.off('add remove reset change:position change:size change:parent', this.#schedule);
    if (this.#frame !== 0) cancelAnimationFrame(this.#frame);
    this.#endDrag();
    this.#layer?.remove();
    this.#layer = null;
  }

  // An arrow field so it can be registered and removed by identity.
  readonly #schedule = (): void => {
    if (this.#frame !== 0) return;
    this.#frame = requestAnimationFrame(() => { this.draw(); });
  };

  #createSplitter(spec: SplitterSpec): SVGGElement {
    const doc = this.#paper.el.ownerDocument;
    const group = doc.createElementNS('http://www.w3.org/2000/svg', 'g');
    group.setAttribute('class', 'bpmn-lane-splitter');
    group.setAttribute('data-lane', spec.upperLaneId);

    const hit = doc.createElementNS('http://www.w3.org/2000/svg', 'rect');
    hit.setAttribute('class', 'bpmn-lane-splitter-hit');
    hit.setAttribute('fill', 'transparent');
    hit.setAttribute('cursor', spec.isHorizontal ? 'row-resize' : 'col-resize');

    if (spec.isHorizontal) {
      hit.setAttribute('x', String(spec.from));
      hit.setAttribute('y', String(spec.position - (HIT_SIZE / 2)));
      hit.setAttribute('width', String(spec.length));
      hit.setAttribute('height', String(HIT_SIZE));
    } else {
      hit.setAttribute('x', String(spec.position - (HIT_SIZE / 2)));
      hit.setAttribute('y', String(spec.from));
      hit.setAttribute('width', String(HIT_SIZE));
      hit.setAttribute('height', String(spec.length));
    }

    group.appendChild(hit);

    // The paper starts its own gestures (pan, rubber band, element drag) from these events, so
    // the splitter has to keep them to itself.
    for (const type of ['pointerdown', 'mousedown', 'touchstart']) {
      hit.addEventListener(type, (event) => { event.stopPropagation(); });
    }

    hit.addEventListener('pointerdown', (event) => { this.#beginDrag(spec, event, hit); });

    return group;
  }

  #beginDrag(spec: SplitterSpec, event: PointerEvent, hit: SVGRectElement): void {
    if (event.button !== 0) return;
    event.preventDefault();

    const doc = this.#paper.el.ownerDocument;
    const line = doc.createElementNS('http://www.w3.org/2000/svg', 'line');
    line.setAttribute('class', 'bpmn-lane-splitter-preview');
    this.#placeLine(line, spec, spec.position);
    this.#layer?.appendChild(line);

    this.#drag = { spec, position: spec.position, line };
    hit.setPointerCapture(event.pointerId);

    const move = (moved: PointerEvent): void => {
      if (this.#drag === null) return;

      const local = this.#paper.clientToLocalPoint({ x: moved.clientX, y: moved.clientY });
      const raw = spec.isHorizontal ? local.y : local.x;
      const position = Math.min(Math.max(raw, spec.min), spec.max);

      this.#drag.position = position;
      this.#placeLine(line, spec, position);
    };

    const finish = (): void => {
      hit.removeEventListener('pointermove', move);
      hit.removeEventListener('pointerup', finish);
      hit.removeEventListener('pointercancel', cancel);

      const drag = this.#drag;
      this.#endDrag();
      this.draw();

      // A click without movement is not a resize and must not report a command that changes nothing.
      if (drag === null || Math.abs(drag.position - spec.position) < 0.01) return;

      this.#callbacks.splitterMoved(spec.upperLaneId, drag.position);
    };

    const cancel = (): void => {
      hit.removeEventListener('pointermove', move);
      hit.removeEventListener('pointerup', finish);
      hit.removeEventListener('pointercancel', cancel);
      this.#endDrag();
      this.draw();
    };

    hit.addEventListener('pointermove', move);
    hit.addEventListener('pointerup', finish);
    hit.addEventListener('pointercancel', cancel);
  }

  #placeLine(line: SVGLineElement, spec: SplitterSpec, position: number): void {
    const to = spec.from + spec.length;

    line.setAttribute('x1', String(spec.isHorizontal ? spec.from : position));
    line.setAttribute('y1', String(spec.isHorizontal ? position : spec.from));
    line.setAttribute('x2', String(spec.isHorizontal ? to : position));
    line.setAttribute('y2', String(spec.isHorizontal ? position : to));
  }

  #endDrag(): void {
    this.#drag?.line.remove();
    this.#drag = null;
  }
}

/** The splitters the graph implies: one between each pair of adjacent Lanes of a Pool. */
export function splittersOf(graph: dia.Graph): SplitterSpec[] {
  const byPool = new Map<string, dia.Element[]>();

  for (const element of graph.getElements()) {
    const bpmn = element.get('bpmn') as { readonly elementType?: string } | undefined;
    if (bpmn?.elementType !== 'lane') continue;

    // Read off the attribute: a Lane whose Pool is not embedded has no parent at run time.
    const key = element.get('parent');
    if (key === undefined) continue;

    byPool.set(key, [...(byPool.get(key) ?? []), element]);
  }

  const specs: SplitterSpec[] = [];

  for (const lanes of byPool.values()) {
    const bpmn = lanes[0]?.get('bpmn') as { readonly isHorizontal?: boolean } | undefined;
    const isHorizontal = bpmn?.isHorizontal !== false;
    const along = (lane: dia.Element): number => (isHorizontal ? lane.position().y : lane.position().x);

    lanes.sort((first, second) => along(first) - along(second));

    for (let index = 0; index + 1 < lanes.length; index++) {
      const upper = lanes[index];
      const lower = lanes[index + 1];
      if (upper === undefined || lower === undefined) continue;

      const upperAt = upper.position();
      const upperSize = upper.size();
      const lowerAt = lower.position();
      const lowerSize = lower.size();

      const origin = isHorizontal ? upperAt.y : upperAt.x;
      const end = isHorizontal ? lowerAt.y + lowerSize.height : lowerAt.x + lowerSize.width;

      specs.push({
        upperLaneId: String(upper.id),
        isHorizontal,
        position: origin + (isHorizontal ? upperSize.height : upperSize.width),
        from: isHorizontal ? upperAt.x : upperAt.y,
        length: isHorizontal ? upperSize.width : upperSize.height,
        min: origin + LANE_MINIMUM_EXTENT,
        max: end - LANE_MINIMUM_EXTENT,
      });
    }
  }

  return specs;
}
