import type { dia } from '@joint/core';

import type { BoundsContract } from '../model/contracts.js';
import type { FontMetricsProvider } from '../labels/font-metrics.js';
import { anchorPointOf, toLabelAttrs, type LabelAttrs } from '../labels/label-markup.js';
import { layoutLabel, type LabelStyle } from '../labels/label-layout.js';

/**
 * Metadata every BPMN cell carries, so a view or tool can get back to the semantic
 * element without a second lookup table.
 *
 * The semantic model is canonical and the cell is a projection of it, never the reverse
 * (implementation plan section 2) — which is why cell ids are the BPMN element ids and
 * this bag holds references, not copies of editable state.
 */
export interface BpmnCellMetadata {
  readonly elementId: string;
  readonly elementType: string;
  readonly containerId: string | null;
  readonly poolId: string | null;
  /** Flow nodes only: the trigger/result type the connection rules read (MF-007/MF-008). */
  readonly eventDefinition?: string;
  /** Flow nodes only: a compensation handler, reachable by Association rather than Sequence Flow. */
  readonly isForCompensation?: boolean;
}

/** The subset of cell attributes the shape factories set. */
export interface BpmnCellAttributes {
  readonly bpmn: BpmnCellMetadata;
}

/**
 * Lays out a label in element-local coordinates and returns the JointJS text attrs.
 *
 * Element attrs are expressed relative to the element origin, so the label box passed in
 * must be local too; the same local box is what the M4 inline editor will position
 * itself over, which is how editor and rendered text stay metric-identical
 * (specification section 4.5).
 */
export function labelAttrsFor(
  text: string,
  localBox: BoundsContract,
  style: LabelStyle,
  metrics: FontMetricsProvider,
): LabelAttrs {
  const layout = layoutLabel(text, localBox, metrics, style);
  return toLabelAttrs(layout, anchorPointOf(layout));
}

/** Local (element-origin-relative) box for a shape of the given size. */
export function localBox(size: { width: number; height: number }): BoundsContract {
  return { x: 0, y: 0, width: size.width, height: size.height };
}

/**
 * Asserts the invariant the whole projection rests on: a cell's id is its BPMN element
 * id. Violating it silently would break selection, command routing and DI writeback.
 */
export function assertCellIdentity(cell: dia.Cell, elementId: string): void {
  if (cell.id !== elementId) {
    throw new Error(
      `BPMN cell identity violated: cell '${String(cell.id)}' must use the BPMN element id '${elementId}'.`,
    );
  }
}
