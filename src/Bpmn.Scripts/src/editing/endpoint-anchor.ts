import type { dia } from '@joint/core';

import { outlineOf, projectToOutline, snapToGrid, snapToMagnet } from '../geometry/outline.js';
import type { GridOptionsContract, PointContract } from '../interop/contracts.js';

/**
 * Where a connector attaches to a shape (specification section 4.6).
 *
 * One function for all three gestures — creating a connector, reconnecting an end to a different
 * shape, and dragging an existing endpoint handle — because section 4.6 requires exactly that: the
 * same grid-snap setting and coordinate transform applied consistently, or the same connector ends
 * up in a different place depending on how it got there.
 *
 * **Snap first, then project.** The order is the whole rule. Projecting and then snapping would
 * pull the committed point off the outline whenever the outline does not happen to cross a grid
 * line, which for a circle or a diamond is almost always. Snapping first quantises the *pointer*,
 * which is what the user is aiming with, and the projection then guarantees the result lies
 * exactly on the boundary.
 *
 * What this never does is quantise the outline into ports. The projection is continuous: every
 * point of a circle, diamond or rounded rectangle remains reachable, which is the other half of
 * section 4.6.
 */
export function attachmentPoint(
  pointer: PointContract,
  element: dia.Element,
  grid: GridOptionsContract,
): PointContract {
  const snapped = grid.snapEnabled ? snapToGrid(pointer, grid.size) : pointer;
  const outline = outlineFor(element);

  // A magnet is an outline point, so pulling onto one after the projection keeps the result on the
  // boundary; a point that is not near one is left where the pointer put it.
  return snapToMagnet(projectToOutline(snapped, outline), outline);
}

/**
 * Keeps a connector end on the outline while it is being dragged.
 *
 * JointJS asks for the end definition on every pointer move over a shape it can snap to, and again
 * when the end is dropped. Answering with the attachment point as a shape-relative anchor (the form
 * imported and created connectors are drawn with) makes the line follow the pointer along the
 * outline as it moves, snapping to a magnet when it passes near one, instead of sitting wherever
 * JointJS's own anchor policy would put it. What is drawn while dragging is then exactly what
 * `attachmentPoint` commits on release.
 *
 * @param grid The grid configuration, so the drag quantises the pointer the way creation does.
 * @returns A `connectionStrategy` for the paper.
 */
export function endpointStrategy(
  grid: GridOptionsContract,
): (end: dia.Link.EndJSON, view: dia.CellView, magnet: SVGElement, coords: PointContract) => dia.Link.EndJSON {
  return (end, view, _magnet, coords) => {
    if (!view.model.isElement()) return end;

    const element = view.model;
    const point = attachmentPoint({ x: coords.x, y: coords.y }, element, grid);
    const { x, y } = element.position();

    return {
      ...end,
      anchor: { name: 'topLeft', args: { dx: point.x - x, dy: point.y - y, useModelGeometry: true } },
      connectionPoint: { name: 'anchor' },
    };
  };
}

/** The outline a cell is drawn with, read from the metadata the shape factories stored. */
export function outlineFor(element: dia.Element) {
  const type = (element.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType;
  const { x, y } = element.position();
  const { width, height } = element.size();

  return outlineOf(type ?? '', { x, y, width, height });
}

/**
 * The waypoints a connector should carry between two shapes.
 *
 * The middle of the route is left alone. A reconnect or an endpoint drag changes where a line
 * meets a shape, not the bends the user put in it — replacing the whole route would silently
 * discard routing decisions, and sections 10.6 and 11 keep stored geometry as the document has it.
 *
 * Two points is the floor, because DI-001 is the schema's own minimum.
 */
export function routeWith(
  existing: readonly PointContract[],
  source: PointContract,
  target: PointContract,
): readonly PointContract[] {
  const middle = existing.length > 2 ? existing.slice(1, -1) : [];

  return [source, ...middle, target];
}
