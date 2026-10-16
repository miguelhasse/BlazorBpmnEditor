import { routers, type dia, type g } from '@joint/core';

import { isProvisional } from '../editing/provisional.js';
import type { PointContract } from '../interop/contracts.js';
import { alignToEndpoints } from './align.js';
import { declutter } from './declutter.js';

/**
 * The Manhattan router, followed by keeping clear of the other connectors.
 *
 * It has to be a router rather than a pass after one, because JointJS re-runs a link's router on
 * every frame of a drag and nothing else: a correction applied afterwards would show for one frame
 * and be gone on the next.
 *
 * What it avoids is whatever the other connectors are drawn as right now. A connector that is
 * itself being routed live is only avoided when it is earlier in the graph, so that two connectors
 * moving at once do not each step aside for the other's previous frame and swap places forever.
 */

/** The least a loop stays off its shape, in paper units, whatever the padding is configured to. */
const MIN_CLEARANCE = 10;

/** The router's name in `routerNamespace`. */
export const ORTHOGONAL_ROUTER = 'bpmnOrthogonal';

/** What the router is told beyond the Manhattan arguments. */
export interface OrthogonalArguments {
  /** How far to step aside per attempt, in paper units. */
  readonly separation?: number;

  /** How far a flow that returns to its own shape stays off the outline, in paper units. */
  readonly clearance?: number;
}

/**
 * The box of the shape at one end of a flow, or undefined for a loose end.
 *
 * The element's own box, not the view's: JointJS measures the whole view, which takes in the
 * external label under a Gateway or an Event, and an attachment on the outline is then nowhere
 * near a face of that box.
 */
function boxOf(view: dia.LinkView, end: 'source' | 'target'): g.Rect | undefined {
  const id = view.model[end]().id;
  const cell = id === undefined ? undefined : (view.model.graph as dia.Graph | null)?.getCell(id);

  return cell?.isElement() === true ? cell.getBBox() : undefined;
}

/**
 * The Manhattan router, told how much room there really is.
 *
 * Its padding inflates both end shapes, and when the shapes are closer than two paddings the
 * inflated boxes meet and the direct route counts as blocked. The router then leaves by the far
 * side and doubles back through the very shape it left, which is a connector overlapping itself —
 * and it is what a shape being dragged towards its neighbour produces on the way. So the padding
 * never exceeds half the gap.
 */
function manhattan(vertices: g.Point[], args: OrthogonalArguments & { padding?: number }, view: dia.LinkView, exact = false): g.PlainPoint[] {
  const source = boxOf(view, 'source');
  const target = boxOf(view, 'target');
  const gap = source === undefined || target === undefined
    ? Infinity
    : Math.max(
      Math.max(source.x - (target.x + target.width), target.x - (source.x + source.width)),
      Math.max(source.y - (target.y + target.height), target.y - (source.y + source.height)),
    );
  const padding = exact ? (args.padding ?? 0) : Math.max(0, Math.min(args.padding ?? 0, Math.floor(gap / 2) - 1));

  // An end leaves by the face it is on. The configured directions follow the Pool's axis, which is
  // right for an end on a side face and wrong for one on the top or bottom: leaving "sideways" from
  // there runs along the shape's own outline or through its body.
  const start = facesOf(view.sourceAnchor, source);
  const end = facesOf(view.targetAnchor, target);

  return (routers.manhattan as unknown as (
    vertices: g.Point[],
    args: object,
    view: dia.LinkView,
  ) => g.PlainPoint[])(vertices, {
    ...args,
    padding,
    ...(start === null ? {} : { startDirections: start }),
    ...(end === null ? {} : { endDirections: end }),
  }, view);
}

/**
 * The faces of a box that a point on its outline is on, or null where that cannot be told.
 *
 * Decided by which way the point lies from the centre, scaled by the box, so a Gateway's or an
 * Event's outline — which only touches the box at four points — still has a face. Near a corner
 * both neighbouring faces are allowed.
 */
function facesOf(anchor: g.PlainPoint | undefined, box: g.Rect | undefined): dia.OrthogonalDirection[] | null {
  if (anchor === undefined || box === undefined || box.width <= 0 || box.height <= 0) return null;

  const dx = (anchor.x - (box.x + (box.width / 2))) / (box.width / 2);
  const dy = (anchor.y - (box.y + (box.height / 2))) / (box.height / 2);
  if (Math.max(Math.abs(dx), Math.abs(dy)) < 0.5) return null;

  const faces: dia.OrthogonalDirection[] = [];
  const corner = 0.15;

  if (Math.abs(dx) >= Math.abs(dy) - corner) faces.push(dx < 0 ? 'left' : 'right');
  if (Math.abs(dy) >= Math.abs(dx) - corner) faces.push(dy < 0 ? 'top' : 'bottom');

  return faces;
}

