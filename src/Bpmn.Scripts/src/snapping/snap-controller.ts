import type { dia } from '@joint/core';

import type { SnappingOptionsContract } from '../interop/contracts.js';
import { snapToPeers, type Box } from './alignment.js';
import { GuideLayer } from './guide-layer.js';

/**
 * The part of a `change:position` option bag this needs.
 *
 * `translateBy` is not in the published typings, which is why it is named here rather than reached
 * for through a cast at the call site.
 */
interface MoveOptions {
  readonly translateBy?: unknown;
}

/**
 * Applies alignment guides whenever an element moves.
 *
 * Driven by `change:position` on the graph rather than by a drag handler, so it is correct for
 * every way an element can move — a pointer drag, a keyboard nudge, a command, an undo — without
 * each of those having to remember to ask for snapping. Element dragging itself arrives with the
 * editing interactions; until then this responds to programmatic moves.
 *
 * Guides are cleared on an idle timer instead of on pointer-up for the same reason: there is one
 * rule, and it does not depend on which input produced the movement.
 */
export class SnapController {
  /** Long enough to span the gap between frames of a drag, short enough to feel immediate. */
  static readonly #IdleMs = 400;

  readonly #graph: dia.Graph;
  readonly #options: SnappingOptionsContract;
  readonly #layer: GuideLayer;

  #applying = false;
  #idleTimer: ReturnType<typeof setTimeout> | null = null;

  public constructor(graph: dia.Graph, paper: dia.Paper, options: SnappingOptionsContract) {
    this.#graph = graph;
    this.#options = options;
    this.#layer = new GuideLayer(paper);

    if (!options.alignmentGuidesEnabled) return;

    this.#graph.on('change:position', this.#handleMove);
  }

  public destroy(): void {
    this.#graph.off('change:position', this.#handleMove);
    if (this.#idleTimer !== null) clearTimeout(this.#idleTimer);
    this.#layer.destroy();
  }

  readonly #handleMove = (cell: dia.Cell, _position: unknown, options?: MoveOptions): void => {
    // The correction below is itself a position change; without this guard it would re-enter
    // and snap against the peer it just aligned to.
    if (this.#applying || !cell.isElement()) return;

    /*
     * Not when an ancestor is carrying it.
     *
     * JointJS stamps `translateBy` with the id of whatever started a deep translate, so every
     * frame of a Pool's drag reports a position change for each of its contents. Snapping those
     * independently aligns them to their own peers and pulls them out of place relative to the
     * container carrying them — measured on pizza-store, a Start Event crept eight pixels up its
     * Pool over a single drag, a pixel at a time. An element dragged on its own stamps its own id.
     */
    const carriedBy = options?.translateBy;
    if (typeof carriedBy === 'string' && carriedBy !== String(cell.id)) return;

    const element = cell;
    const moving = boxOf(element);
    const peers = this.#peersOf(element);

    const result = snapToPeers(moving, peers, this.#options.tolerance);

    if (result.x !== moving.x || result.y !== moving.y) {
      this.#applying = true;
      try {
        /*
         * Deep, so whatever is embedded comes with it.
         *
         * `position()` on its own moves the element and leaves its children where they were. The
         * alignment correction is a move like any other, so every snapped frame of a drag slid the
         * parent out from under its contents by the snap delta — a Boundary Event crept around its
         * host's outline as `#pinToHost` re-projected it to the nearest point, and a Pool left its
         * tasks behind. Measured on order-process: the offset between a Task and its Boundary Event
         * jumped five pixels on the frame the snapper fired.
         */
        element.position(result.x, result.y, { deep: true });
      } finally {
        this.#applying = false;
      }
    }

    this.#layer.show(result.guides);
    this.#scheduleClear();
  };

  /**
   * Siblings at the same containment level.
   *
   * Aligning to a shape inside a different Pool or Sub-Process would be meaningless — the two are
   * not laid out against each other — and aligning to an ancestor would snap a child to the
   * container it lives in.
   */
  #peersOf(element: dia.Element): Box[] {
    const parentId = element.parent();
    const peers: Box[] = [];

    for (const candidate of this.#graph.getElements()) {
      if (candidate.id !== element.id && candidate.parent() === parentId) peers.push(boxOf(candidate));
    }

    return peers;
  }

  #scheduleClear(): void {
    if (this.#idleTimer !== null) clearTimeout(this.#idleTimer);

    this.#idleTimer = setTimeout(() => {
      this.#idleTimer = null;
      this.#layer.clear();
    }, SnapController.#IdleMs);
  }
}

function boxOf(element: dia.Element): Box {
  const { x, y } = element.position();
  const { width, height } = element.size();
  return { x, y, width, height };
}
