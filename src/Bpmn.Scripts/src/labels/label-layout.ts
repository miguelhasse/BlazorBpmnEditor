import * as constants from '../geometry/constants.js';
import type { BoundsContract } from '../model/contracts.js';

import type { FontMetricsProvider, LabelFontStyle } from './font-metrics.js';
import { wrapText } from './word-wrap.js';

export type HorizontalAlign = 'start' | 'middle' | 'end';
export type VerticalAlign = 'top' | 'middle' | 'bottom';

export interface LabelStyle extends LabelFontStyle {
  readonly horizontalAlign: HorizontalAlign;
  readonly verticalAlign: VerticalAlign;
  readonly paddingX: number;
  readonly paddingY: number;
  /** Fixed boxes clip; auto-sized boxes grow vertically instead (section 4.5). */
  readonly clipOverflow: boolean;
}

export const defaultLabelStyle: LabelStyle = {
  fontFamily: constants.LABEL_FONT_FAMILY,
  fontSize: constants.LABEL_FONT_SIZE,
  fontWeight: 'normal',
  lineHeight: constants.LABEL_LINE_HEIGHT,
  horizontalAlign: 'middle',
  verticalAlign: 'middle',
  paddingX: constants.LABEL_PADDING_X,
  paddingY: constants.LABEL_PADDING_Y,
  clipOverflow: false,
};

/** The computed result: explicit lines, never browser-decided wrapping. */
export interface LabelLayout {
  readonly lines: readonly string[];
  readonly lineHeight: number;
  /** The box the lines occupy, after vertical growth for an auto-sized label. */
  readonly bounds: BoundsContract;
  /** True when a fixed box cannot show every line; the full text is still intact. */
  readonly clipped: boolean;
  readonly style: LabelStyle;
}

/**
 * Lays out a label into explicit lines (specification section 4.5).
 *
 * The full semantic string is never shortened here. A fixed imported BPMNLabel.Bounds
 * that cannot fit its text is reported as clipped and the renderer may visually
 * ellipsise it; accessibility, tooltips and property inspection keep the whole string.
 */
export function layoutLabel(
  text: string,
  box: BoundsContract,
  metrics: FontMetricsProvider,
  style: LabelStyle = defaultLabelStyle,
): LabelLayout {
  const innerWidth = Math.max(0, box.width - style.paddingX * 2);
  const lines = wrapText(text, innerWidth, style, metrics);
  const lineHeight = metrics.lineHeight(style);
  const requiredHeight = lines.length * lineHeight + style.paddingY * 2;

  const clipped = style.clipOverflow && requiredHeight > box.height;
  const height = style.clipOverflow ? box.height : Math.max(box.height, requiredHeight);

  return {
    lines,
    lineHeight,
    bounds: { x: box.x, y: box.y + verticalOffset(style, box.height, height), width: box.width, height },
    clipped,
    style,
  };
}

/**
 * Keeps a grown auto-sized box anchored the way its alignment implies: a label below a
 * shape grows downward, a centred one grows symmetrically.
 */
function verticalOffset(style: LabelStyle, originalHeight: number, grownHeight: number): number {
  const growth = grownHeight - originalHeight;
  if (growth <= 0) return 0;

  switch (style.verticalAlign) {
    case 'top':
      return 0;
    case 'middle':
      return -growth / 2;
    case 'bottom':
      return -growth;
  }
}
