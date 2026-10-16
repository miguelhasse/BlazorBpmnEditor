import { describe, expect, it } from 'vitest';

import { externalLabelBox, isPlausibleExternalLabel } from '../../src/labels/default-boxes.js';

/**
 * Where an external label sits relative to its shape.
 *
 * Below it, which is where BPMN puts an Event's name — except for a Boundary Event, which sits on
 * its host's border. "Below" then lands the name on top of the Activity whenever the Event is
 * anywhere but the bottom edge, where it reads as part of the Activity and often overlaps the
 * Activity's own text.
 */
const shape = { x: 100, y: 100, width: 36, height: 36 };

describe('externalLabelBox', () => {
  it('sits below the shape by default', () => {
    const { box, style } = externalLabelBox(shape);

    expect(box.y).toBeGreaterThan(shape.y + shape.height);
    expect(style.verticalAlign).toBe('top');
    expect(style.horizontalAlign).toBe('middle');
  });

  it('sits above the shape when the host is below it', () => {
    const { box, style } = externalLabelBox(shape, 'top');

    // Wholly clear of the shape, not merely anchored above its centre.
    expect(box.y + box.height).toBeLessThanOrEqual(shape.y);
    expect(style.verticalAlign).toBe('bottom');
  });

  it('sits to the left, aligned towards the shape', () => {
    const { box, style } = externalLabelBox(shape, 'left');

    expect(box.x + box.width).toBeLessThanOrEqual(shape.x);

    // Ending at the shape rather than starting a label-width away from it.
    expect(style.horizontalAlign).toBe('end');
    expect(style.verticalAlign).toBe('middle');
  });

  it('sits to the right, aligned towards the shape', () => {
    const { box, style } = externalLabelBox(shape, 'right');

    expect(box.x).toBeGreaterThanOrEqual(shape.x + shape.width);
    expect(style.horizontalAlign).toBe('start');
  });

  it('never overlaps the shape, whichever side it is on', () => {
    // The property the Boundary Event case actually needs: a label placed away from the host
    // cannot be sitting on the shape it was placed away from.
    for (const side of ['top', 'bottom', 'left', 'right'] as const) {
      const { box } = externalLabelBox(shape, side);

      const overlaps = box.x < shape.x + shape.width
        && box.x + box.width > shape.x
        && box.y < shape.y + shape.height
        && box.y + box.height > shape.y;

      expect(overlaps, `${side} label overlaps its shape`).toBe(false);
    }
  });

  it('keeps the vertical sides centred on the shape', () => {
    const { box } = externalLabelBox(shape, 'left');

    expect(box.y + (box.height / 2)).toBeCloseTo(shape.y + (shape.height / 2), 5);
  });
});

describe('isPlausibleExternalLabel', () => {
  it('accepts a box directly under the shape', () => {
    expect(isPlausibleExternalLabel({ x: 74, y: 140, width: 88, height: 5 }, shape)).toBe(true);
  });

  it('accepts a box overlapping the shape', () => {
    expect(isPlausibleExternalLabel({ x: 90, y: 90, width: 100, height: 40 }, shape)).toBe(true);
  });

  it('rejects a box far above and to the side of the shape', () => {
    expect(isPlausibleExternalLabel({ x: -89, y: -107.5, width: 88, height: 5 }, shape)).toBe(false);
  });
});
