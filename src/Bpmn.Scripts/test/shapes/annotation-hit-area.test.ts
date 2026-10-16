import { describe, expect, it } from 'vitest';

import { OUTLINE_HIT_TOLERANCE } from '../../src/geometry/constants.js';
import { BpmnTextAnnotationElement } from '../../src/shapes/elements.js';

/**
 * A Text Annotation is a thin unfilled bracket in an otherwise empty box, so without a hit area it
 * took a pixel-exact press to grab one and a miss panned the view. These pin the hit areas the way
 * the Group's band is pinned: a wide transparent stroke on the outline, and the text as a block.
 */
describe('Text Annotation hit areas', () => {
  const cell = new BpmnTextAnnotationElement();
  const markup = (cell as unknown as { markup: readonly { readonly selector?: string }[] }).markup;
  const selectors = markup.map((node) => node.selector);

  it('puts a tolerance band under the bracket', () => {
    expect(selectors.indexOf('hitArea')).toBeGreaterThanOrEqual(0);
    expect(selectors.indexOf('hitArea')).toBeLessThan(selectors.indexOf('bracket'));

    const hit = cell.attr('hitArea') as Record<string, unknown>;
    expect(hit.stroke).toBe('transparent');
    expect(hit.strokeWidth).toBe(OUTLINE_HIT_TOLERANCE * 2);
    expect(hit['pointer-events']).toBe('stroke');
  });

  it('hits the text as one block, sized to the label', () => {
    const text = cell.attr('textHitArea') as Record<string, unknown>;
    expect(text.ref).toBe('label');
    expect(text.refWidth).toBe('100%');
    expect(text.refHeight).toBe('100%');
    expect(text['pointer-events']).toBe('fill');
  });

  it('leaves the visible bracket inert, so the band alone decides', () => {
    expect((cell.attr('bracket') as Record<string, unknown>)['pointer-events']).toBe('none');
  });
});
