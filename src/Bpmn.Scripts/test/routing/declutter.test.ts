import { describe, expect, it } from 'vitest';

import { declutter } from '../../src/routing/declutter.js';

/** A Z route: out along x, across the gap at x = 100, along x again. */
const Z = [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 80 }, { x: 200, y: 80 }];

describe('keeping connectors apart', () => {
  it('leaves a route alone when nothing shares its line', () => {
    const other = [{ x: 0, y: 200 }, { x: 300, y: 200 }];

    expect(declutter(Z, [other], 12)).toEqual(Z);
  });

  it('moves an interior segment that lies on another connector', () => {
    const other = [{ x: 100, y: -20 }, { x: 100, y: 100 }];
    const result = declutter(Z, [other], 12);

    expect(result[1]).toEqual({ x: 112, y: 0 });
    expect(result[2]).toEqual({ x: 112, y: 80 });
  });

  it('stays orthogonal and keeps its ends', () => {
    const others = [[{ x: 100, y: 0 }, { x: 100, y: 80 }], [{ x: 112, y: 0 }, { x: 112, y: 80 }]];
    const result = declutter(Z, others, 12);

    expect(result[0]).toEqual(Z[0]);
    expect(result.at(-1)).toEqual(Z.at(-1));
    result.slice(1).forEach((point, index) => {
      const previous = result[index];
      expect(previous !== undefined && (previous.x === point.x || previous.y === point.y)).toBe(true);
    });
    // The second corridor is taken as well, so the nearest free one is on the other side.
    expect(result[1]?.x).toBe(88);
  });

  it('does not move a segment that only touches another at a point', () => {
    const other = [{ x: 100, y: 80 }, { x: 100, y: 200 }];

    expect(declutter(Z, [other], 12)).toEqual(Z);
  });

  it('will not flip a neighbouring segment to get clear', () => {
    // 5 units of lead-in: moving the corridor 12 towards the start would fold the route back.
    const short = [{ x: 0, y: 0 }, { x: 5, y: 0 }, { x: 5, y: 80 }, { x: 200, y: 80 }];
    const others = [[{ x: 5, y: 0 }, { x: 5, y: 80 }], [{ x: 17, y: 0 }, { x: 17, y: 80 }]];
    const result = declutter(short, others, 12);

    expect(result[1]?.x).toBeGreaterThanOrEqual(5);
  });

  it('cannot move a route with no interior segment', () => {
    const elbow = [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 80 }];
    const other = [{ x: 50, y: 0 }, { x: 150, y: 0 }];

    expect(declutter(elbow, [other], 12)).toEqual(elbow);
  });
});

describe('a connector never overlaps itself', () => {
  it('removes a bend that doubles back over the segment before it', async () => {
    const { dropRepeats } = await import('../../src/routing/align.js');
    const spur = [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 60, y: 0 }, { x: 60, y: 50 }];

    expect(dropRepeats(spur)).toEqual([{ x: 0, y: 0 }, { x: 60, y: 0 }, { x: 60, y: 50 }]);
  });

  it('moves a segment off an earlier segment of its own route', () => {
    const loop = [
      { x: 0, y: 0 }, { x: 0, y: -40 }, { x: 100, y: -40 }, { x: 100, y: 40 },
      { x: 0, y: 40 }, { x: 0, y: -40 }, { x: -50, y: -40 },
    ];
    const result = declutter(loop, [], 12);

    expect(result).not.toEqual(loop);
  });
});
