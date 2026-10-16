import { describe, expect, it } from 'vitest';

import { GestureTracker, pinchSnapshot } from '../../src/viewport/gestures.js';

/**
 * Touch gesture arithmetic.
 *
 * One pointer is a pan, two are a pinch, and the interesting cases are the transitions: adding a
 * second finger must not jump the zoom, and lifting one of three must not either.
 */
describe('pinchSnapshot', () => {
  it('reports the distance and midpoint of two pointers', () => {
    const snapshot = pinchSnapshot({ id: 1, x: 0, y: 0 }, { id: 2, x: 30, y: 40 });

    expect(snapshot.distance).toBe(50);
    expect(snapshot.centerX).toBe(15);
    expect(snapshot.centerY).toBe(20);
  });
});

describe('GestureTracker', () => {
  it('reports a single pointer move as a pan', () => {
    const tracker = new GestureTracker();
    tracker.start({ id: 1, x: 10, y: 10 });

    expect(tracker.move({ id: 1, x: 25, y: 5 })).toEqual({ kind: 'pan', dx: 15, dy: -5 });
  });

  it('ignores a move for a pointer it never saw go down', () => {
    const tracker = new GestureTracker();

    expect(tracker.move({ id: 99, x: 1, y: 1 })).toEqual({ kind: 'none' });
  });

  it('reports spreading two pointers as a proportional pinch', () => {
    const tracker = new GestureTracker();
    tracker.start({ id: 1, x: 0, y: 0 });
    tracker.start({ id: 2, x: 100, y: 0 });

    const intent = tracker.move({ id: 2, x: 200, y: 0 });

    expect(intent.kind).toBe('pinch');
    if (intent.kind !== 'pinch') return;
    expect(intent.scale).toBe(2);
    expect(intent.centerX).toBe(100);
  });

  it('reports the midpoint translation alongside the scale', () => {
    // Two fingers usually drift as they spread. Handling only the scale makes the gesture feel
    // like it is fighting the user.
    const tracker = new GestureTracker();
    tracker.start({ id: 1, x: 0, y: 0 });
    tracker.start({ id: 2, x: 100, y: 0 });

    const intent = tracker.move({ id: 2, x: 100, y: 100 });

    expect(intent.kind).toBe('pinch');
    if (intent.kind !== 'pinch') return;
    expect(intent.dy).toBe(50);
  });

  it('does not jump when a second finger lands', () => {
    // The baseline is taken on pointer-down, so the very first two-finger move is measured from
    // where the fingers actually were, not from a stale single-pointer state.
    const tracker = new GestureTracker();
    tracker.start({ id: 1, x: 0, y: 0 });
    tracker.move({ id: 1, x: 50, y: 0 });
    tracker.start({ id: 2, x: 150, y: 0 });

    const intent = tracker.move({ id: 2, x: 150, y: 0 });

    expect(intent.kind).toBe('pinch');
    if (intent.kind !== 'pinch') return;
    expect(intent.scale).toBe(1);
  });

  it('re-baselines when one of three pointers lifts', () => {
    const tracker = new GestureTracker();
    tracker.start({ id: 1, x: 0, y: 0 });
    tracker.start({ id: 2, x: 100, y: 0 });
    tracker.start({ id: 3, x: 300, y: 0 });

    tracker.end(1);

    // Pointers 2 and 3 are 200 apart and have not moved, so the next move must read as no change
    // rather than as a jump from the old 1-to-2 distance of 100.
    const intent = tracker.move({ id: 3, x: 300, y: 0 });

    expect(intent.kind).toBe('pinch');
    if (intent.kind !== 'pinch') return;
    expect(intent.scale).toBe(1);
  });

  it('falls back to panning when a pinch drops to one pointer', () => {
    const tracker = new GestureTracker();
    tracker.start({ id: 1, x: 0, y: 0 });
    tracker.start({ id: 2, x: 100, y: 0 });
    tracker.end(2);

    expect(tracker.isPinching).toBe(false);
    expect(tracker.move({ id: 1, x: 10, y: 0 })).toEqual({ kind: 'pan', dx: 10, dy: 0 });
  });

  it('counts and clears active pointers', () => {
    const tracker = new GestureTracker();
    tracker.start({ id: 1, x: 0, y: 0 });
    tracker.start({ id: 2, x: 5, y: 5 });

    expect(tracker.count).toBe(2);

    tracker.clear();
    expect(tracker.count).toBe(0);
  });
});
