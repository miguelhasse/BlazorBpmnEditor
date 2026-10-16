import { describe, expect, it } from 'vitest';

import { edgeLabelPosition } from '../../src/labels/edge-label-position.js';

const route = [
  { x: 0, y: 0 },
  { x: 100, y: 0 },
  { x: 100, y: 100 },
];

describe('edgeLabelPosition', () => {
  it('anchors on the nearest point of the route and offsets by the remainder', () => {
    const position = edgeLabelPosition(route, { x: 30, y: -30, width: 40, height: 20 });

    expect(position).toEqual({ distance: 50, offset: { x: 0, y: -20 } });
  });

  it('measures distance along later segments', () => {
    const position = edgeLabelPosition(route, { x: 110, y: 40, width: 20, height: 20 });

    expect(position?.distance).toBeCloseTo(150);
    expect(position?.offset).toEqual({ x: 20, y: 0 });
  });

  it('returns null when the box is far from the route', () => {
    expect(edgeLabelPosition(route, { x: 985, y: 622, width: 1, height: 15 })).toBeNull();
  });

  it('returns null for a route with fewer than two points', () => {
    expect(edgeLabelPosition([{ x: 0, y: 0 }], { x: 0, y: 0, width: 1, height: 1 })).toBeNull();
  });
});
