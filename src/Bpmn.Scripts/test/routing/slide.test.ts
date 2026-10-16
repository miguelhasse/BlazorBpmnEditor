import { describe, expect, it } from 'vitest';

import type { PointContract } from '../../src/interop/contracts.js';
import {
  isLegal,
  nearestSegment,
  slideSegment,
  slideWithin,
  type SlideEnvironment,
} from '../../src/routing/slide.js';

/** A Z: out along y = 0, across at x = 100, along y = 80. Segment 1 is the vertical run. */
const Z: readonly PointContract[] = [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 80 }, { x: 200, y: 80 }];

const FREE: SlideEnvironment = { others: [], boxes: [], stub: 10 };

const orthogonal = (route: readonly PointContract[]): boolean => route.slice(1).every((point, index) => {
  const previous = route[index];
  return previous !== undefined && (Math.abs(previous.x - point.x) < 0.5 || Math.abs(previous.y - point.y) < 0.5);
});

describe('picking the run under a point', () => {
  it('finds the nearest segment', () => {
    expect(nearestSegment(Z, { x: 100, y: 40 })).toBe(1);
    expect(nearestSegment(Z, { x: 20, y: 3 })).toBe(0);
    expect(nearestSegment(Z, { x: 180, y: 78 })).toBe(2);
  });

  it('has none for a route that is only a point', () => {
    expect(nearestSegment([{ x: 0, y: 0 }], { x: 1, y: 1 })).toBeNull();
  });
});

describe('sliding a run', () => {
  it('moves an interior run sideways and keeps every segment on its axis', () => {
    const route = slideSegment(Z, 1, 30, 10);

    expect(route).toEqual([{ x: 0, y: 0 }, { x: 130, y: 0 }, { x: 130, y: 80 }, { x: 200, y: 80 }]);
    expect(orthogonal(route)).toBe(true);
  });

  it('is the same route for an offset of nothing', () => {
    expect(slideSegment(Z, 1, 0, 10)).toBe(Z);
  });

  it('collapses a run slid onto its neighbour instead of doubling back', () => {
    // Slid right past the end of the last run: the route would fold back over itself.
    const route = slideSegment(Z, 1, 120, 10);

    expect(orthogonal(route)).toBe(true);
    expect(route[0]).toEqual({ x: 0, y: 0 });
    expect(route.at(-1)).toEqual({ x: 200, y: 80 });
  });

  it('keeps an end where it is by stepping the first run out and across', () => {
    const route = slideSegment(Z, 0, 20, 10);

    expect(route[0]).toEqual({ x: 0, y: 0 });
    expect(route[1]).toEqual({ x: 10, y: 0 });
    expect(route[2]).toEqual({ x: 10, y: 20 });
    expect(orthogonal(route)).toBe(true);
  });

  it('keeps the other end where it is for the last run', () => {
    const route = slideSegment(Z, 2, -20, 10);

    expect(route.at(-1)).toEqual({ x: 200, y: 80 });
    expect(route.at(-2)).toEqual({ x: 190, y: 80 });
    expect(orthogonal(route)).toBe(true);
  });

  it('turns a straight two-point flow into a stepped one with both ends where they were', () => {
    const straight: readonly PointContract[] = [{ x: 0, y: 0 }, { x: 100, y: 0 }];
    const route = slideSegment(straight, 0, 20, 10);

    expect(route).toEqual([
      { x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 20 }, { x: 90, y: 20 }, { x: 90, y: 0 }, { x: 100, y: 0 },
    ]);
  });
});

describe('what a slide has to keep clear of', () => {
  it('is legal when nothing is in the way', () => {
    expect(isLegal(Z, slideSegment(Z, 1, 30, 10), FREE)).toBe(true);
  });

  it('is not legal along another connector', () => {
    const environment: SlideEnvironment = { ...FREE, others: [[{ x: 130, y: -20 }, { x: 130, y: 100 }]] };

    expect(isLegal(Z, slideSegment(Z, 1, 30, 10), environment)).toBe(false);
  });

  it('is not legal through one of its own shapes', () => {
    const environment: SlideEnvironment = { ...FREE, boxes: [{ x: 120, y: 20, width: 40, height: 40 }] };

    expect(isLegal(Z, slideSegment(Z, 1, 30, 10), environment)).toBe(false);
  });

  it('does not hold a route to what it already was', () => {
    // The route already lies along the other connector, and slides off it.
    const environment: SlideEnvironment = { ...FREE, others: [[{ x: 0, y: 0 }, { x: 100, y: 0 }]] };

    expect(isLegal(Z, slideSegment(Z, 1, 30, 10), environment)).toBe(true);
  });

  it('stops a drag against what is in the way instead of jumping over or refusing it', () => {
    const environment: SlideEnvironment = { ...FREE, boxes: [{ x: 125, y: 20, width: 60, height: 40 }] };
    const route = slideWithin(Z, 1, 60, environment);

    // The run stops against the box at x = 125, which is held two units inside its edge.
    const run = route[1];
    expect(run?.x).toBeGreaterThan(100);
    expect(run?.x).toBeLessThanOrEqual(127);
    expect(orthogonal(route)).toBe(true);
  });

  it('crosses over another connector rather than stopping at it', () => {
    const environment: SlideEnvironment = { ...FREE, others: [[{ x: 130, y: 0 }, { x: 130, y: 80 }]] };
    const route = slideWithin(Z, 1, 60, environment);

    expect(route[1]).toEqual({ x: 160, y: 0 });
  });

  it('leaves the route alone when every offset is illegal', () => {
    const environment: SlideEnvironment = { ...FREE, boxes: [{ x: 90, y: 20, width: 200, height: 40 }] };

    expect(slideWithin(Z, 1, 30, environment)).toBe(Z);
  });
});
