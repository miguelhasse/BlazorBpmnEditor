import { describe, expect, it } from 'vitest';

import {
  inferConnectorKind,
  liveRuleIds,
  validateConnection,
} from '../../src/rules/connection-rules.js';
import { endpointOf } from '../../src/rules/endpoint-facts.js';

/**
 * Live connection validation, against the real rule table.
 *
 * Deliberately not against a fixture table: the point of `rules/bpmn-rules.json` is that one file
 * drives both runtimes, and a test with its own rules would pass while the shipped predicates were
 * wrong. Each case below names the rule it expects, so a table edit that changes an outcome fails
 * here rather than silently in a drag.
 */
const node = (id: string, type: string, container = 'Process_1', pool: string | null = null) =>
  endpointOf(id, type, { containerId: container, poolId: pool });

const violated = (result: { violations: readonly { id: string }[] }) =>
  result.violations.map((violation) => violation.id);

describe('validateConnection: Sequence Flow', () => {
  it('accepts a Start Event to a Task in the same container', () => {
    const result = validateConnection('sequenceFlow', node('a', 'startEvent'), node('b', 'userTask'));

    expect(result.violations).toEqual([]);
    expect(result.isValid).toBe(true);
  });

  it('refuses an outgoing flow from an End Event', () => {
    const result = validateConnection('sequenceFlow', node('a', 'endEvent'), node('b', 'userTask'));

    expect(result.isValid).toBe(false);
    expect(violated(result)).toContain('SF-002');
  });

  it('refuses an incoming flow to a Start Event', () => {
    const result = validateConnection('sequenceFlow', node('a', 'userTask'), node('b', 'startEvent'));

    expect(violated(result)).toContain('SF-003');
  });

  it('refuses a Boundary Event as a target', () => {
    const result = validateConnection('sequenceFlow', node('a', 'userTask'), node('b', 'boundaryEvent'));

    expect(violated(result)).toContain('SF-004');
  });

  it('refuses a flow that crosses a container boundary', () => {
    // SF-007: the container decides Sequence Flow legality, never the Pool.
    const result = validateConnection(
      'sequenceFlow',
      node('a', 'userTask', 'Process_1'),
      node('b', 'userTask', 'Sub_1'),
    );

    expect(result.isValid).toBe(false);
    expect(violated(result).some((id) => id.startsWith('SF-'))).toBe(true);
  });

  it('refuses an Event Sub-Process at either end', () => {
    const asSource = validateConnection('sequenceFlow', node('a', 'eventSubProcess'), node('b', 'userTask'));
    const asTarget = validateConnection('sequenceFlow', node('a', 'userTask'), node('b', 'eventSubProcess'));

    expect(violated(asSource)).toContain('SF-005');
    expect(violated(asTarget)).toContain('SF-005');
  });

  it('refuses an artifact as an endpoint', () => {
    const result = validateConnection('sequenceFlow', node('a', 'textAnnotation'), node('b', 'userTask'));

    expect(violated(result)).toContain('SF-001');
  });

  it('reports every violated rule, not only the first', () => {
    // An End Event to a Start Event breaks both direction rules. Showing one and hiding the rest
    // means the user fixes it and is refused again.
    const result = validateConnection('sequenceFlow', node('a', 'endEvent'), node('b', 'startEvent'));

    expect(violated(result)).toContain('SF-002');
    expect(violated(result)).toContain('SF-003');
  });
});

