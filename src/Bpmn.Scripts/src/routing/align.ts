import type { PointContract } from '../interop/contracts.js';

/**
 * Holds a routed polyline to the attachment points the editor chose for its ends.
 *
 * Two rules meet here and disagree. The Manhattan router computes its bends from the anchors
 * JointJS picked, which are wherever its own anchor policy puts them; section 4.6 says a connector
 * ends at the pointer's position snapped to the grid and then projected onto the outline. Keeping
 * the router's interior and substituting section 4.6's ends — which is what the editor wants, since
 * only one of those two rules is in the specification — leaves the first and last segments running
 * diagonally from the ends to bends that were placed for somewhere else.
 *
 * So the end bends move onto the ends' axes. Nothing else does: the interior of the route is the
 * router's, and the correction is the smallest one that makes every segment axial again.
 */

/** Points this close together are the same point, in paper units. */
const EPSILON = 0.5;

/**
 * Squares up a route's first and last segments against its endpoints.
 *
 * @param from The source attachment point.
 * @param bends The router's interior bends, in order.
 * @param to The target attachment point.
 * @returns The whole route, every segment axial.
 */
export function alignToEndpoints(
  from: PointContract,
  bends: readonly PointContract[],
  to: PointContract,
): readonly PointContract[] {
  if (bends.length === 0) return straightOrElbow(from, to);

  const aligned = [...bends];
  const first = aligned[0];
  const last = aligned[aligned.length - 1];

  if (first === undefined || last === undefined) return straightOrElbow(from, to);

  if (aligned.length === 1) {
    /*
     * One bend is an elbow, and there are exactly two: over-then-across, or across-then-over. The
     * router's bend says which one it meant, so the nearer corner is kept.
     *
     * Through dropRepeats like every other branch. When the two ends already share an axis the
     * corner lands exactly on one of them, and the duplicate survives into the link's vertices as
     * a zero-length final segment — which is the segment the arrowhead takes its rotation from.
     * Two shapes on one line is the commonest layout in BPMN, so this is not an edge case.
     */
    aligned[0] = nearerCorner(from, to, first);
    return dropRepeats([from, ...aligned, to]);
  }

  const afterFirst = aligned[1];
  const beforeLast = aligned[aligned.length - 2];

  if (afterFirst === undefined || beforeLast === undefined) return straightOrElbow(from, to);

  aligned[0] = onAxisOf(from, first, afterFirst);
  aligned[aligned.length - 1] = onAxisOf(to, last, beforeLast);

  return dropRepeats([from, ...aligned, to]);
}

/**
 * Moves a bend onto the end's axis, keeping the axis it shares with its neighbour.
 *
 * The segment from the bend to its neighbour already runs along one axis and must go on doing so,
 * so the bend may only move along the other — which is exactly the move that makes the segment to
 * the end axial too.
 */
function onAxisOf(end: PointContract, bend: PointContract, neighbour: PointContract): PointContract {
  return Math.abs(neighbour.x - bend.x) < EPSILON
    ? { x: bend.x, y: end.y }
    : { x: end.x, y: bend.y };
}

/** Which of the two elbows between the ends the router's bend was closer to. */
function nearerCorner(from: PointContract, to: PointContract, bend: PointContract): PointContract {
  const across: PointContract = { x: to.x, y: from.y };
  const over: PointContract = { x: from.x, y: to.y };

  return distance(bend, across) <= distance(bend, over) ? across : over;
}

/**
 * A route with no bends at all.
 *
 * Straight when the ends already line up, and an elbow when they do not — because a diagonal line
 * is the one thing an orthogonal route may not contain, and two points that share neither axis have
 * no straight orthogonal path between them.
 */
function straightOrElbow(from: PointContract, to: PointContract): readonly PointContract[] {
  const axial = Math.abs(from.x - to.x) < EPSILON || Math.abs(from.y - to.y) < EPSILON;

  return axial ? [from, to] : [from, { x: to.x, y: from.y }, to];
}

/**
 * Consecutive duplicates, which an alignment can produce when a bend lands on its neighbour, and
 * the doubling back that follows from them: a bend that sends the route out along a line and then
 * straight back over itself is a segment drawn twice, so it is removed.
 */
export function dropRepeats(route: readonly PointContract[]): readonly PointContract[] {
  let result = route.filter((point, index) => {
    const previous = route[index - 1];
    return previous === undefined || distance(point, previous) > EPSILON;
  });

  for (let changed = true; changed;) {
    changed = false;

    for (let index = 1; index < result.length - 1; index += 1) {
      const before = result[index - 1];
      const here = result[index];
      const after = result[index + 1];
      if (before === undefined || here === undefined || after === undefined) continue;

      const alongX = Math.abs(before.y - here.y) < EPSILON && Math.abs(here.y - after.y) < EPSILON;
      const alongY = Math.abs(before.x - here.x) < EPSILON && Math.abs(here.x - after.x) < EPSILON;
      if (!alongX && !alongY) continue;

      // Straight on is a needless bend; back on itself is an overlap. Neither is kept.
      result = [...result.slice(0, index), ...result.slice(index + 1)];
      changed = true;
      break;
    }
  }

  return result;
}

function distance(first: PointContract, second: PointContract): number {
  return Math.hypot(first.x - second.x, first.y - second.y);
}
