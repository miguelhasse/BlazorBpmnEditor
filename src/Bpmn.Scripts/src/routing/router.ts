import type { dia, routers } from '@joint/core';

import type { RoutingOptionsContract } from '../interop/contracts.js';
import type { ConnectorKind } from '../rules/endpoint-facts.js';
import { ORTHOGONAL_ROUTER, type OrthogonalArguments } from './live-router.js';

export type { ConnectorKind };

/**
 * A router choice, in the shape JointJS takes it.
 *
 * The whole point of naming this type is that nothing outside this folder builds one. Section 2 of
 * the implementation plan asks for "a stable internal Router abstraction so an alternative router
 * implementation could be substituted later without changing commands, BPMNDI waypoints, or public
 * interop contracts" — which only holds if the rest of the editor asks for *a route* rather than
 * for the manhattan router by name.
 */
export interface RouteSpec {
  readonly name: string;
  readonly args: routers.RouterArgumentsMap[keyof routers.RouterArgumentsMap] | OrthogonalArguments | undefined;
}

/** Which way a Pool reads, which is the axis a new route should leave and enter along. */
export type FlowAxis = 'horizontal' | 'vertical';

/**
 * The router a connector of this kind should use.
 *
 * Sequence Flow and Message Flow are drawn with right angles; Associations and Data Associations
 * are not. That is notation rather than preference — an Association is a loose tie to an annotation
 * or a data object and is conventionally drawn as the short line it is, and forcing it orthogonal
 * makes it read as process flow.
 *
 * @param kind Which connector is being drawn.
 * @param options The accepted routing configuration.
 * @param axis The dominant flow axis of the Pool the connector is in.
 * @returns The router name and its arguments, ready for a link's `router` property.
 */
export function routeFor(
  kind: ConnectorKind,
  options: RoutingOptionsContract,
  axis: FlowAxis = 'horizontal',
): RouteSpec {
  /*
   * Only Associations are straight. `options.mode` no longer decides it: a Sequence or Message Flow
   * is right-angled wherever it is drawn, moved or dragged, and the document holds it that way, so
   * a preview that was straight would show one thing and store another.
   */
  if (!isOrthogonal(kind)) return STRAIGHT;

  return {
    name: ORTHOGONAL_ROUTER,
    args: {
      separation: options.parallelSeparation,
      clearance: options.padding,
      step: options.step,
      padding: options.padding,

      /*
       * The directions a route may leave and enter by. Restricting them to the Pool's own axis is
       * what makes a new connector in a horizontal Pool read left-to-right instead of leaving
       * through the top of a shape because that happened to be one step shorter.
       *
       * It has to be a restriction, not a preference. This listed all four directions with the
       * dominant axis first, on the belief that the router searches in that order; it does not.
       * JointJS seeds every listed direction into the same open set at equal cost and only ever
       * asks whether a direction is in the list, so ordering it changed nothing at all and the
       * option did nothing for as long as it existed.
       */
      startDirections: directionsFor(axis, options.followPoolOrientation),
      endDirections: directionsFor(axis, options.followPoolOrientation, true),
    },
  };
}

/** Whether this kind of connector is drawn with right angles. */
export function isOrthogonal(kind: ConnectorKind): boolean {
  return kind === 'sequenceFlow' || kind === 'messageFlow';
}

/** No router: JointJS draws straight segments through whatever vertices the link has. */
const STRAIGHT: RouteSpec = { name: 'normal', args: undefined };

function directionsFor(
  axis: FlowAxis,
  follow: boolean,
  entering = false,
): dia.OrthogonalDirection[] {
  if (!follow) return ['left', 'right', 'top', 'bottom'];

  /*
   * Both ends of the axis, not one. A connector that doubles back — a loop to an earlier Task — has
   * to be able to leave by the side it came from, and pinning the direction to "forwards" would
   * make that route impossible rather than merely unusual.
   */
  return axis === 'horizontal'
    ? (entering ? ['left', 'right'] : ['right', 'left'])
    : (entering ? ['top', 'bottom'] : ['bottom', 'top']);
}
