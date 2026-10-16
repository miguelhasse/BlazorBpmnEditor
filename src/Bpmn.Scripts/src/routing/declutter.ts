import type { PointContract } from '../interop/contracts.js';

/**
 * Keeps one connector's segments off the segments of the others.
 *
 * Two flows that run along the same line read as one flow, and the reader then sees a connector
 * that the document does not have. `parallel.ts` separates the connectors between one pair of
 * shapes when they are created; this is the general rule, for any two connectors, and it is cheap
 * enough to run on every frame of a drag.
 *
 * Only a route's *interior* moves, for the reason `parallel.ts` gives: the first and last segments
 * are pinned by the attachment points section 4.6 puts on the outline. A segment is shifted
 * perpendicular to itself, so every segment stays on the axis it was on and the route stays
 * orthogonal. A route with no interior segment (a straight line or a single elbow) is returned
 * unchanged.
 */

/** Points this close together share a coordinate, in paper units. */
const EPSILON = 0.5;

/** How many steps either side a segment is tried at before it is left where it is. */
const MAX_STEPS = 4;

/**
 * The shortest a neighbouring segment may be left after one is moved, in paper units.
 *
 * Shifting a segment lengthens one neighbour and shortens the other. Shortening past zero would
 * flip it, and a flipped segment doubles back over itself, which is a worse overlap than the one
 * being cured.
 */
const MIN_STUB = 4;

/**
 * Shifts the interior segments of a route that lie on top of another connector's.
 *
 * @param route The orthogonal route, ends included.
 * @param others The routes to keep clear of, ends included.
 * @param gap How far to move a clashing segment per step, in paper units. Zero returns the route unchanged.
 * @returns The route with clashing interior segments moved to the nearest clear corridor.
 */
export function declutter(
  route: readonly PointContract[],
  others: readonly (readonly PointContract[])[],
  gap: number,
): readonly PointContract[] {
  if (gap <= 0 || route.length < 4) return route;

  const moved = route.map((point) => ({ ...point }));

  for (let index = 1; index <= moved.length - 3; index += 1) {
    const start = moved[index];
    const end = moved[index + 1];
    if (start === undefined || end === undefined) continue;

    const vertical = Math.abs(start.x - end.x) < EPSILON;
    if (!vertical && Math.abs(start.y - end.y) >= EPSILON) continue;

    // A route must not run over itself either: its own segments, bar the two beside this one.
    const own = moved.slice(0, -1)
      .map((point, at) => [point, moved[at + 1]] as const)
      .filter(([, next], at) => next !== undefined && Math.abs(at - index) > 1)
      .map(([a, b]) => [a, b as PointContract]);
    const avoid = [...others, ...own];

    if (!clashes(start, end, vertical, avoid)) continue;

    for (const offset of candidates(gap)) {
      const at = vertical ? start.x + offset : start.y + offset;
      const from = vertical ? { x: at, y: start.y } : { x: start.x, y: at };
      const to = vertical ? { x: at, y: end.y } : { x: end.x, y: at };

      if (clashes(from, to, vertical, avoid)) continue;
      if (!keepsNeighbours(moved, index, offset, vertical)) continue;

      start.x = from.x;
      start.y = from.y;
      end.x = to.x;
      end.y = to.y;
      break;
    }
  }

  // The first and last segments are pinned to the outline, so they step aside by a jog instead.
  const front = jogFirst(moved, others, gap);
  const both = jogFirst([...front].reverse(), others, gap);

  return [...both].reverse();
}

/**
 * Moves the run that starts a route onto a free line, by a short jog just outside the shape.
 *
 * The end segment cannot shift — its end is on the outline — so the route leaves along it for a
 * few units, steps sideways, and carries on parallel to where it was. Returns the route unchanged
 * if it does not clash or no jog is clear.
 */
