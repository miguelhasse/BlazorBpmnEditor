import type { RuleFacts } from './predicate.js';

/**
 * Turns a connector endpoint into the flat facts the rule table reads.
 *
 * The table addresses facts by dotted path — `source.kind`, `target.category` — rather than by
 * walking a model, so the same predicate can be evaluated in C# against the semantic model and
 * here against a JointJS cell. This module is the one place that knows how a BPMN element maps
 * onto those names, which is what keeps the two sides from disagreeing about what `kind` means.
 */

/** What a connector can be attached to. */
export interface Endpoint {
  readonly id: string;
  /** The BPMN element type, or a pseudo-type for the non-flow-node endpoints. */
  readonly elementType: string;
  /** The Process or Sub-Process that owns it — decides Sequence Flow legality. */
  readonly containerId: string | null;
  /** The Pool that draws it — decides Message Flow legality. Never conflated with the container. */
  readonly poolId: string | null;
  readonly eventDefinitionType: string | null;
  readonly isForCompensation: boolean;
}

/** The connector being drawn. */
export type ConnectorKind = 'sequenceFlow' | 'messageFlow' | 'association' | 'dataAssociation';

/**
 * The coarse classification the table branches on first.
 *
 * Deliberately coarser than the element type: most rules care that something is an Activity, not
 * which of the ten Activity types it is.
 */
export type EndpointKind = 'event' | 'activity' | 'gateway' | 'pool' | 'lane' | 'data' | 'artifact' | 'unknown';

const EVENTS = new Set<string>([
  'startEvent', 'endEvent', 'intermediateCatchEvent', 'intermediateThrowEvent', 'boundaryEvent',
]);

const ACTIVITIES = new Set<string>([
  'task', 'userTask', 'serviceTask', 'scriptTask', 'manualTask', 'businessRuleTask', 'sendTask',
  'receiveTask', 'subProcess', 'eventSubProcess', 'transaction', 'adHocSubProcess', 'callActivity',
]);

const GATEWAYS = new Set<string>([
  'exclusiveGateway', 'inclusiveGateway', 'parallelGateway', 'complexGateway', 'eventBasedGateway',
]);

const DATA = new Set<string>(['dataObjectReference', 'dataStoreReference']);
const ARTIFACTS = new Set<string>(['textAnnotation', 'group']);

export function kindOf(elementType: string): EndpointKind {
  if (EVENTS.has(elementType)) return 'event';
  if (ACTIVITIES.has(elementType)) return 'activity';
  if (GATEWAYS.has(elementType)) return 'gateway';
  if (DATA.has(elementType)) return 'data';
  if (ARTIFACTS.has(elementType)) return 'artifact';
  if (elementType === 'pool') return 'pool';
  if (elementType === 'lane') return 'lane';

  return 'unknown';
}

/**
 * Which flavour of Event, which the rules distinguish constantly.
 *
 * `null` for anything that is not an Event, so a rule comparing `source.category` to `'boundary'`
 * cannot accidentally match a Task.
 */
export function categoryOf(elementType: string): string | null {
  switch (elementType) {
    case 'startEvent': return 'start';
    case 'endEvent': return 'end';
    case 'intermediateCatchEvent': return 'intermediateCatch';
    case 'intermediateThrowEvent': return 'intermediateThrow';
    case 'boundaryEvent': return 'boundary';
    default: return null;
  }
}

/** Which Sub-Process kind, since an Event Sub-Process has no external Sequence Flow at all. */
export function subProcessKindOf(elementType: string): string | null {
  switch (elementType) {
    case 'subProcess': return 'embedded';
    case 'eventSubProcess': return 'eventSubProcess';
    case 'transaction': return 'transaction';
    case 'adHocSubProcess': return 'adHoc';
    case 'callActivity': return 'callActivity';
    default: return null;
  }
}

/**
 * The Gateway type, for the rules about Event-Based and Parallel Gateways.
 *
 * In the table's vocabulary — `eventBased`, `parallel`, `exclusive` — not the element type: a rule
 * comparing against `eventBasedGateway` would never fire, and a refusal that never fires looks
 * exactly like one that is passing.
 */
export function gatewayTypeOf(elementType: string): string | null {
  return GATEWAYS.has(elementType) ? elementType.slice(0, -'Gateway'.length) : null;
}

/** The Activity type, for the rules that single out one — a Receive Task after an Event-Based Gateway. */
export function taskTypeOf(elementType: string): string | null {
  return ACTIVITIES.has(elementType) ? elementType : null;
}

/** The artifact type, for the Association rules. */
export function artifactTypeOf(elementType: string): string | null {
  return ARTIFACTS.has(elementType) || DATA.has(elementType) ? elementType : null;
}

/** Edge-level facts that are not about either endpoint. */
export interface ConnectorFacts {
  readonly isConditional?: boolean;
  readonly isDefault?: boolean;
}

/** Flattens both endpoints and the edge into the dotted paths the table addresses. */
export function factsFor(
  source: Endpoint,
  target: Endpoint,
  connector: ConnectorFacts = {},
): RuleFacts {
  return {
    ...sideFacts('source', source),
    ...sideFacts('target', target),
    isConditional: connector.isConditional ?? false,
    isDefault: connector.isDefault ?? false,
  };
}

function sideFacts(side: 'source' | 'target', endpoint: Endpoint): RuleFacts {
  return {
    [`${side}.id`]: endpoint.id,
    [`${side}.elementType`]: endpoint.elementType,
    [`${side}.kind`]: kindOf(endpoint.elementType),
    [`${side}.category`]: categoryOf(endpoint.elementType),
    [`${side}.subProcessKind`]: subProcessKindOf(endpoint.elementType),
    [`${side}.gatewayType`]: gatewayTypeOf(endpoint.elementType),
    [`${side}.taskType`]: taskTypeOf(endpoint.elementType),
    [`${side}.artifactType`]: artifactTypeOf(endpoint.elementType),
    [`${side}.containerId`]: endpoint.containerId,
    [`${side}.poolId`]: endpoint.poolId,
    [`${side}.eventDefinitionType`]: endpoint.eventDefinitionType,
    [`${side}.isForCompensation`]: endpoint.isForCompensation,
  };
}

/**
 * Builds an endpoint with the defaults filled in.
 *
 * The type stays a plain string rather than the model's union: the non-flow-node endpoints — a
 * Pool, a Lane, an artifact — are not in that union, and widening it back to `string` for them
 * would make the annotation decorative.
 */
export function endpointOf(
  id: string,
  type: string,
  overrides: Partial<Omit<Endpoint, 'id' | 'elementType'>> = {},
): Endpoint {
  return {
    id,
    elementType: type,
    containerId: overrides.containerId ?? null,
    poolId: overrides.poolId ?? null,
    eventDefinitionType: overrides.eventDefinitionType ?? null,
    isForCompensation: overrides.isForCompensation ?? false,
  };
}
