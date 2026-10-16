import type { PointContract } from '../interop/contracts.js';
import { dropRepeats } from './align.js';
import { clashes } from './declutter.js';

/**
 * Sliding one run of a right-angled connector sideways.
 *
 * What a user does by taking hold of the line anywhere and dragging it. The run moves perpendicular
 * to itself, so every other segment stays on the axis it was on and the route stays right-angled.
 *
 * The first and last runs cannot simply move: their ends are on the shapes' outlines (section
 * 4.6). They leave along the run for a short stub, step sideways by the offset, and carry on beside
 * where they were — the same stub-and-jog `declutter` uses to step a connector off another.
 *
 * Nothing here knows about JointJS or the DOM; the routes are plain points, so every rule below is
 * a unit test rather than a drag.
 */

/** Points this close together share a coordinate, in paper units. */
const EPSILON = 0.5;

/** A box a route must not run through, in paper units. */
export interface SlideBox {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** What a slide has to keep clear of. */
export interface SlideEnvironment {
  /** Every other connector's route, ends included. */
  readonly others: readonly (readonly PointContract[])[];

  /** The shapes at the two ends, which the route must not cross. */
  readonly boxes: readonly SlideBox[];

  /** How far an end run leaves along itself before it steps sideways. */
  readonly stub: number;
}

/** The index of the segment of a route that lies nearest a point, or null for a route with none. */
export function nearestSegment(route: readonly PointContract[], point: PointContract): number | null {
  let best: number | null = null;
  let bestDistance = Number.POSITIVE_INFINITY;

  for (let index = 0; index < route.length - 1; index += 1) {
    const from = route[index];
    const to = route[index + 1];
    if (from === undefined || to === undefined || !axial(from, to)) continue;

    const distance = distanceToSegment(point, from, to);

    if (distance < bestDistance) {
      best = index;
      bestDistance = distance;
    }
  }

  return best;
}

/** Whether a segment runs up and down, as opposed to across. */
export function isVertical(from: PointContract, to: PointContract): boolean {
  return Math.abs(from.x - to.x) < EPSILON;
}

/**
 * The route with one run moved sideways by an offset, whether or not that is a good idea.
 *
 * @param route The right-angled route, ends included.
 * @param index Which segment: the one from `route[index]` to `route[index + 1]`.
 * @param offset How far to move it, signed, along the axis perpendicular to it.
 * @param stub How far an end run leaves along itself before stepping sideways.
 * @returns The new route. An offset of zero returns the route unchanged.
 */
export function slideSegment(
  route: readonly PointContract[],
  index: number,
  offset: number,
  stub: number,
): readonly PointContract[] {
  const from = route[index];
  const to = route[index + 1];
  if (from === undefined || to === undefined || !axial(from, to) || Math.abs(offset) < EPSILON) return route;

  const vertical = isVertical(from, to);
  const length = Math.hypot(to.x - from.x, to.y - from.y);
  if (length < EPSILON) return route;

  const direction = { x: (to.x - from.x) / length, y: (to.y - from.y) / length };
  const leave = Math.max(0, Math.min(stub, length / 2));
  const shift = (point: PointContract): PointContract => (vertical
    ? { x: point.x + offset, y: point.y }
    : { x: point.x, y: point.y + offset });

  const last = route.length - 1;
  const moved: PointContract[] = [];

  route.forEach((point, position) => {
    if (position === index) {
      if (index === 0) {
        const stubbed = { x: point.x + (direction.x * leave), y: point.y + (direction.y * leave) };
        moved.push(point, stubbed, shift(stubbed));
      } else {
        moved.push(shift(point));
      }
    } else if (position === index + 1) {
      if (position === last) {
        const stubbed = { x: point.x - (direction.x * leave), y: point.y - (direction.y * leave) };
        moved.push(shift(stubbed), stubbed, point);
      } else {
        moved.push(shift(point));
      }
    } else {
      moved.push(point);
    }
  });

  return dropRepeats(moved);
}

/**
 * Whether a route is fit to be drawn: it does not run over itself, and what it has added does not
 * run over another connector or through its own shapes.
 *
 * Only segments the slide *made* are held to the last two. A route that was imported lying on
 * another connector, or grazing its own shape, has to be able to be slid away from it, not be
 * refused for what it already was.
 */
export function isLegal(
  before: readonly PointContract[],
  after: readonly PointContract[],
  environment: SlideEnvironment,
): boolean {
  for (let first = 0; first < after.length - 1; first += 1) {
    for (let second = first + 2; second < after.length - 1; second += 1) {
      if (overlaps(after[first], after[first + 1], after[second], after[second + 1])) return false;
    }
  }

  for (let index = 0; index < after.length - 1; index += 1) {
    const from = after[index];
    const to = after[index + 1];
    if (from === undefined || to === undefined || existed(before, from, to)) continue;

    if (clashes(from, to, isVertical(from, to), environment.others) && !alreadyClashed(before, from, to, environment)) return false;
    if (environment.boxes.some((box) => passesThrough(from, to, box))) return false;
  }

  return true;
}

/**
 * The slid route nearest the one asked for that is legal.
 *
 * Walks from the requested offset back towards zero, so a drag that would run into something stops
 * against it instead of jumping or refusing. Offsets that merely cross over another line are fine;
 * it is lying along one that is not.
 *
 * @param route The right-angled route, ends included.
 * @param index Which segment is being slid.
 * @param offset The offset the pointer asks for.
 * @param environment What the slide has to keep clear of.
 * @param step How finely to search, in paper units.
 * @returns The legal route, which is the original one if nothing but zero is legal.
 */
export function slideWithin(
  route: readonly PointContract[],
  index: number,
  offset: number,
  environment: SlideEnvironment,
  step = 1,
): readonly PointContract[] {
  const sign = Math.sign(offset);

  for (let distance = Math.abs(offset); distance >= step; distance -= step) {
    const candidate = slideSegment(route, index, sign * distance, environment.stub);

    if (isLegal(route, candidate, environment)) return candidate;
  }

  return route;
}

function axial(from: PointContract, to: PointContract): boolean {
  return Math.abs(from.x - to.x) < EPSILON || Math.abs(from.y - to.y) < EPSILON;
}

function distanceToSegment(point: PointContract, from: PointContract, to: PointContract): number {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const lengthSquared = (dx * dx) + (dy * dy);
  const along = lengthSquared === 0
    ? 0
    : Math.max(0, Math.min(1, (((point.x - from.x) * dx) + ((point.y - from.y) * dy)) / lengthSquared));

  return Math.hypot(point.x - (from.x + (along * dx)), point.y - (from.y + (along * dy)));
}

/**
 * Whether the line a segment lies on was already lying on another connector before the slide.
 *
 * A segment a slide merely lengthens is not a new clash because it is a new segment: the route was
 * on that line, and on that connector, before it was touched.
 */
function alreadyClashed(
  before: readonly PointContract[],
  from: PointContract,
  to: PointContract,
  environment: SlideEnvironment,
): boolean {
  const vertical = isVertical(from, to);

  return before.slice(1).some((point, index) => {
    const previous = before[index];
    if (previous === undefined || isVertical(previous, point) !== vertical) return false;

    const sameLine = vertical ? Math.abs(previous.x - from.x) < EPSILON : Math.abs(previous.y - from.y) < EPSILON;

    return sameLine && clashes(previous, point, vertical, environment.others);
  });
}

/** Whether a segment is one the route already had. */
function existed(route: readonly PointContract[], from: PointContract, to: PointContract): boolean {
  return route.slice(1).some((point, index) => {
    const previous = route[index];
    return previous !== undefined
      && ((near(previous, from) && near(point, to)) || (near(previous, to) && near(point, from)));
  });
}

function near(first: PointContract, second: PointContract): boolean {
  return Math.abs(first.x - second.x) < EPSILON && Math.abs(first.y - second.y) < EPSILON;
}

/** Whether two segments lie along the same line and share some of its length. */
function overlaps(
  firstFrom: PointContract | undefined,
  firstTo: PointContract | undefined,
  secondFrom: PointContract | undefined,
  secondTo: PointContract | undefined,
): boolean {
  if (firstFrom === undefined || firstTo === undefined || secondFrom === undefined || secondTo === undefined) return false;

  return clashes(firstFrom, firstTo, isVertical(firstFrom, firstTo), [[secondFrom, secondTo]]);
}

/** Whether a right-angled segment runs through the inside of a box. */
function passesThrough(from: PointContract, to: PointContract, box: SlideBox): boolean {
  const inset = 2;
  const left = box.x + inset;
  const right = box.x + box.width - inset;
  const top = box.y + inset;
  const bottom = box.y + box.height - inset;

  const minX = Math.min(from.x, to.x);
  const maxX = Math.max(from.x, to.x);
  const minY = Math.min(from.y, to.y);
  const maxY = Math.max(from.y, to.y);

  return maxX > left && minX < right && maxY > top && minY < bottom
    && (isVertical(from, to) ? from.x > left && from.x < right : from.y > top && from.y < bottom);
}
