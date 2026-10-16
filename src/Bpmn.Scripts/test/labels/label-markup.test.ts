import { describe, expect, it } from 'vitest';

import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { defaultLabelStyle, layoutLabel } from '../../src/labels/label-layout.js';
import { anchorPointOf, toLabelAttrs } from '../../src/labels/label-markup.js';

/**
 * Vectorizer splits the text on newlines into one tspan per line and applies lineHeight
 * directly as each tspan's dy. Handing it the ratio instead of the computed pixel height drew
 * every wrapped line on the same baseline, which looked like overlapping garbage on any label
 * long enough to wrap.
 */
const metrics = new TableFontMetrics();

describe('toLabelAttrs', () => {
  it('reports line height in pixels, not as a ratio', () => {
    const layout = layoutLabel('a label that will wrap', { x: 0, y: 0, width: 60, height: 20 }, metrics);
    const attrs = toLabelAttrs(layout, anchorPointOf(layout));

    expect(layout.lines.length).toBeGreaterThan(1);
    // 12px font x 1.2 ratio (floating point, hence the tolerance).
    expect(attrs.lineHeight).toBeCloseTo(14.4, 6);
    expect(attrs.lineHeight).not.toBe(defaultLabelStyle.lineHeight);
  });

  it('joins the computed lines with newlines and leaves wrapping off', () => {
    const layout = layoutLabel('one two three', { x: 0, y: 0, width: 60, height: 20 }, metrics);
    const attrs = toLabelAttrs(layout, anchorPointOf(layout));

    expect(attrs.textWrap).toBe(false);
    expect(attrs.text.split('\n')).toEqual([...layout.lines]);
  });
});
