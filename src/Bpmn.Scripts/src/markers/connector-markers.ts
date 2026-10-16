/**
 * Connector line endings (specification section 4, "defs" table).
 *
 * Expressed as JointJS marker descriptors rather than hand-authored defs/marker
 * elements. JointJS already owns marker generation, orientation and per-paper id
 * scoping; reimplementing it would collide ids across multiple papers on one page for
 * no gain (implementation plan section 2, JointJS OSS reuse policy).
 *
 * Marker coordinates run backwards along the line from the endpoint, which is why every
 * arrowhead below starts at x=10 and closes at the origin.
 */

export interface MarkerSpec {
  readonly type: string;
  readonly [attribute: string]: unknown;
}

/** Sequence Flow: solid filled triangle. */
export const sequenceFlowArrow: MarkerSpec = {
  type: 'path',
  d: 'M 10 -5 0 0 10 5 z',
  fill: 'var(--bpmn-stroke)',
  stroke: 'none',
};

/** Message Flow: open, unfilled arrowhead. */
export const messageFlowArrow: MarkerSpec = {
  type: 'path',
  d: 'M 10 -5 0 0 10 5',
  fill: 'none',
  stroke: 'var(--bpmn-stroke)',
  'stroke-width': 1.2,
};

/** Message Flow source: small hollow circle. */
export const messageFlowCircle: MarkerSpec = {
  type: 'circle',
  r: 4,
  cx: 4,
  fill: 'var(--bpmn-fill)',
  stroke: 'var(--bpmn-stroke)',
  'stroke-width': 1,
};

/** Conditional Sequence Flow source: unfilled diamond. */
export const conditionalDiamond: MarkerSpec = {
  type: 'path',
  d: 'M 0 0 10 -5 20 0 10 5 z',
  fill: 'var(--bpmn-fill)',
  stroke: 'var(--bpmn-stroke)',
  'stroke-width': 1,
};

/** Association / Data Association: open thin arrow. */
export const associationArrow: MarkerSpec = {
  type: 'path',
  d: 'M 10 -5 0 0 10 5',
  fill: 'none',
  stroke: 'var(--bpmn-stroke)',
  'stroke-width': 1,
};

/**
 * The default-flow slash: a short tick crossing the flow near its source (section 4).
 *
 * An extra child of the link markup rather than a marker, because a source marker would sit at
 * the very start of the line where the tick has to stand a little way along it. The renderer
 * positions it with atConnectionLength, which also rotates it with the line so the tick keeps
 * crossing the flow at 45 degrees whatever direction the flow runs.
 */
export const defaultFlowSlash = {
  d: 'M -5 5 L 5 -5',
  stroke: 'var(--bpmn-stroke)',
  'stroke-width': 1.5,
  fill: 'none',
} as const;
