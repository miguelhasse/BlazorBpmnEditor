import { describe, expect, it } from 'vitest';

import { roundedPath } from '../../src/routing/rounded-path.js';

const curves = (path: string): number => (path.match(/C/g) ?? []).length;

describe('drawing a route', () => {
  it('rounds each corner with one curve', () => {
    const path = roundedPath([{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 80 }], 8);

    expect(curves(path)).toBe(1);
    expect(path.startsWith('M 0 0')).toBe(true);
    expect(path.endsWith('L 100 80')).toBe(true);
  });

  it('draws a straight line as one line', () => {
    expect(roundedPath([{ x: 0, y: 0 }, { x: 100, y: 0 }], 8)).toBe('M 0 0 L 100 0');
  });

  it('draws a one-unit step between two long runs as a single smooth S, not two sharp corners', () => {
    // 223,588 > 231.2,588 > 231,589 > 231.2,590 > 277,590: a step of two units, as the Pizza Store has.
    const path = roundedPath(
      [{ x: 185, y: 588 }, { x: 231.2, y: 588 }, { x: 231, y: 589 }, { x: 231.2, y: 590 }, { x: 277, y: 590 }],
      8,
    );

    expect(curves(path)).toBe(1);
    expect(path.endsWith('L 277 590')).toBe(true);
  });

  it('keeps the ends exactly where the route has them', () => {
    const path = roundedPath(
      [{ x: 533.5, y: 208.5 }, { x: 694, y: 209 }, { x: 701.7, y: 208.7 }, { x: 702, y: 208 }, { x: 701.7, y: 207 }, { x: 869.5, y: 207 }],
      8,
    );

    expect(path.startsWith('M 533.5 208.5')).toBe(true);
    expect(path.endsWith('L 869.5 207')).toBe(true);
  });

  it('does not take a real turn for a jog', () => {
    // A short leg that turns the route round is a corner, not a step between parallel lines.
    const path = roundedPath([{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 2 }, { x: 0, y: 2 }], 8);

    expect(curves(path)).toBe(2);
  });

  it('draws sharp corners when asked for no rounding', () => {
    expect(curves(roundedPath([{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 80 }], 0))).toBe(0);
  });
});
