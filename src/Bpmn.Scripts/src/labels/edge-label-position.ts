import * as constants from '../geometry/constants.js';
import type { BoundsContract, PointContract } from '../model/contracts.js';

/** A label position along a link, in the form JointJS link labels take. */
export interface EdgeLabelPosition {
  /** Pixels along the route from its source (a value above 1 is absolute in JointJS). */
  readonly distance: number;
  /** Offset of the label centre from the point on the route at `distance`. */
  readonly offset: PointContract;
}

/**
 * Expresses an imported edge label box as a position relative to its connector.
 *
 * The box centre is projected onto the nearest point of the route; the label is then anchored there
 * and offset by the remainder, so it keeps its place beside the connector when the route is edited.
 *
 * @param waypoints The connector's full route, endpoints included, in diagram coordinates.
 * @param box The imported label box, in diagram coordinates.
 * @returns `null` when the route has fewer than two points, or the box is too far from it to be believed.
 */
export function edgeLabelPosition(
  waypoints: readonly PointContract[],
  box: BoundsContract,
): EdgeLabelPosition | null {
  if (waypoints.length < 2) return null;

  const centre = { x: box.x + box.width / 2, y: box.y + box.height / 2 };

  let best = { distance: 0, point: waypoints[0] ?? centre, gap: Number.POSITIVE_INFINITY };
  let travelled = 0;

  for (let i = 1; i < waypoints.length; i++) {
    const a = waypoints[i - 1];
    const b = waypoints[i];
    if (a === undefined || b === undefined) continue;

    const dx = b.x - a.x;
    const dy = b.y - a.y;
    const length = Math.hypot(dx, dy);

    const t = length === 0 ? 0 : Math.max(0, Math.min(1, ((centre.x - a.x) * dx + (centre.y - a.y) * dy) / (length * length)));
    const point = { x: a.x + dx * t, y: a.y + dy * t };
    const gap = Math.hypot(centre.x - point.x, centre.y - point.y);

    if (gap < best.gap) best = { distance: travelled + length * t, point, gap };
    travelled += length;
  }

  if (best.gap > constants.EDGE_LABEL_MAX_DISTANCE) return null;

  return { distance: best.distance, offset: { x: centre.x - best.point.x, y: centre.y - best.point.y } };
}