/** Whether any segment of the route runs through the inside of a box. */
export function crosses(
  route: readonly g.PlainPoint[],
  box: Pick<g.Rect, 'x' | 'y' | 'width' | 'height'> | undefined,
): boolean {
  if (box === undefined) return false;

  const inset = 2;
  const left = box.x + inset;
  const right = box.x + box.width - inset;
  const top = box.y + inset;
  const bottom = box.y + box.height - inset;

  for (let index = 1; index < route.length; index += 1) {
    const from = route[index - 1];
    const to = route[index];
    if (from === undefined || to === undefined) continue;

    const x0 = Math.min(from.x, to.x);
    const x1 = Math.max(from.x, to.x);
    const y0 = Math.min(from.y, to.y);
    const y1 = Math.max(from.y, to.y);

    if (Math.min(x1, right) - Math.max(x0, left) > 0 && Math.min(y1, bottom) - Math.max(y0, top) > 0
      || (x0 === x1 && x0 > left && x0 < right && y1 > top && y0 < bottom)
      || (y0 === y1 && y0 > top && y0 < bottom && x1 > left && x0 < right)) return true;
  }

  return false;
}

/**
 * The route of a flow whose two ends are on the same shape.
 *
 * The router excludes both ends from its obstacles, and here they are one box, so it happily
 * crosses it. The loop is drawn around the outside instead: out of one face, round, and into the
 * other, `clearance` off the outline.
 */
function loopBends(
  from: g.PlainPoint,
  to: g.PlainPoint,
  box: g.Rect,
  clearance: number,
): g.PlainPoint[] | null {
  const first = facesOf(from, box)?.[0];
  const second = facesOf(to, box)?.[0];
  if (first === undefined || second === undefined) return null;

  const out = (face: dia.OrthogonalDirection, at: g.PlainPoint): g.PlainPoint => ({
    x: at.x + (face === 'left' ? -clearance : face === 'right' ? clearance : 0),
    y: at.y + (face === 'top' ? -clearance : face === 'bottom' ? clearance : 0),
  });
  const horizontal = (face: dia.OrthogonalDirection): boolean => face === 'left' || face === 'right';

  const a = out(first, from);
  const b = out(second, to);

  if (first === second) {
    // One face: a bracket, both legs running out to the same line.
    return horizontal(first)
      ? [a, { x: first === 'right' ? Math.max(a.x, b.x) : Math.min(a.x, b.x), y: a.y }, { x: first === 'right' ? Math.max(a.x, b.x) : Math.min(a.x, b.x), y: b.y }, b]
      : [a, { x: a.x, y: first === 'bottom' ? Math.max(a.y, b.y) : Math.min(a.y, b.y) }, { x: b.x, y: first === 'bottom' ? Math.max(a.y, b.y) : Math.min(a.y, b.y) }, b];
  }

  if (horizontal(first) !== horizontal(second)) {
    // Adjacent faces: one corner outside the box joins the two legs.
    return [a, horizontal(first) ? { x: a.x, y: b.y } : { x: b.x, y: a.y }, b];
  }

  // Opposite faces: over the top (or down the side) of the shape.
  return horizontal(first)
    ? [a, { x: a.x, y: box.y - clearance }, { x: b.x, y: box.y - clearance }, b]
    : [a, { x: box.x - clearance, y: a.y }, { x: box.x - clearance, y: b.y }, b];
}

/**
 * The route from a shape to a loose end, which is the pointer while an end is being dragged.
 *
 * One elbow: out along the face's normal to the pointer's level, then across. Left to the router,
 * a point with no box to approach gets a hook — out, past the pointer, and back in. Null when the
 * pointer is behind the face, where the router's detour is the right answer.
 */
function elbowToLoose(from: g.PlainPoint, face: dia.OrthogonalDirection, loose: g.PlainPoint): g.PlainPoint[] | null {
  const vertical = face === 'top' || face === 'bottom';
  const ahead = face === 'right' ? loose.x - from.x : face === 'left' ? from.x - loose.x
    : face === 'bottom' ? loose.y - from.y : from.y - loose.y;
  if (ahead <= 0) return null;

  const corner = vertical ? { x: from.x, y: loose.y } : { x: loose.x, y: from.y };

  return Math.hypot(corner.x - loose.x, corner.y - loose.y) < 0.5 ? [] : [corner];
}

