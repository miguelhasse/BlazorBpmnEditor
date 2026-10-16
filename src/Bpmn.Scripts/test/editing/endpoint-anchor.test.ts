import { dia } from '@joint/core';
import { describe, expect, it } from 'vitest';

import { attachmentPoint, routeWith } from '../../src/editing/endpoint-anchor.js';
import type { GridOptionsContract } from '../../src/interop/contracts.js';

/**
 * Connector endpoint attachment (specification section 4.6).
 *
 * The rule that matters is the *order*: snap the pointer to the grid, then project onto the
 * outline. Doing it the other way round pulls the committed point off the boundary whenever the
 * boundary does not happen to cross a grid line, which for a circle or a diamond is almost always.
 */
const off: GridOptionsContract = { size: 10, isVisible: true, snapEnabled: false };
const on: GridOptionsContract = { size: 10, isVisible: true, snapEnabled: true };

const element = (
  elementType: string,
  box: { x: number; y: number; width: number; height: number },
): dia.Element => new dia.Element({
  id: 'Shape_1',
  type: 'standard.Rectangle',
  position: { x: box.x, y: box.y },
  size: { width: box.width, height: box.height },
  bpmn: { elementType },
});

/** How far a point is from a rectangle's boundary; zero means exactly on it. */
const distanceToRect = (
  point: { x: number; y: number },
  box: { x: number; y: number; width: number; height: number },
): number => {
  const dx = Math.min(Math.abs(point.x - box.x), Math.abs(point.x - (box.x + box.width)));
  const dy = Math.min(Math.abs(point.y - box.y), Math.abs(point.y - (box.y + box.height)));

  const onVertical = point.y >= box.y && point.y <= box.y + box.height;
  const onHorizontal = point.x >= box.x && point.x <= box.x + box.width;

  return Math.min(onVertical ? dx : Infinity, onHorizontal ? dy : Infinity);
};

describe('attachmentPoint', () => {
  it('projects a pointer outside the shape onto its nearest edge', () => {
    const task = element('task', { x: 100, y: 100, width: 100, height: 80 });

    expect(attachmentPoint({ x: 250, y: 140 }, task, off)).toEqual({ x: 200, y: 140 });
  });

  it('projects a pointer inside the shape outwards to the boundary', () => {
    const task = element('task', { x: 100, y: 100, width: 100, height: 80 });

    // A connector ends on the outline, never in the middle of a shape.
    expect(attachmentPoint({ x: 190, y: 140 }, task, off)).toEqual({ x: 200, y: 140 });
  });

  it('leaves the attachment exactly on the outline when snapping is on', () => {
    // The point of the ordering. 36 is not a multiple of 10, so a grid-snapped result that was
    // not re-projected would float off the circle.
    const event = element('startEvent', { x: 100, y: 100, width: 36, height: 36 });
    const point = attachmentPoint({ x: 153, y: 121 }, event, on);

    const cx = 118;
    const cy = 118;
    const radius = 18;
    const distance = Math.hypot(point.x - cx, point.y - cy);

    expect(Math.abs(distance - radius)).toBeLessThan(0.001);
  });

  it('snaps the pointer, so the same shape gives different points for nearby aims', () => {
    const task = element('task', { x: 100, y: 100, width: 100, height: 80 });

    // 111 and 118 snap to 110 and 120, which are different attachment points; without snapping
    // they would land 7 units apart on the edge instead of 10. Kept clear of the side's midpoint at
    // 140, where the magnet would pull both onto the same point.
    expect(attachmentPoint({ x: 250, y: 111 }, task, on)).toEqual({ x: 200, y: 110 });
    expect(attachmentPoint({ x: 250, y: 118 }, task, on)).toEqual({ x: 200, y: 120 });
  });

  it('does not quantise an outline into a set of ports', () => {
    // Section 4.6 is explicit: snapping must not reduce the outline to fixed anchors. Ten aims
    // along one edge must give ten distinct points, not one repeated.
    const pool = element('pool', { x: 0, y: 0, width: 600, height: 400 });

    const points = new Set(
      Array.from({ length: 10 }, (_unused, index) =>
        JSON.stringify(attachmentPoint({ x: 700, y: 20 + (index * 10) }, pool, on))),
    );

    expect(points.size).toBe(10);
  });

  it('keeps a diamond attachment on the diamond', () => {
    const gateway = element('exclusiveGateway', { x: 100, y: 100, width: 50, height: 50 });
    const point = attachmentPoint({ x: 200, y: 200 }, gateway, off);

    // |dx| + |dy| from the centre equals the half-diagonal on a diamond's boundary.
    const manhattan = Math.abs(point.x - 125) + Math.abs(point.y - 125);
    expect(Math.abs(manhattan - 25)).toBeLessThan(0.001);
  });

  it('falls back to a rectangle for a type it has never heard of', () => {
    const unknown = element('somethingNew', { x: 100, y: 100, width: 100, height: 80 });
    const point = attachmentPoint({ x: 250, y: 140 }, unknown, off);

    expect(distanceToRect(point, { x: 100, y: 100, width: 100, height: 80 })).toBeLessThan(0.001);
  });
});

describe('routeWith', () => {
  it('keeps the bends and replaces only the ends', () => {
    // A reconnect changes where a line meets a shape, not the route the user shaped.
    const existing = [{ x: 0, y: 0 }, { x: 50, y: 50 }, { x: 80, y: 20 }, { x: 100, y: 100 }];

    expect(routeWith(existing, { x: 5, y: 5 }, { x: 95, y: 95 })).toEqual([
      { x: 5, y: 5 },
      { x: 50, y: 50 },
      { x: 80, y: 20 },
      { x: 95, y: 95 },
    ]);
  });

  it('gives a straight route two points, which is DI-001s minimum', () => {
    expect(routeWith([{ x: 0, y: 0 }, { x: 10, y: 10 }], { x: 1, y: 1 }, { x: 9, y: 9 }))
      .toEqual([{ x: 1, y: 1 }, { x: 9, y: 9 }]);
  });

  it('tolerates an empty existing route', () => {
    expect(routeWith([], { x: 1, y: 1 }, { x: 9, y: 9 })).toEqual([{ x: 1, y: 1 }, { x: 9, y: 9 }]);
  });
});
