import type { Box } from '../snapping/alignment.js';

/**
 * DI-011: peer Flow Nodes may not be left overlapping.
 *
 * Checked here as well as in the semantic editor, and for a different reason. This side owns the
 * live geometry and must refuse a drag *before* it commits, so the user sees the rejection while
 * the pointer is still down; the C# side is the model of record and must refuse a command that
 * arrives from anywhere else. Neither can assume the other looked.
 *
 * Pure so the rule can be stated once and tested without a paper.
 */

/** One candidate in the check: its identity, its container, and where it is. */
export interface CollisionCandidate {
  readonly id: string;
  /** The container that decides who counts as a peer. */
  readonly containerId: string | null;
  readonly box: Box;
  /**
   * Whether this element takes part at all. Boundary Events do not — DI-010 requires one to sit
   * *on* its host's outline — and nor do containers, which legitimately enclose their content.
   */
  readonly participates: boolean;
}

/** The pair that collided. */
export interface Collision {
  readonly id: string;
  readonly peerId: string;
}

/** Positive-area overlap after growing both rectangles by half the clearance each. */
export function overlaps(first: Box, second: Box, clearance: number): boolean {
  const pad = clearance / 2;

  return first.x - pad < second.x + second.width + pad
    && second.x - pad < first.x + first.width + pad
    && first.y - pad < second.y + second.height + pad
    && second.y - pad < first.y + first.height + pad;
}

/**
 * Finds the first collision a set of moving elements would cause, or `null`.
 *
 * @param candidates Every element's effective geometry, moving ones already at their new position.
 * @param movingIds The elements whose position is being changed.
 * @param clearance Extra space required between peers; zero permits edge-to-edge touching.
 */
export function findCollision(
  candidates: readonly CollisionCandidate[],
  movingIds: ReadonlySet<string>,
  clearance: number,
): Collision | null {
  const active = candidates.filter((candidate) => candidate.participates);

  for (const candidate of active) {
    if (!movingIds.has(candidate.id)) continue;

    for (const peer of active) {
      if (peer.id === candidate.id || peer.containerId !== candidate.containerId) continue;

      // Two moving elements are one pair, not two: comparing them twice would report the
      // collision against whichever happened to be visited first.
      if (movingIds.has(peer.id) && peer.id < candidate.id) continue;

      if (overlaps(candidate.box, peer.box, clearance)) {
        return { id: candidate.id, peerId: peer.id };
      }
    }
  }

  return null;
}