describe('validateConnection: Message Flow', () => {
  it('accepts two Activities in different Pools', () => {
    const result = validateConnection(
      'messageFlow',
      node('a', 'userTask', 'Process_A', 'Pool_A'),
      node('b', 'userTask', 'Process_B', 'Pool_B'),
    );

    expect(result.violations).toEqual([]);
  });

  it('refuses two endpoints in the same Pool', () => {
    const result = validateConnection(
      'messageFlow',
      node('a', 'userTask', 'Process_A', 'Pool_A'),
      node('b', 'userTask', 'Process_A', 'Pool_A'),
    );

    expect(result.isValid).toBe(false);
    expect(violated(result)).toContain('MF-002');
  });

  it('refuses a Gateway as an endpoint', () => {
    const result = validateConnection(
      'messageFlow',
      node('a', 'exclusiveGateway', 'Process_A', 'Pool_A'),
      node('b', 'userTask', 'Process_B', 'Pool_B'),
    );

    expect(violated(result)).toContain('MF-001');
  });

  it('does not refuse two endpoints whose Pool is simply unknown', () => {
    // Absent is not a value. Treating two missing Pool ids as "the same Pool" would make MF-002
    // fire on every pair of shapes that has not been placed yet.
    const result = validateConnection('messageFlow', node('a', 'userTask'), node('b', 'userTask'));

    expect(violated(result)).not.toContain('MF-002');
  });

  const event = (id: string, type: string, definition: string, pool: string) =>
    endpointOf(id, type, { containerId: `Process_${pool}`, poolId: pool, eventDefinitionType: definition });

  it.each([
    ['startEvent', 'message'],
    ['intermediateCatchEvent', 'message'],
    ['boundaryEvent', 'message'],
    ['startEvent', 'multiple'],
    ['intermediateCatchEvent', 'parallelMultiple'],
  ])('accepts a Task sending to a %s with a %s trigger', (type, definition) => {
    const result = validateConnection(
      'messageFlow',
      node('a', 'task', 'Process_A', 'Pool_A'),
      event('b', type, definition, 'Pool_B'),
    );

    expect(result.violations).toEqual([]);
  });

  it.each([
    ['startEvent', 'none'],
    ['intermediateCatchEvent', 'timer'],
    ['intermediateThrowEvent', 'message'],
  ])('refuses a %s with a %s definition as the receiving end', (type, definition) => {
    const result = validateConnection(
      'messageFlow',
      node('a', 'task', 'Process_A', 'Pool_A'),
      event('b', type, definition, 'Pool_B'),
    );

    expect(violated(result)).toContain('MF-008');
  });

  it.each([
    ['endEvent', 'message'],
    ['intermediateThrowEvent', 'message'],
    ['intermediateThrowEvent', 'multiple'],
  ])('accepts a %s with a %s result sending to a Task', (type, definition) => {
    const result = validateConnection(
      'messageFlow',
      event('a', type, definition, 'Pool_A'),
      node('b', 'task', 'Process_B', 'Pool_B'),
    );

    expect(result.violations).toEqual([]);
  });

  it('refuses a None End Event as the sending end', () => {
    const result = validateConnection(
      'messageFlow',
      event('a', 'endEvent', 'none', 'Pool_A'),
      node('b', 'task', 'Process_B', 'Pool_B'),
    );

    expect(violated(result)).toContain('MF-007');
  });
});

describe('validateConnection: Event-Based Gateway and Link Events', () => {
  const typed = (id: string, type: string, definition: string) =>
    endpointOf(id, type, { containerId: 'Process_1', eventDefinitionType: definition });

  it('accepts a Receive Task after an Event-Based Gateway', () => {
    const result = validateConnection('sequenceFlow', node('a', 'eventBasedGateway'), node('b', 'receiveTask'));

    expect(result.violations).toEqual([]);
  });

  it('accepts a Message catch after an Event-Based Gateway', () => {
    const result = validateConnection(
      'sequenceFlow',
      node('a', 'eventBasedGateway'),
      typed('b', 'intermediateCatchEvent', 'message'),
    );

    expect(result.violations).toEqual([]);
  });

  it('refuses a plain Task after an Event-Based Gateway', () => {
    const result = validateConnection('sequenceFlow', node('a', 'eventBasedGateway'), node('b', 'userTask'));

    expect(violated(result)).toContain('SF-011');
  });

  it('refuses an outgoing Sequence Flow from a Link throw', () => {
    const result = validateConnection('sequenceFlow', typed('a', 'intermediateThrowEvent', 'link'), node('b', 'task'));

    expect(violated(result)).toContain('SF-017');
  });

  it('refuses an incoming Sequence Flow to a Link catch', () => {
    const result = validateConnection('sequenceFlow', node('a', 'task'), typed('b', 'intermediateCatchEvent', 'link'));

    expect(violated(result)).toContain('SF-018');
  });

  it('accepts flow into a Link throw and out of a Link catch', () => {
    expect(validateConnection('sequenceFlow', node('a', 'task'), typed('b', 'intermediateThrowEvent', 'link')).isValid)
      .toBe(true);
    expect(validateConnection('sequenceFlow', typed('a', 'intermediateCatchEvent', 'link'), node('b', 'task')).isValid)
      .toBe(true);
  });
});

