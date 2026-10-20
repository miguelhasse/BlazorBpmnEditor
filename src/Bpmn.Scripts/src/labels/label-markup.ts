import type { LabelLayout } from './label-layout.js';

/** The JointJS attribute bag that renders a laid-out label. */
export interface LabelAttrs {
  readonly text: string;
  readonly textWrap: false;
  readonly textAnchor: 'start' | 'middle' | 'end';
  readonly textVerticalAnchor: 'top' | 'middle' | 'bottom';
  readonly x: number;
  readonly y: number;
  readonly fontFamily: string;
  readonly fontSize: number;
  readonly fontWeight: string;
  readonly lineHeight: number;
  /** Available wrapping width, also retained on hidden labels for the inline editor. */
  readonly 'data-bpmn-label-width': number;
  readonly annotations?: never;
}

/**
 * Turns a computed layout into JointJS text attributes.
 *
 * JointJS's own textWrap measures live DOM, which would make line breaks depend on the
 * browser and on when the element happened to be attached — incompatible with section
 * 4.5's determinism requirement and with inline-editor parity. So wrapping happens in
 * label-layout.ts and this hands JointJS pre-broken lines with textWrap off.
 *
 * Vectorizer splits the text value on newlines into one tspan.v-line per line
 * (see V.prototype.text), which is exactly the explicit line layout section 4.5 asks
 * for; no foreignObject and no browser auto-wrap is involved.
 */
export function toLabelAttrs(layout: LabelLayout, refPosition: { x: number; y: number }): LabelAttrs {
  const { style } = layout;

  return {
    text: layout.lines.join('\n'),
    textWrap: false,
    textAnchor: style.horizontalAlign,
    textVerticalAnchor: style.verticalAlign,
    x: refPosition.x,
    y: refPosition.y,
    fontFamily: style.fontFamily,
    fontSize: style.fontSize,
    fontWeight: style.fontWeight,
    // Pixels, not the ratio. Vectorizer applies this value directly as each tspan's dy, so a
    // ratio of 1.2 would space the lines 1.2px apart and draw them all on top of each other.
    // layout.lineHeight is already fontSize multiplied by the ratio, and it is the same value
    // the layout used to size the box, so box and rendering cannot disagree.
    lineHeight: layout.lineHeight,
    'data-bpmn-label-width': Math.max(0, layout.bounds.width - style.paddingX * 2),
  };
}

/**
 * The anchor point a label's text attributes should reference, derived from its
 * alignment so the text box and the laid-out box agree.
 */
export function anchorPointOf(layout: LabelLayout): { x: number; y: number } {
  const { bounds, style } = layout;

  const x =
    style.horizontalAlign === 'start'
      ? bounds.x + style.paddingX
      : style.horizontalAlign === 'end'
        ? bounds.x + bounds.width - style.paddingX
        : bounds.x + bounds.width / 2;

  const y =
    style.verticalAlign === 'top'
      ? bounds.y + style.paddingY
      : style.verticalAlign === 'bottom'
        ? bounds.y + bounds.height - style.paddingY
        : bounds.y + bounds.height / 2;

  return { x, y };
}
