import { describe, expect, it } from 'vitest';

import table from '../../../../rules/connection-cases.json' with { type: 'json' };
import { inferConnectorKind, validateConnection } from '../../src/rules/connection-rules.js';
import type { ConnectorKind, Endpoint } from '../../src/rules/endpoint-facts.js';

/**
 * The connection-rule cases both runtimes share.
 *
 * The C# evaluator (`BpmnConnectionRules`) runs the same file in `tests/Bpmn.Tests`, so a case whose
 * outcome differs between the canvas and an agent tool fails in at least one of the two suites.
 * Expectations are exact: a rule that starts firing on a case, or stops, has to be reflected in the
 * table, deliberately, in the same change.
 */
interface Case {
  readonly name: string;
  readonly kind: ConnectorKind;
  readonly source: Endpoint;
  readonly target: Endpoint;
  readonly isConditional: boolean;
  readonly isDefault: boolean;
  readonly violations: readonly string[];
  readonly inferredKind: ConnectorKind;
}

const cases = (table as { readonly cases: readonly Case[] }).cases;

const outcome = (entry: Case) => ({
  violations: validateConnection(entry.kind, entry.source, entry.target, {
    isConditional: entry.isConditional,
    isDefault: entry.isDefault,
  }).violations.map((violation) => violation.id),
  inferredKind: inferConnectorKind(entry.source, entry.target),
});

describe('shared connection cases', () => {
  it.each(cases.map((entry) => [entry.name, entry] as const))('%s', (_name, entry) => {
    expect(outcome(entry)).toEqual({ violations: entry.violations, inferredKind: entry.inferredKind });
  });
});
