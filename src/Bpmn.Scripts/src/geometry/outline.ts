import type { BoundsContract, PointContract } from '../model/contracts.js';
import { MAGNET_SNAP_RADIUS, TASK_CORNER_RADIUS } from './constants.js';

/**
 * Shape outlines and nearest-point projection (specification section 4.6).
 *
 * BPMN shapes expose a continuous outline, never a finite port set: a connector may
 * attach anywhere on the boundary. This module owns that geometry as pure functions so
 * the same code serves static edge endpoints now and the interactive attachment gesture
 * in M4, including the grid-snap-then-project ordering section 4.6 mandates.
 */
export type Outline =
  | { readonly kind: 'rect'; readonly bounds: BoundsContract }
  | { readonly kind: 'roundedRect'; readonly bounds: BoundsContract; readonly radius: number }
  | { readonly kind: 'ellipse'; readonly bounds: BoundsContract }
  | { readonly kind: 'diamond'; readonly bounds: BoundsContract };

export function centerOf(bounds: BoundsContract): PointContract {
  return { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
}

/**
 * Projects a point onto the nearest position on the outline.
 *
 * Section 4.6 requires the committed endpoint to lie exactly on the boundary even when
 * the pointer was snapped to a grid that the boundary never crosses, so callers snap
 * first and project second — never the reverse.
 */
export function projectToOutline(point: PointContract, outline: Outline): PointContract {
  switch (outline.kind) {
    case 'rect':
      return projectToRect(point, outline.bounds);
    case 'roundedRect':
      return projectToRoundedRect(point, outline.bounds, outline.radius);
    case 'ellipse':
      return projectToEllipse(point, outline.bounds);
    case 'diamond':
      return projectToPolygon(point, diamondVertices(outline.bounds));
  }
}

/** The point where a ray from the shape centre towards a target leaves the outline. */
export function outlineIntersection(outline: Outline, target: PointContract): PointContract {
  const center = centerOf(bounds(outline));
  if (center.x === target.x && center.y === target.y) return center;

  // Walking the ray and projecting its far end keeps one implementation for every
  // outline kind rather than four bespoke intersection formulas.
  const dx = target.x - center.x;
  const dy = target.y - center.y;
  const length = Math.hypot(dx, dy);
  const reach = Math.max(bounds(outline).width, bounds(outline).height);
  const far = { x: center.x + (dx / length) * reach, y: center.y + (dy / length) * reach };
  return projectToOutline(far, outline);
}

export function bounds(outline: Outline): BoundsContract {
  return outline.bounds;
}

/** Snaps a point to the configured grid, in paper coordinates (section 4.6). */
export function snapToGrid(point: PointContract, gridSize: number): PointContract {
  if (gridSize <= 0) return point;
  return {
    x: Math.round(point.x / gridSize) * gridSize,
    y: Math.round(point.y / gridSize) * gridSize,
  };
}

/**
 * The virtual points on an outline that pull a nearby connector endpoint onto them.
 *
 * Compass points only: circles have north, east, south and west, a diamond its four vertices (the
 * same four points) and an activity the midpoint of each side. Anything else has none, and a
 * connector attaches wherever it is put. They are never drawn — a magnet is a preference for a
 * spot, not a port, so every other point of the outline stays reachable (section 4.6).
 */
export function magnetsOf(outline: Outline): readonly PointContract[] {
  if (outline.kind === 'rect') return [];

  const { x, y, width, height } = outline.bounds;
  const centre = centerOf(outline.bounds);

  return [
    { x: centre.x, y },
    { x: x + width, y: centre.y },
    { x: centre.x, y: y + height },
    { x, y: centre.y },
  ];
}

/**
 * Pulls a point on an outline onto the nearest magnet when it is within `radius` of one.
 *
 * Applied after the projection, never before, so the result is always on the outline: a magnet is
 * itself an outline point, and a point that is not near one is returned untouched.
 */
export function snapToMagnet(
  point: PointContract,
  outline: Outline,
  radius: number = MAGNET_SNAP_RADIUS,
): PointContract {
  let nearest: PointContract | undefined;
  let nearestDistance = radius;

  for (const magnet of magnetsOf(outline)) {
    const distance = Math.hypot(magnet.x - point.x, magnet.y - point.y);
    if (distance <= nearestDistance) {
      nearest = magnet;
      nearestDistance = distance;
    }
  }

  return nearest ?? point;
}

/**
 * Where a connector's end belongs on a shape, given the waypoint a document stored for it.
 *
 * Projected onto the outline, so an end is always on the boundary whatever the file said. A waypoint
 * farther from the shape than the shape is large cannot be an attachment to it (a file that declares
 * every waypoint at the origin is the real case), and projecting it would pile every connector onto
 * one corner, so such an end leaves the outline on the line towards the other end instead.
 *
 * The C# side implements the same rule (`ConnectorEndpoints`), and both are held to one table of
 * cases, so a document that was normalised on import is drawn where it is stored.
 *
 * @param waypoint The stored end.
 * @param outline The outline of the shape it belongs to.
 * @param other The centre of the shape at the other end of the connector.
 */
export function normalizedEndpoint(
  waypoint: PointContract,
  outline: Outline,
  other: PointContract,
): PointContract {
  const box = outline.bounds;
  const reach = Math.max(box.width, box.height);

  return distanceToBounds(waypoint, box) <= reach
    ? projectToOutline(waypoint, outline)
    : outlineIntersection(outline, other);
}

/** The distance from a point to a rectangle, zero inside it. */
export function distanceToBounds(point: PointContract, box: BoundsContract): number {
  const dx = Math.max(box.x - point.x, 0, point.x - (box.x + box.width));
  const dy = Math.max(box.y - point.y, 0, point.y - (box.y + box.height));

  return Math.hypot(dx, dy);
}

/**
 * The nearest point of a rounded rectangle's outline.
 *
 * The straight sides are the rectangle's; within a corner the nearest point is on the arc, along the
 * line from the arc's centre through the point. Exact rather than the rectangle's, because an
 * endpoint that is dragged around a corner has to follow the corner it is drawn with.
 */
function projectToRoundedRect(point: PointContract, box: BoundsContract, radius: number): PointContract {
  const candidate = projectToRect(point, box);
  const r = Math.min(radius, box.width / 2, box.height / 2);
  if (r <= 0) return candidate;

  const left = box.x + r;
  const right = box.x + box.width - r;
  const top = box.y + r;
  const bottom = box.y + box.height - r;

  // The corner square the rectangle's nearest point falls in, if any.
  const inLeft = candidate.x < left;
  const inRight = candidate.x > right;
  const inTop = candidate.y < top;
  const inBottom = candidate.y > bottom;
  if (!(inLeft || inRight) || !(inTop || inBottom)) return candidate;

  const centre = { x: inLeft ? left : right, y: inTop ? top : bottom };
  const dx = point.x - centre.x;
  const dy = point.y - centre.y;

  // Only in the arc's own quadrant; outside it the straight side is nearer.
  const outward = (inLeft ? dx <= 0 : dx >= 0) && (inTop ? dy <= 0 : dy >= 0);
  if (!outward) return candidate;

  const length = Math.hypot(dx, dy);
  if (length === 0) return { x: centre.x + (inLeft ? -r : r) * Math.SQRT1_2, y: centre.y + (inTop ? -r : r) * Math.SQRT1_2 };

  return { x: centre.x + (dx / length) * r, y: centre.y + (dy / length) * r };
}

function projectToRect(point: PointContract, box: BoundsContract): PointContract {
  const left = box.x;
  const right = box.x + box.width;
  const top = box.y;
  const bottom = box.y + box.height;

  const clampedX = Math.min(right, Math.max(left, point.x));
  const clampedY = Math.min(bottom, Math.max(top, point.y));

  const inside = point.x > left && point.x < right && point.y > top && point.y < bottom;
  if (!inside) return { x: clampedX, y: clampedY };

  // Inside the box the nearest boundary point lies on whichever edge is closest.
  const distances = [
    { edge: { x: left, y: clampedY }, distance: point.x - left },
    { edge: { x: right, y: clampedY }, distance: right - point.x },
    { edge: { x: clampedX, y: top }, distance: point.y - top },
    { edge: { x: clampedX, y: bottom }, distance: bottom - point.y },
  ];

  let nearest = distances[0];
  for (const candidate of distances) {
    if (nearest === undefined || candidate.distance < nearest.distance) nearest = candidate;
  }
  return nearest === undefined ? { x: clampedX, y: clampedY } : nearest.edge;
}

function projectToEllipse(point: PointContract, box: BoundsContract): PointContract {
  const center = centerOf(box);
  const radiusX = box.width / 2;
  const radiusY = box.height / 2;
  if (radiusX === 0 || radiusY === 0) return center;

  const dx = point.x - center.x;
  const dy = point.y - center.y;
  if (dx === 0 && dy === 0) return { x: center.x + radiusX, y: center.y };

  // Scale the offset so it lands on the unit circle in normalised space, then unscale.
  const normalised = Math.hypot(dx / radiusX, dy / radiusY);
  return { x: center.x + dx / normalised, y: center.y + dy / normalised };
}

function diamondVertices(box: BoundsContract): readonly PointContract[] {
  const center = centerOf(box);
  return [
    { x: center.x, y: box.y },
    { x: box.x + box.width, y: center.y },
    { x: center.x, y: box.y + box.height },
    { x: box.x, y: center.y },
  ];
}

function projectToPolygon(point: PointContract, vertices: readonly PointContract[]): PointContract {
  let best: PointContract | undefined;
  let bestDistance = Number.POSITIVE_INFINITY;

  for (let index = 0; index < vertices.length; index += 1) {
    const from = vertices[index];
    const to = vertices[(index + 1) % vertices.length];
    if (from === undefined || to === undefined) continue;

    const candidate = projectToSegment(point, from, to);
    const distance = Math.hypot(candidate.x - point.x, candidate.y - point.y);
    if (distance < bestDistance) {
      bestDistance = distance;
      best = candidate;
    }
  }

  return best ?? point;
}

function projectToSegment(point: PointContract, from: PointContract, to: PointContract): PointContract {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const lengthSquared = dx * dx + dy * dy;
  if (lengthSquared === 0) return from;

  const t = Math.min(1, Math.max(0, ((point.x - from.x) * dx + (point.y - from.y) * dy) / lengthSquared));
  return { x: from.x + t * dx, y: from.y + t * dy };
}

/**
 * The outline a BPMN type is drawn with.
 *
 * Keyed by the wire name the shape factories store on the cell, so the geometry used to place a
 * connector endpoint is derived from the same fact as the shape that was drawn. A type nobody has
 * taught this function about falls back to a rectangle: a slightly wrong endpoint on an unfamiliar
 * shape beats no connector at all.
 */
export function outlineOf(elementType: string, box: BoundsContract): Outline {
  switch (elementType) {
    case 'startEvent':
    case 'endEvent':
    case 'intermediateCatchEvent':
    case 'intermediateThrowEvent':
    case 'boundaryEvent':
      return { kind: 'ellipse', bounds: box };

    case 'exclusiveGateway':
    case 'inclusiveGateway':
    case 'parallelGateway':
    case 'complexGateway':
    case 'eventBasedGateway':
      return { kind: 'diamond', bounds: box };

    case 'pool':
    case 'lane':
    case 'dataObject':
    case 'dataObjectReference':
    case 'dataStoreReference':
    case 'textAnnotation':
    case 'group':
      return { kind: 'rect', bounds: box };

    default:
      return { kind: 'roundedRect', bounds: box, radius: TASK_CORNER_RADIUS };
  }
}