function route(
  vertices: g.Point[],
  args: OrthogonalArguments,
  view: dia.LinkView,
): g.PlainPoint[] {
  const ends = { from: view.sourceAnchor as g.PlainPoint | undefined, to: view.targetAnchor as g.PlainPoint | undefined };
  const own = boxOf(view, 'source');
  const loop = vertices.length === 0 && own !== undefined && view.model.source().id === view.model.target().id
    && ends.from !== undefined && ends.to !== undefined
    ? loopBends(ends.from, ends.to, own, Math.max(args.clearance ?? 20, MIN_CLEARANCE))
    : null;

  const sourceLoose = view.model.source().id === undefined;
  const targetLoose = view.model.target().id === undefined;
  let loose: g.PlainPoint[] | null = null;

  if (vertices.length === 0 && loop === null && ends.from !== undefined && ends.to !== undefined) {
    if (targetLoose && !sourceLoose) {
      const face = facesOf(ends.from, boxOf(view, 'source'))?.[0];
      loose = face === undefined ? null : elbowToLoose(ends.from, face, ends.to);
    } else if (sourceLoose && !targetLoose) {
      const face = facesOf(ends.to, boxOf(view, 'target'))?.[0];
      loose = face === undefined ? null : elbowToLoose(ends.to, face, ends.from)?.reverse() ?? null;
    }
  }

  let bends = loop ?? loose ?? manhattan(vertices, args, view);

  const from = view.sourceAnchor as g.PlainPoint | undefined;
  const to = view.targetAnchor as g.PlainPoint | undefined;
  const graph = view.model.graph as dia.Graph | null;
  if (from === undefined || to === undefined || graph === null) return bends;

  // Still through its own shapes: try again with no padding at all, and keep the first answer
  // rather than a cruder one if that does not help.
  const sourceBox = boxOf(view, 'source');
  const targetBox = boxOf(view, 'target');
  const through = (points: readonly g.PlainPoint[]): boolean => crosses(points, sourceBox)
    || crosses(points, targetBox);

  if (vertices.length === 0 && through([from, ...bends, to])) {
    const tighter = manhattan(vertices, { ...args, padding: 0 }, view, true);
    if (!through([from, ...tighter, to])) bends = tighter;
  }

  const links = graph.getLinks();
  const self = links.indexOf(view.model);

  // Squared up to the ends on every frame, as it is at rest: the router leaves the first and last
  // bends on its own grid, which draws a diagonal from an anchor that is not on it.
  const tidy = alignToEndpoints(from, bends, to);
  const reach = (args.separation ?? 0) * 5 + 1;
  const mine = boundsOf(tidy, from, to);
  const others: PointContract[][] = [];

  for (let index = 0; index < links.length; index += 1) {
    const link = links[index];
    if (link === undefined || link === view.model || isProvisional(link)
      || (link.get('router') !== undefined && index >= self)) continue;

    const drawn = drawnRoute(view.paper, link);
    // Declutter only moves a segment a few steps, so a route farther off than that cannot clash.
    if (drawn !== null && overlaps(mine, boundsOf(drawn), reach)) others.push(drawn);
  }

  const cleared = declutter(tidy, others, args.separation ?? 0);

  // A bend that coincided with the target anchor is dropped as a repeat; the end must survive.
  const last = cleared.at(-1);
  const keepsEnd = last !== undefined && Math.hypot(last.x - to.x, last.y - to.y) <= 0.5;

  return (keepsEnd ? cleared : [...cleared, to]).slice(1, -1);
}

interface Span { readonly x0: number; readonly y0: number; readonly x1: number; readonly y1: number }

function boundsOf(route: readonly g.PlainPoint[], ...extra: readonly g.PlainPoint[]): Span {
  let x0 = Infinity;
  let y0 = Infinity;
  let x1 = -Infinity;
  let y1 = -Infinity;

  for (const points of [route, extra]) {
    for (const point of points) {
      if (point.x < x0) x0 = point.x;
      if (point.x > x1) x1 = point.x;
      if (point.y < y0) y0 = point.y;
      if (point.y > y1) y1 = point.y;
    }
  }

  return { x0, y0, x1, y1 };
}

function overlaps(a: Span, b: Span, reach: number): boolean {
  return a.x0 - reach <= b.x1 && b.x0 - reach <= a.x1 && a.y0 - reach <= b.y1 && b.y0 - reach <= a.y1;
}

/** A connector's route as it is drawn now, ends included, or null before it has been drawn. */
export function drawnRoute(paper: dia.Paper | null, link: dia.Link): PointContract[] | null {
  const other = paper?.findViewByModel<dia.LinkView>(link);
  const { sourcePoint, route: bends, targetPoint } = (other ?? {}) as {
    sourcePoint?: g.PlainPoint;
    route?: g.PlainPoint[];
    targetPoint?: g.PlainPoint;
  };

  if (sourcePoint === undefined || bends === undefined || targetPoint === undefined) return null;

  return [sourcePoint, ...bends, targetPoint];
}

/** The routers the paper resolves names against: JointJS's own, and this one. */
export const routerNamespace = { ...routers, [ORTHOGONAL_ROUTER]: route };