function jogFirst(
  route: readonly PointContract[],
  others: readonly (readonly PointContract[])[],
  gap: number,
): readonly PointContract[] {
  const [a, b, c] = route;
  if (a === undefined || b === undefined || c === undefined) return route;

  const vertical = Math.abs(a.x - b.x) < EPSILON;
  if (!vertical && Math.abs(a.y - b.y) >= EPSILON) return route;
  if (!clashes(a, b, vertical, others)) return route;

  const length = vertical ? b.y - a.y : b.x - a.x;
  const direction = Math.sign(length);

  for (const stub of [gap, 2 * gap]) {
    if (Math.abs(length) - stub < MIN_STUB) continue;

    for (const offset of candidates(gap)) {
      const p1 = vertical ? { x: a.x, y: a.y + direction * stub } : { x: a.x + direction * stub, y: a.y };
      const p2 = vertical ? { x: a.x + offset, y: p1.y } : { x: p1.x, y: a.y + offset };
      const b2 = vertical ? { x: b.x + offset, y: b.y } : { x: b.x, y: b.y + offset };

      // The segment after the bend runs along the axis the jog moves on; it must keep its direction.
      const after = vertical ? (c.x - b.x) * (c.x - b2.x) : (c.y - b.y) * (c.y - b2.y);
      if (after <= 0 || (vertical ? Math.abs(c.x - b2.x) : Math.abs(c.y - b2.y)) < MIN_STUB) continue;

      const pieces: [PointContract, PointContract, boolean][] = [
        [a, p1, vertical],
        [p1, p2, !vertical],
        [p2, b2, vertical],
        [b2, c, !vertical],
      ];
      if (pieces.some(([from, to, along]) => clashes(from, to, along, others))) continue;

      return [a, p1, p2, b2, ...route.slice(2)];
    }
  }

  return route;
}

/** +1 step, -1 step, +2, -2 ... so the nearest corridor on either side wins. */
function* candidates(gap: number): Generator<number> {
  for (let step = 1; step <= MAX_STEPS; step += 1) {
    yield step * gap;
    yield -step * gap;
  }
}

/** Whether a segment runs along, and over part of, any segment of the other routes. */
export function clashes(
  from: PointContract,
  to: PointContract,
  vertical: boolean,
  others: readonly (readonly PointContract[])[],
): boolean {
  const low = vertical ? Math.min(from.y, to.y) : Math.min(from.x, to.x);
  const high = vertical ? Math.max(from.y, to.y) : Math.max(from.x, to.x);
  const across = vertical ? from.x : from.y;

  for (const other of others) {
    for (let index = 0; index < other.length - 1; index += 1) {
      const a = other[index];
      const b = other[index + 1];
      if (a === undefined || b === undefined) continue;

      const sameAxis = vertical ? Math.abs(a.x - b.x) < EPSILON : Math.abs(a.y - b.y) < EPSILON;
      if (!sameAxis) continue;

      const otherAcross = vertical ? a.x : a.y;
      if (Math.abs(otherAcross - across) >= EPSILON) continue;

      const otherLow = vertical ? Math.min(a.y, b.y) : Math.min(a.x, b.x);
      const otherHigh = vertical ? Math.max(a.y, b.y) : Math.max(a.x, b.x);

      if (Math.min(high, otherHigh) - Math.max(low, otherLow) > EPSILON) return true;
    }
  }

  return false;
}

/** Whether the segments either side of the moved one keep their direction and a visible length. */
function keepsNeighbours(
  route: readonly PointContract[],
  index: number,
  offset: number,
  vertical: boolean,
): boolean {
  const before = route[index - 1];
  const start = route[index];
  const end = route[index + 1];
  const after = route[index + 2];
  if (before === undefined || start === undefined || end === undefined || after === undefined) return false;

  // The neighbours run along the axis the segment is being moved on.
  const shrinks = (near: PointContract, far: PointContract): boolean => {
    const length = vertical ? near.x - far.x : near.y - far.y;

    return (Math.abs(length) < EPSILON ? 0 : Math.sign(length) * (length + offset)) < MIN_STUB;
  };

  return !shrinks(start, before) && !shrinks(end, after);
}
