import table from '../../../../rules/bpmn-rules.json' with { type: 'json' };
import {
  factsFor,
  type ConnectorFacts,
  type ConnectorKind,
  type Endpoint,
} from './endpoint-facts.js';
import { evaluate, PredicateError, type Predicate, type RuleFacts } from './predicate.js';

/**
 * Live connection validation, driven by the shared rule table.
 *
 * Imported as JSON at build time from the same `rules/bpmn-rules.json` the C# source generator
 * reads, so a rule cannot exist on one side only. Nothing here hard-codes a rule: adding one to
 * the table is the whole change.
 *
 * Only `scope: "live"` rules are evaluated. Document-scoped rules need to count sibling edges or
 * walk reachability, which a single connector gesture cannot answer, and they are C#'s to run
 * against the whole model.
 */

/** One rule as the table declares it. */
interface RuleEntry {
  readonly id: string;
  readonly scope: string;
  readonly connectorKind?: string;
  readonly severity: string;
  readonly message: string;
  readonly predicate?: unknown;
  readonly native?: boolean;
}

/** A rule that was violated. */
export interface RuleViolation {
  readonly id: string;
  readonly severity: 'error' | 'warning';
  readonly message: string;
}

export interface ValidationResult {
  readonly isValid: boolean;
  readonly violations: readonly RuleViolation[];
}

const ENTRIES: readonly RuleEntry[] = (table as { readonly rules: readonly RuleEntry[] }).rules;

/**
 * The live rules for one connector kind, resolved once.
 *
 * A `native: true` entry has no predicate — it reserves an id whose logic is hand-written — so it
 * is skipped here rather than treated as a rule that never fires.
 */
const liveRules = new Map<string, readonly RuleEntry[]>();

function rulesFor(kind: ConnectorKind): readonly RuleEntry[] {
  const cached = liveRules.get(kind);
  if (cached !== undefined) return cached;

  const resolved = ENTRIES.filter((entry) =>
    entry.scope === 'live'
    && entry.native !== true
    && entry.predicate !== undefined
    && entry.connectorKind === kind);

  liveRules.set(kind, resolved);
  return resolved;
}

/** Every live rule id this evaluator will consider, for tests and diagnostics. */
export function liveRuleIds(kind: ConnectorKind): readonly string[] {
  return rulesFor(kind).map((entry) => entry.id);
}

/**
 * Validates one proposed connection.
 *
 * Every violated rule is reported, not just the first. A drag that breaks three rules should say
 * so — showing one and hiding the rest means the user fixes it and is refused again.
 *
 * A malformed predicate is reported as a violation of its own rule rather than thrown: a bad
 * table entry must not make the canvas unusable, and refusing the connection is the safe way to
 * fail.
 */
export function validateConnection(
  kind: ConnectorKind,
  source: Endpoint,
  target: Endpoint,
  connector: ConnectorFacts = {},
): ValidationResult {
  const facts = factsFor(source, target, connector);
  const violations: RuleViolation[] = [];

  for (const entry of rulesFor(kind)) {
    const outcome = run(entry, facts);

    if (outcome.failure !== undefined) {
      violations.push({ id: entry.id, severity: 'error', message: outcome.failure });
      continue;
    }

    if (!outcome.violated) continue;

    violations.push({
      id: entry.id,
      severity: entry.severity === 'warning' ? 'warning' : 'error',
      message: entry.message,
    });
  }

  // Warnings are lint, not refusal: permissive mode surfaces them and still allows the edge.
  return { isValid: !violations.some((violation) => violation.severity === 'error'), violations };
}

/**
 * Evaluates one entry, turning a malformed predicate into a result rather than an exception.
 *
 * A bad table entry must not make the canvas unusable, and refusing the connection is the safe
 * way to fail.
 */
function run(entry: RuleEntry, facts: RuleFacts): { violated: boolean; failure?: string } {
  try {
    return { violated: evaluate(entry.predicate as Predicate, facts) };
  } catch (error) {
    return {
      violated: true,
      failure: error instanceof PredicateError
        ? `Rule '${entry.id}' could not be evaluated: ${error.message}`
        : `Rule '${entry.id}' could not be evaluated.`,
    };
  }
}

/**
 * The connector kind a pair of endpoints implies.
 *
 * Resolved from the endpoints rather than chosen by the user, which is what the reference
 * behaviour does and what makes a single drag gesture able to produce the right edge: two nodes in
 * different Pools mean a Message Flow, an artifact at either end means an Association.
 */
export function inferConnectorKind(source: Endpoint, target: Endpoint): ConnectorKind {
  const facts = factsFor(source, target);
  const sourceKind = facts['source.kind'];
  const targetKind = facts['target.kind'];

  if (facts['source.artifactType'] === 'textAnnotation' || facts['target.artifactType'] === 'textAnnotation') {
    return 'association';
  }

  if (sourceKind === 'data' || targetKind === 'data') return 'dataAssociation';

  if (sourceKind === 'pool' || targetKind === 'pool') return 'messageFlow';

  // Different Pools can only be joined by a Message Flow; the same Pool only by a Sequence Flow.
  const crossesPools = source.poolId !== null && target.poolId !== null && source.poolId !== target.poolId;
  if (crossesPools) return 'messageFlow';

  /*
   * Except a compensation handler: it is attached to its Compensation Boundary Event by an
   * Association, never a Sequence Flow (BPMN 2.0.2 §10.6.5, SF-008/SF-009). Only in the
   * Event-to-handler direction, because the Association is stored as drawn and points at the handler.
   */
  if (isCompensationAssociation(facts)) return 'association';

  return 'sequenceFlow';
}

function isCompensationAssociation(facts: RuleFacts): boolean {
  return facts['source.kind'] === 'event'
    && facts['source.category'] === 'boundary'
    && facts['source.eventDefinitionType'] === 'compensation'
    && facts['target.kind'] === 'activity'
    && facts['target.isForCompensation'] === true;
}
