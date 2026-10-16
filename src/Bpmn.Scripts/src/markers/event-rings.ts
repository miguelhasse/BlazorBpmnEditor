import * as constants from '../geometry/constants.js';
import type { BpmnEventContext } from '../model/element-kind.js';

/** One concentric circle of an Event's border. */
export interface EventRing {
  readonly radius: number;
  readonly strokeWidth: number;
  readonly dashArray: string | null;
}

/**
 * The ring stack for an Event (specification section 1.1 / section 4.2).
 *
 * The three distinctions this encodes are the whole of BPMN event notation:
 * Start is a single thin ring, Intermediate and Boundary are double rings, End is a
 * single thick ring, and a non-interrupting Boundary or Event Sub-Process Start dashes
 * every ring it has.
 */
export function ringsFor(context: BpmnEventContext, isInterrupting: boolean): readonly EventRing[] {
  const dash = isInterrupting ? null : constants.DASH_NON_INTERRUPTING;

  switch (context) {
    case 'start':
      return [{ radius: constants.EVENT_OUTER_RADIUS, strokeWidth: constants.STROKE_THIN, dashArray: dash }];

    case 'end':
      return [{ radius: constants.EVENT_OUTER_RADIUS, strokeWidth: constants.STROKE_THICK, dashArray: null }];

    case 'intermediateCatch':
    case 'intermediateThrow':
    case 'boundary':
      return [
        { radius: constants.EVENT_OUTER_RADIUS, strokeWidth: constants.STROKE_THIN, dashArray: dash },
        { radius: constants.EVENT_INNER_RADIUS, strokeWidth: constants.STROKE_THIN, dashArray: dash },
      ];
  }
}