describe('validateConnection: Data Association', () => {
  it('accepts a Data Object Reference into an Activity', () => {
    const result = validateConnection(
      'dataAssociation',
      node('a', 'dataObjectReference'),
      node('b', 'userTask'),
    );

    expect(result.isValid).toBe(true);
  });

  it('refuses a Text Annotation, which connects only by Association', () => {
    const result = validateConnection('dataAssociation', node('a', 'textAnnotation'), node('b', 'userTask'));

    expect(violated(result)).toContain('DA-003');
  });
});

describe('the table itself', () => {
  it('contributes live rules for every connector kind', () => {
    // A kind with no rules would validate everything, which is worse than having no validation at
    // all because it looks enforced.
    expect(liveRuleIds('sequenceFlow').length).toBeGreaterThan(0);
    expect(liveRuleIds('messageFlow').length).toBeGreaterThan(0);
    expect(liveRuleIds('dataAssociation').length).toBeGreaterThan(0);
  });

  it('evaluates every live rule without a predicate error on a plausible connection', () => {
    // A malformed predicate is reported as a violation of its own rule. Asserting the clean case
    // produces none is what proves the whole table is actually interpretable by this evaluator.
    const result = validateConnection('sequenceFlow', node('a', 'startEvent'), node('b', 'userTask'));

    expect(result.violations.filter((v) => v.message.includes('could not be evaluated'))).toEqual([]);
  });
});

describe('inferConnectorKind', () => {
  it('picks a Sequence Flow within one Pool', () => {
    expect(inferConnectorKind(
      node('a', 'userTask', 'Process_A', 'Pool_A'),
      node('b', 'userTask', 'Process_A', 'Pool_A'),
    )).toBe('sequenceFlow');
  });

  it('picks a Message Flow across Pools', () => {
    expect(inferConnectorKind(
      node('a', 'userTask', 'Process_A', 'Pool_A'),
      node('b', 'userTask', 'Process_B', 'Pool_B'),
    )).toBe('messageFlow');
  });

  it('picks a Message Flow when either end is a Pool itself', () => {
    // A black-box Participant is a valid Message Flow endpoint and nothing else.
    expect(inferConnectorKind(node('a', 'userTask'), node('b', 'pool'))).toBe('messageFlow');
  });

  it('picks an Association for a Text Annotation', () => {
    expect(inferConnectorKind(node('a', 'textAnnotation'), node('b', 'userTask'))).toBe('association');
  });

  it('picks a Data Association for a data reference', () => {
    expect(inferConnectorKind(node('a', 'dataObjectReference'), node('b', 'userTask')))
      .toBe('dataAssociation');
  });

  it('prefers an Association over a Data Association for an annotation on data', () => {
    // A Text Annotation never connects by Data Association (DA-003), so the annotation wins.
    expect(inferConnectorKind(node('a', 'textAnnotation'), node('b', 'dataObjectReference')))
      .toBe('association');
  });

  it('picks an Association from a Compensation Boundary Event to its handler', () => {
    const boundary = endpointOf('a', 'boundaryEvent', { containerId: 'Process_1', eventDefinitionType: 'compensation' });
    const handler = endpointOf('b', 'task', { containerId: 'Process_1', isForCompensation: true });

    expect(inferConnectorKind(boundary, handler)).toBe('association');
    expect(validateConnection('association', boundary, handler).isValid).toBe(true);
    // Reversed, it is a Sequence Flow into a handler, which SF-009 refuses.
    expect(inferConnectorKind(handler, boundary)).toBe('sequenceFlow');
  });

  it('keeps a Sequence Flow from a non-compensation Boundary Event', () => {
    const boundary = endpointOf('a', 'boundaryEvent', { containerId: 'Process_1', eventDefinitionType: 'timer' });

    expect(inferConnectorKind(boundary, node('b', 'task'))).toBe('sequenceFlow');
  });
});
