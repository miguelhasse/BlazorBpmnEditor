import type { PointContract } from '../interop/contracts.js';

/**
 * How a connector's polyline is drawn: rounded where it turns, and smooth where it only jogs.
 *
 * Drawing only. The waypoints the document stores are not touched, so what is written back out is
 * what was read in; this decides how the line looks between them.
 *
 * The reason it is not JointJS's own `rounded` connector is the jogs. A diagram drawn in another
 * tool often carries a step of a pixel or two where two runs were meant to be one line, and a
 * corner cannot be rounded on a segment that short: the radius is clamped to half of it, which is
 * a sharp kink. Here a run of such short segments is drawn as one gentle S between the two long
 * runs either side of it, which is what it was meant to look like.
 */

/** A segment this short, in diagram units, is part of a jog rather than a run of its own. */
const SHORT_SEGMENT = 2.5;

/** The most a jog may add up to, in diagram units. Beyond this it is a real step and gets corners. */
const MAX_JOG = 5;

/** Points this close together are the same point. */
const EPSILON = 0.01;

/**
 * The SVG path through a polyline.
 *
 * @param points The route, ends included.
 * @param radius How far from a corner the curve starts, in diagram units.
 * @returns A path string.
 */
export function roundedPath(points: readonly PointContract[], radius: number): string {
  const route = withoutRepeats(points);
  const first = route[0];
  const last = route[route.length - 1];
  if (first === undefined || last === undefined) return '';

  const parts = [`M ${format(first)}`];

  if (radius > 0) {
    let index = 1;

    while (index < route.length - 1) {
      const previous = route[index - 1];
      const current = route[index];
      if (previous === undefined || current === undefined) break;

      const jog = jogAt(route, index);

      if (jog !== null) {
        const reach = Math.min(radius, jog.before / 2, jog.after / 2);
        const from = along(jog.start, jog.backward, reach);
        const to = along(jog.end, jog.forward, reach);

        parts.push(
          `L ${format(from)}`,
          `C ${format(along(from, jog.forward, reach))} ${format(along(to, jog.backward, reach))} ${format(to)}`,
        );
        index = jog.next;
        continue;
      }

      const next = route[index + 1];
      if (next === undefined) break;

      const corner = cornerAt(previous, current, next, radius);
      if (corner !== null) {
        parts.push(`L ${format(corner.start)}`, `C ${format(corner.first)} ${format(corner.second)} ${format(corner.end)}`);
      }

      index += 1;
    }
  }

  parts.push(`L ${format(last)}`);

  return parts.join(' ');
}

interface Jog {
  /** Where the run before the jog ends, and the run after it begins. */
  readonly start: PointContract;
  readonly end: PointContract;

  /** Unit vectors pointing back along the run before and forward along the run after. */
  readonly backward: PointContract;
  readonly forward: PointContract;

  /** The lengths of the runs either side, which bound how far the S may reach. */
  readonly before: number;
  readonly after: number;

  /** The index of the first point after the jog. */
  readonly next: number;
}

/**
 * The jog that starts at a point, if one does.
 *
 * A jog is a run of short segments between two long ones that run the same way, and that move
 * sideways overall: the step between two parallel lines. Anything else — a run that turns the
 * route round, or one with a long segment in it — is a corner, however short.
 */
function jogAt(route: readonly PointContract[], index: number): Jog | null {
  const start = route[index];
  const before = route[index - 1];
  if (start === undefined || before === undefined) return null;
  if (distance(before, start) <= SHORT_SEGMENT) return null;

  let end = index;
  let length = 0;

  while (end + 1 < route.length) {
    const from = route[end];
    const to = route[end + 1];
    if (from === undefined || to === undefined) return null;

    const step = distance(from, to);
    if (step > SHORT_SEGMENT) break;

    length += step;
    end += 1;
  }

  const finish = route[end];
  const after = route[end + 1];
  if (end === index || finish === undefined || after === undefined || length > MAX_JOG) return null;

  const backward = unit(start, before);
  const forward = unit(finish, after);
  if (backward === null || forward === null) return null;

  // The two long runs head the same way, so the jog is a step between parallel lines...
  if ((-backward.x * forward.x) - (backward.y * forward.y) < 0.9) return null;

  // ...and it moves across them rather than along them.
  const across = unit(start, finish);
  if (across === null) return null;
  if (Math.abs((across.x * forward.x) + (across.y * forward.y)) > 0.5) return null;

  return {
    start,
    end: finish,
    backward,
    forward,
    before: distance(before, start),
    after: distance(finish, after),
    next: end + 1,
  };
}

interface Corner {
  readonly start: PointContract;
  readonly first: PointContract;
  readonly second: PointContract;
  readonly end: PointContract;
}

/** A corner rounded with a cubic, or null where the route does not turn. */
function cornerAt(
  previous: PointContract,
  current: PointContract,
  next: PointContract,
  radius: number,
): Corner | null {
  const back = unit(current, previous);
  const forward = unit(current, next);
  if (back === null || forward === null) return null;

  // Straight on: nothing to round.
  if (Math.abs((back.x * forward.y) - (back.y * forward.x)) < 0.02) return null;

  const start = along(current, back, Math.min(radius, distance(previous, current) / 2));
  const end = along(current, forward, Math.min(radius, distance(current, next) / 2));

  return {
    start,
    first: mix(start, current, 2 / 3),
    second: mix(end, current, 2 / 3),
    end,
  };
}

function withoutRepeats(points: readonly PointContract[]): PointContract[] {
  return points.filter((point, index) => {
    const previous = points[index - 1];
    return previous === undefined || distance(point, previous) > EPSILON;
  });
}

function distance(from: PointContract, to: PointContract): number {
  return Math.hypot(to.x - from.x, to.y - from.y);
}

/** The unit vector from one point towards another, or null if they are the same point. */
function unit(from: PointContract, to: PointContract): PointContract | null {
  const length = distance(from, to);

  return length < EPSILON ? null : { x: (to.x - from.x) / length, y: (to.y - from.y) / length };
}

function along(point: PointContract, direction: PointContract, length: number): PointContract {
  return { x: point.x + (direction.x * length), y: point.y + (direction.y * length) };
}

/** A point a fraction of the way from the first to the second. */
function mix(from: PointContract, to: PointContract, fraction: number): PointContract {
  return { x: from.x + ((to.x - from.x) * fraction), y: from.y + ((to.y - from.y) * fraction) };
}

function format(point: PointContract): string {
  return `${String(Math.round(point.x * 100) / 100)} ${String(Math.round(point.y * 100) / 100)}`;
}
