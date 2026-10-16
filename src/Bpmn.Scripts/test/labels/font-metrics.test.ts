// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';

import {
  CanvasFontMetrics,
  FALLBACK_FONT_STACK,
  resolveFontFamily,
  type LabelFontStyle,
} from '../../src/labels/font-metrics.js';
import * as constants from '../../src/geometry/constants.js';

/**
 * Specification section 4.5 wants wrapping to be deterministic and identical between the inline
 * editor and committed SVG. It was neither, for a reason nothing could report: the label style
 * carries `var(--bpmn-font-family)`, which is a perfectly good CSS value and an invalid
 * `CanvasRenderingContext2D.font`. The assignment was rejected silently, the context stayed on its
 * default `10px sans-serif`, and every label measured 15.8% narrow — so every label wrapped early.
 *
 * These tests are about the shape of that bug rather than about any particular width: nothing
 * containing `var(` may reach the canvas, whatever the theme says.
 */
const style = (fontFamily: string): LabelFontStyle => ({
  fontFamily,
  fontSize: 12,
  fontWeight: '400',
  lineHeight: 1.2,
});

/** A context that records what it was asked to measure with, and rejects `var()` as a browser does. */
function recordingContext(): { context: CanvasRenderingContext2D; fonts: string[] } {
  const fonts: string[] = [];
  let current = '10px sans-serif';

  const context = {
    get font() {
      return current;
    },
    set font(value: string) {
      // The browser parses the shorthand and ignores an assignment it cannot parse, which is the
      // whole reason the defect was invisible.
      if (value.includes('var(')) return;
      current = value;
      fonts.push(value);
    },
    measureText: (text: string) => ({ width: text.length * 7 }),
  } as unknown as CanvasRenderingContext2D;

  return { context, fonts };
}

describe('resolveFontFamily', () => {
  it('leaves a real font stack alone', () => {
    expect(resolveFontFamily('"Segoe UI", sans-serif')).toBe('"Segoe UI", sans-serif');
  });

  it('reads the custom property off the scope it is given', () => {
    // The theme declares its variables on .bpmn-canvas, never on :root, so the scope is the point.
    const scope = document.createElement('div');
    scope.style.setProperty('--bpmn-font-family', 'Inter, sans-serif');
    document.body.append(scope);

    expect(resolveFontFamily('var(--bpmn-font-family)', scope)).toBe('Inter, sans-serif');
  });

  it('uses the declared fallback when the property is not set', () => {
    expect(resolveFontFamily('var(--not-declared, Arial)', document.createElement('div'))).toBe('Arial');
  });

  it('resolves a fallback that is itself a reference', () => {
    const scope = document.createElement('div');
    scope.style.setProperty('--secondary', 'Georgia, serif');
    document.body.append(scope);

    expect(resolveFontFamily('var(--missing, var(--secondary))', scope)).toBe('Georgia, serif');
  });

  it('falls back to the theme stack when nothing resolves', () => {
    expect(resolveFontFamily('var(--nothing-at-all)', document.createElement('div')))
      .toBe(FALLBACK_FONT_STACK);
  });
});

describe('CanvasFontMetrics', () => {
  it('never hands the canvas a font containing var()', () => {
    // The regression, stated as plainly as it can be: this is the assignment the browser threw
    // away, and throwing it away cost 15.8% of every measurement.
    const { context, fonts } = recordingContext();
    const scope = document.createElement('div');
    scope.style.setProperty('--bpmn-font-family', 'Inter, sans-serif');
    document.body.append(scope);

    new CanvasFontMetrics(scope, context).measure('Review the application', style(constants.LABEL_FONT_FAMILY));

    expect(fonts).toEqual(['400 12px Inter, sans-serif']);
  });

  it('measures the default label style at the size it was asked for', () => {
    const { context } = recordingContext();
    const scope = document.createElement('div');
    scope.style.setProperty('--bpmn-font-family', 'Inter, sans-serif');
    document.body.append(scope);

    const metrics = new CanvasFontMetrics(scope, context);

    // 7px per character from the stub, so a real measurement rather than the rejected-assignment
    // path, which would have measured at 10px sans-serif and returned something else entirely.
    expect(metrics.measure('abcd', style(constants.LABEL_FONT_FAMILY))).toBe(28);
    expect(context.font).toBe('400 12px Inter, sans-serif');
  });

  it('caches per resolved font, not per requested one', () => {
    const { context, fonts } = recordingContext();
    const metrics = new CanvasFontMetrics(document.createElement('div'), context);

    metrics.measure('once', style(constants.LABEL_FONT_FAMILY));
    metrics.measure('once', style(constants.LABEL_FONT_FAMILY));

    expect(fonts).toHaveLength(1);
  });

  it('still measures when there is no context at all', () => {
    // Server prerender: layout has to stay finite, and the client re-measures before paint.
    const metrics = new CanvasFontMetrics(null, null);

    expect(metrics.measure('abcd', style(constants.LABEL_FONT_FAMILY))).toBeGreaterThan(0);
  });
});
