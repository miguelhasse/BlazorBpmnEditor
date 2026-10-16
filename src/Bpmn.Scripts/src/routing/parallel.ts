import type { dia } from '@joint/core';

import { findElement } from '../graph/cells.js';
import type { PointContract } from '../interop/contracts.js';

/**
 * Pulls apart connectors that run between the same two shapes.
 *
 * Two Sequence Flows from one Gateway to one Task are legal and not unusual — a loop back and a
 * forward branch, or two conditions that happen to share a target — and routed identically they
 * draw as one line. The reader then sees one connector where the document has two.
 *
 * The separation moves the route's *interior*, not its endpoints. Sliding the attachments along
 * their outlines was tried first and cannot work: section 4.6 requires an attachment to sit on the
 * outline, so a nudged endpoint has to be projected back onto it, and the projection undoes most of
 * the nudge — the routes still overlapped. The corridor between the two attachments is the only
 * part with room to move.
 *
 * Deterministic: the offset follows the index of the connector among its peers, so the same
 * document always produces the same picture, which is what makes the visual regression suite
 * possible at all.
 */

/**
 * How many peers a pair is spread over before the offsets repeat.
 *
 * Beyond this the corridors coincide, so the seventh connector between one pair of shapes draws on
 * top of the sixth. That is a deliberate ceiling rather than a limit of the mechanism: the spread
 * is symmetric about the direct route, and widening it without bound walks the later connectors
 * across unrelated shapes, which is a worse picture than two lines sharing a corridor.
 */
const MAX_SPREAD = 6;

/**
 * Moves a route's interior clear of the ones already between the same pair.
 *
 * An orthogonal route between two fixed attachment points has exactly one degree of freedom: the
 * corridor its interior runs along. The endpoints cannot move — section 4.6 puts them on the
 * outline and the router squares the route up against them — so shifting the corridor is the
 * separation that is actually available, and it is what the implementation plan asks for when it
 * says "route offsets".
 *
 * Only the segments that touch neither endpoint move. Each is shifted perpendicular to itself,
 * which leaves every segment on the axis it was already on: a bend that belongs to two shifted
 * segments is moved once in x and once in y, and a bend that also touches a fixed end segment
 * moves only along the axis that end segment does not depend on.
 *
 * A route with no interior — two attachment points and a straight line between them — has nothing
 * to offset and is returned unchanged. Two such connectors do overlap; see docs/routing.md.
 *
 * @param route The aligned orthogonal route.
 * @param peers How many connectors already run between this ordered pair.
 * @param separation How far apart to pull them, in paper units. Zero returns the route unchanged.
 * @returns The route with its corridor shifted.
 */
export function separate(
  route: readonly PointContract[],
  peers: number,
  separation: number,
): readonly PointContract[] {
  if (peers <= 0 || separation <= 0 || route.length < 4) return route;

  const offset = offsetFor(peers, separation);
  const moved = route.map((point) => ({ ...point }));

  // Segments (1,2) through (n-3, n-2): everything that does not touch an endpoint.
  for (let index = 1; index <= route.length - 3; index += 1) {
    const from = route[index];
    const to = route[index + 1];
    const start = moved[index];
    const end = moved[index + 1];

    if (from === undefined || to === undefined || start === undefined || end === undefined) continue;

    if (Math.abs(from.x - to.x) < EPSILON) {
      start.x += offset;
      end.x += offset;
    } else {
      start.y += offset;
      end.y += offset;
    }
  }

  return moved;
}

/** Points this close together share a coordinate, in paper units. */
const EPSILON = 0.5;

/**
 * How far the nth connector between a pair is pushed.
 *
 * Alternating sides, growing outward: the second connector goes one step one way, the third one
 * step the other, the fourth two steps. So adding a connector never moves the ones already drawn,
 * which matters because moving them would mean rewriting their stored waypoints.
 *
 * @param peers How many already run between the pair.
 * @param separation The step size.
 * @returns A signed offset along the perpendicular.
 */
export function offsetFor(peers: number, separation: number): number {
  const index = Math.min(peers, MAX_SPREAD);
  const step = Math.ceil(index / 2);

  return index % 2 === 1 ? step * separation : -step * separation;
}

/** How many links already run between this ordered pair, ignoring scaffolding. */
export function peersBetween(
  graph: dia.Graph,
  sourceId: string,
  targetId: string,
  isScaffolding: (link: dia.Link) => boolean,
): number {
  const origin = findElement(graph, sourceId);
  const candidates = origin === undefined ? graph.getLinks() : graph.getConnectedLinks(origin);
  let count = 0;

  for (const link of candidates) {
    if (isScaffolding(link)) continue;

    const source = link.source().id;
    const target = link.target().id;

    // Undirected for this purpose: a pair drawn one way and the other still overlaps.
    if ((String(source) === sourceId && String(target) === targetId)
      || (String(source) === targetId && String(target) === sourceId)) count += 1;
  }

  return count;
}
