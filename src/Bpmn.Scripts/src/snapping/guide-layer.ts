import { dia } from '@joint/core';

import type { Guide } from './alignment.js';

/**
 * Draws alignment guides on the paper's front layer.
 *
 * The front layer already carries the paper transform, so guides are positioned in paper
 * coordinates and stay correct through zoom and pan without any arithmetic here. Stroke width is
 * divided by the current scale so a guide stays one screen pixel wide rather than thickening as
 * the user zooms in.
 */
export class GuideLayer {
  readonly #paper: dia.Paper;
  readonly #group: SVGGElement;

  public constructor(paper: dia.Paper) {
    this.#paper = paper;

    this.#group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    this.#group.setAttribute('class', 'bpmn-snaplines');
    // Guides are decoration: they must never intercept the drag that produced them.
    this.#group.setAttribute('pointer-events', 'none');
    this.#group.setAttribute('aria-hidden', 'true');

    paper.getLayerView(dia.Paper.Layers.FRONT).el.appendChild(this.#group);
  }

  /** Replaces the drawn guides. An empty list clears the layer. */
  public show(guides: readonly Guide[]): void {
    this.clear();
    if (guides.length === 0) return;

    const { sx } = this.#paper.scale();
    const width = sx === 0 ? 1 : 1 / sx;

    for (const guide of guides) {
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      const vertical = guide.orientation === 'vertical';

      line.setAttribute('x1', String(vertical ? guide.position : guide.from));
      line.setAttribute('y1', String(vertical ? guide.from : guide.position));
      line.setAttribute('x2', String(vertical ? guide.position : guide.to));
      line.setAttribute('y2', String(vertical ? guide.to : guide.position));
      line.setAttribute('class', 'bpmn-snapline');
      line.setAttribute('stroke-width', String(width));

      this.#group.appendChild(line);
    }
  }

  public clear(): void {
    this.#group.replaceChildren();
  }

  public destroy(): void {
    this.#group.remove();
  }
}
