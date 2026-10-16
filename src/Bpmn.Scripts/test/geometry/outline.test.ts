import { describe, expect, it } from 'vitest';

import { centerOf, outlineIntersection, projectToOutline, snapToGrid, type Outline } from '../../src/geometry/outline.js';

/**
 * Specification section 4.6: connectors attach anywhere on a shape's outline, so projection has
 * to land exactly on the boundary for every shape kind — including after the pointer has been
 * snapped to a grid the boundary never crosses.
 */
const box = { x: 100, y: 100, width: 100, height: 80 };

const rect: Outline = { kind: 'rect', bounds: box };
const ellipse: Outline = { kind: 'ellipse', bounds: { x: 100, y: 100, width: 36, height: 36 } };
const diamond: Outline = { kind: 'diamond', bounds: { x: 100, y: 100, width: 50, height: 50 } };

const isOnRect = (point: { x: number; y: number }): boolean =>
  Math.abs(point.x - box.x) < 1e-9
  || Math.abs(point.x - (box.x + box.width)) < 1e-9
  || Math.abs(point.y - box.y) < 1e-9
  || Math.abs(point.y - (box.y + box.height)) < 1e-9;

describe('projectToOutline', () => {
  it('projects an outside point onto the nearest rectangle edge', () => {
    expect(projectToOutline({ x: 50, y: 140 }, rect)).toEqual({ x: 100, y: 140 });
  });

  it('projects an inside point onto the closest rectangle edge, not the centre', () => {
    const projected = projectToOutline({ x: 110, y: 140 }, rect);

    expect(projected).toEqual({ x: 100, y: 140 });
    expect(isOnRect(projected)).toBe(true);
  });

  it('lands exactly on an ellipse for an arbitrary direction', () => {
    const center = centerOf(ellipse.bounds);
    const projected = projectToOutline({ x: 200, y: 200 }, ellipse);
    const distance = Math.hypot(projected.x - center.x, projected.y - center.y);

    expect(distance).toBeCloseTo(18, 9);
  });

  it('lands on a diamond edge rather than inside it', () => {
    const projected = projectToOutline({ x: 90, y: 90 }, diamond);

    // On the top-left edge, |dx| + |dy| from the centre equals half the diagonal.
    const center = centerOf(diamond.bounds);
    expect(Math.abs(projected.x - center.x) + Math.abs(projected.y - center.y)).toBeCloseTo(25, 9);
  });
});

describe('snapToGrid then project', () => {
  it('keeps the endpoint on the outline even when no grid point touches it', () => {
    // The circle's boundary crosses no 10-unit grid intersection, which is exactly the case
    // section 4.6 calls out: snap first, then project, never the reverse.
    const snapped = snapToGrid({ x: 143, y: 147 }, 10);
    expect(snapped).toEqual({ x: 140, y: 150 });

    const center = centerOf(ellipse.bounds);
    const projected = projectToOutline(snapped, ellipse);

    expect(Math.hypot(projected.x - center.x, projected.y - center.y)).toBeCloseTo(18, 9);
  });

  it('leaves the point untouched when snapping is disabled', () => {
    expect(snapToGrid({ x: 143.5, y: 147.5 }, 0)).toEqual({ x: 143.5, y: 147.5 });
  });
});

describe('outlineIntersection', () => {
  it('returns the point where a ray towards a target leaves the shape', () => {
    const projected = outlineIntersection(rect, { x: 500, y: 140 });

    expect(projected.x).toBeCloseTo(200, 9);
    expect(projected.y).toBeCloseTo(140, 9);
  });

  it('returns the centre when the target is the centre', () => {
    const center = centerOf(box);
    expect(outlineIntersection(rect, center)).toEqual(center);
  });
});
