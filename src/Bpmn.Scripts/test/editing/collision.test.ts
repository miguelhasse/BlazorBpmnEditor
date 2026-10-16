import { describe, expect, it } from 'vitest';

import { findCollision, overlaps, type CollisionCandidate } from '../../src/editing/collision.js';

/**
 * DI-011.
 *
 * The rule is not "no two shapes overlap" — containers enclose their content, Boundary Events sit
 * on their host's outline, and shapes in different Pools are not laid out against each other at
 * all. Getting those exemptions wrong makes ordinary diagrams unbuildable, so each one is stated.
 */
const at = (
  id: string,
  x: number,
  y: number,
  overrides: Partial<CollisionCandidate> = {},
): CollisionCandidate => ({
  id,
  containerId: 'Process_1',
  box: { x, y, width: 100, height: 80 },
  participates: true,
  ...overrides,
});

describe('overlaps', () => {
  it('is false for separated rectangles', () => {
    expect(overlaps({ x: 0, y: 0, width: 10, height: 10 }, { x: 20, y: 0, width: 10, height: 10 }, 0))
      .toBe(false);
  });

  it('treats edge-to-edge touching as clear at zero clearance', () => {
    // Legal BPMN; forbidding it would refuse layouts other tools produce.
    expect(overlaps({ x: 0, y: 0, width: 10, height: 10 }, { x: 10, y: 0, width: 10, height: 10 }, 0))
      .toBe(false);
  });

  it('treats the same touching pair as a collision once clearance is required', () => {
    expect(overlaps({ x: 0, y: 0, width: 10, height: 10 }, { x: 10, y: 0, width: 10, height: 10 }, 4))
      .toBe(true);
  });

  it('is true for a genuine overlap', () => {
    expect(overlaps({ x: 0, y: 0, width: 10, height: 10 }, { x: 5, y: 5, width: 10, height: 10 }, 0))
      .toBe(true);
  });
});

describe('findCollision', () => {
  it('finds nothing when the mover lands clear', () => {
    const candidates = [at('a', 0, 0), at('b', 400, 0)];

    expect(findCollision(candidates, new Set(['a']), 0)).toBeNull();
  });

  it('finds the pair when the mover lands on a peer', () => {
    const candidates = [at('a', 380, 0), at('b', 400, 0)];

    expect(findCollision(candidates, new Set(['a']), 0)).toEqual({ id: 'a', peerId: 'b' });
  });

  it('ignores a peer in another container', () => {
    // Two nodes in different Pools are not laid out against each other, so overlapping bounds are
    // meaningless rather than invalid.
    const candidates = [at('a', 380, 0), at('b', 400, 0, { containerId: 'Process_2' })];

    expect(findCollision(candidates, new Set(['a']), 0)).toBeNull();
  });

  it('ignores an element that does not participate', () => {
    // A Boundary Event overlapping its host is required by DI-010, not a defect.
    const candidates = [at('a', 380, 0), at('b', 400, 0, { participates: false })];

    expect(findCollision(candidates, new Set(['a']), 0)).toBeNull();
  });

  it('ignores collisions between two elements that are both stationary', () => {
    // An imported overlap is reportable, but a drag elsewhere must not be blocked by it.
    const candidates = [at('a', 0, 0), at('b', 10, 0), at('c', 900, 0)];

    expect(findCollision(candidates, new Set(['c']), 0)).toBeNull();
  });

  it('reports a collision between two elements that are moving together only once', () => {
    const candidates = [at('a', 0, 0), at('b', 10, 0)];

    const collision = findCollision(candidates, new Set(['a', 'b']), 0);

    expect(collision).toEqual({ id: 'a', peerId: 'b' });
  });

  it('applies the clearance to the moving element as well', () => {
    const candidates = [at('a', 0, 0), at('b', 100, 0)];

    expect(findCollision(candidates, new Set(['a']), 0)).toBeNull();
    expect(findCollision(candidates, new Set(['a']), 10)).not.toBeNull();
  });
});
