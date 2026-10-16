import { describe, expect, it } from 'vitest';

import { snapToPeers, type Box } from '../../src/snapping/alignment.js';

/**
 * Alignment guides.
 *
 * The behaviour worth pinning is which candidate wins and what the guide spans — those are the
 * decisions that make snapping feel deliberate rather than jumpy, and neither is visible from
 * reading the geometry.
 */
const box = (x: number, y: number, width = 100, height = 80): Box => ({ x, y, width, height });

describe('snapToPeers', () => {
  it('leaves a shape alone when nothing is within tolerance', () => {
    const result = snapToPeers(box(0, 0), [box(500, 500)], 5);

    expect(result).toEqual({ x: 0, y: 0, guides: [] });
  });

  it('snaps a near-aligned left edge and draws a vertical guide', () => {
    const result = snapToPeers(box(103, 300), [box(100, 100)], 5);

    expect(result.x).toBe(100);
    expect(result.y).toBe(300);

    const guide = result.guides.find((g) => g.orientation === 'vertical');
    expect(guide?.position).toBe(100);
  });

  it('spans the guide across both shapes so it visibly joins them', () => {
    // Moving shape at y 300..380, peer at y 100..180 — the line has to cover 100..380 or it
    // appears to point at nothing.
    const result = snapToPeers(box(103, 300), [box(100, 100)], 5);
    const guide = result.guides.find((g) => g.orientation === 'vertical');

    expect(guide?.from).toBe(100);
    expect(guide?.to).toBe(380);
  });

  it('aligns an edge to a peer centre, not only like to like', () => {
    // The peer spans x 100..200, so its centre is 150. A shape whose left edge is at 152 should
    // snap to it; comparing only left-to-left would miss this entirely.
    const result = snapToPeers(box(152, 400), [box(100, 100)], 5);

    expect(result.x).toBe(150);
  });

  it('resolves the two axes independently', () => {
    // Horizontally near one peer, vertically near a different one.
    const result = snapToPeers(box(102, 298), [box(100, 700), box(900, 300)], 5);

    expect(result.x).toBe(100);
    expect(result.y).toBe(300);
    expect(result.guides).toHaveLength(2);
  });

  it('prefers the nearest candidate when several are in range', () => {
    const result = snapToPeers(box(104, 400), [box(100, 100), box(105, 200)], 6);

    expect(result.x).toBe(105);
  });

  it('keeps the earlier candidate when two are equally near, so the result is stable', () => {
    // Both peers are 3 away, one below and one above. Preferring strictly-nearer means the first
    // wins and the shape does not flicker between them as the pointer moves.
    const result = snapToPeers(box(100, 400), [box(97, 100), box(103, 200)], 5);

    expect(result.x).toBe(97);
  });

  it('does nothing when tolerance is zero', () => {
    const result = snapToPeers(box(100.4, 100), [box(100, 100)], 0);

    expect(result).toEqual({ x: 100.4, y: 100, guides: [] });
  });

  it('does nothing when there are no peers', () => {
    const result = snapToPeers(box(7, 9), [], 10);

    expect(result).toEqual({ x: 7, y: 9, guides: [] });
  });

  it('snaps shapes of different sizes by their centres', () => {
    // A 36px Event centred on a 100px Task: 100 + 50 = 150, so the Event's centre must land at
    // 150 and its x at 132.
    const result = snapToPeers(box(130, 400, 36, 36), [box(100, 100)], 5);

    expect(result.x).toBe(132);
  });
});
