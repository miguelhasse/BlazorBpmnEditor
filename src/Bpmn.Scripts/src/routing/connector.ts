import { connectors, type g } from '@joint/core';

import { roundedPath } from './rounded-path.js';

/** The connector's name in `connectorNamespace`. */
export const ROUNDED_CONNECTOR = 'bpmnRounded';

/** What the connector is told. */
export interface RoundedArguments {
  /** How far from a corner the curve starts, in diagram units. */
  readonly radius?: number;
}

function draw(
  source: g.PlainPoint,
  target: g.PlainPoint,
  route: readonly g.PlainPoint[],
  args: RoundedArguments,
): string {
  return roundedPath([source, ...route, target], args.radius ?? 0);
}

/** The connectors the paper resolves names against: JointJS's own, and this one. */
export const connectorNamespace = { ...connectors, [ROUNDED_CONNECTOR]: draw };
