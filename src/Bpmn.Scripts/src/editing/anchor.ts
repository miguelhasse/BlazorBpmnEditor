import type { dia } from '@joint/core';

import type { AnchorContract, GeometryBounds } from '../interop/contracts.js';

/**
 * Expresses a place on the canvas in both of the spaces that matter.
 *
 * Paper coordinates are the document's: a context-menu target's bounds, a label's box and anything
 * that turns into a command have to be in them. HTML chrome lives in the canvas host's box instead,
 * and converting between the two means knowing the paper transform and the host's position on the
 * page. Doing that once, here, is what lets the Blazor side position an overlay without knowing
 * anything about JointJS — and stops the two answers drifting apart because two callers converted
 * differently.
 */
export class AnchorResolver {
  readonly #host: HTMLElement;
  readonly #paper: dia.Paper;

  public constructor(host: HTMLElement, paper: dia.Paper) {
    this.#host = host;
    this.#paper = paper;
  }

  /** The paper's current scale, which chrome needs to draw text at the size the canvas shows. */
  public get scale(): number {
    return this.#paper.scale().sx;
  }

  /** A client-space rectangle, in both spaces. */
  public ofRect(rect: DOMRect): AnchorContract {
    const local = this.#paper.clientToLocalRect(rect.left, rect.top, rect.width, rect.height);

    return { bounds: round(local), client: this.#relativeToHost(rect) };
  }

  /** A client-space point as a zero-size anchor, which is what a blank-canvas request uses. */
  public ofPoint(at: { x: number; y: number }): AnchorContract {
    const local = this.#paper.clientToLocalPoint(at);
    const host = this.#host.getBoundingClientRect();

    return {
      bounds: round({ x: local.x, y: local.y, width: 0, height: 0 }),
      client: { x: at.x - host.left, y: at.y - host.top, width: 0, height: 0 },
    };
  }

  /** A paper-space rectangle, in both spaces. The inverse of {@link ofRect}. */
  public ofLocalRect(bounds: GeometryBounds): AnchorContract {
    const client = this.#paper.localToClientRect(bounds.x, bounds.y, bounds.width, bounds.height);
    const host = this.#host.getBoundingClientRect();

    return {
      bounds: round(bounds),
      client: {
        x: client.x - host.left,
        y: client.y - host.top,
        width: client.width,
        height: client.height,
      },
    };
  }

  /** The centre of the canvas, for a request that has no pointer behind it. */
  public centre(): { x: number; y: number } {
    const host = this.#host.getBoundingClientRect();
    return { x: host.left + host.width / 2, y: host.top + host.height / 2 };
  }

  #relativeToHost(rect: DOMRect): GeometryBounds {
    const host = this.#host.getBoundingClientRect();

    return {
      x: rect.left - host.left,
      y: rect.top - host.top,
      width: rect.width,
      height: rect.height,
    };
  }
}

/** Paper coordinates are the document's, and the document stores them at two decimals. */
function round(bounds: GeometryBounds): GeometryBounds {
  return {
    x: Math.round(bounds.x * 100) / 100,
    y: Math.round(bounds.y * 100) / 100,
    width: Math.round(bounds.width * 100) / 100,
    height: Math.round(bounds.height * 100) / 100,
  };
}
