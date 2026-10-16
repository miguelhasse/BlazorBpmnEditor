import { describe, expect, it } from 'vitest';

import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { createFlowNodeCell } from '../../src/shapes/registry.js';
import { flowNode, shape } from '../fixtures/documents.js';

/**
 * The semantic facts a flow-node cell carries for the connection rules.
 *
 * The connect gesture only has cells. Without the trigger on the cell, MF-008 sees every Message
 * Start Event as a None Start and refuses the Message Flow the diagram was drawn with.
 */
const context = { metrics: new TableFontMetrics() };

interface ConnectionFacts {
  readonly eventDefinition?: string;
  readonly isForCompensation?: boolean;
}

describe('flow-node cell metadata', () => {
  it('carries an Event trigger', () => {
    const cell = createFlowNodeCell(
      flowNode('Start_1', 'startEvent', { eventDefinition: 'message' }),
      shape('Start_1', 0, 0, 36, 36),
      context,
    );

    expect((cell.get('bpmn') as ConnectionFacts).eventDefinition).toBe('message');
  });

  it('carries the compensation flag of an Activity', () => {
    const cell = createFlowNodeCell(
      flowNode('Task_1', 'task', { isForCompensation: true }),
      shape('Task_1', 0, 0, 100, 80),
      context,
    );

    expect((cell.get('bpmn') as ConnectionFacts).isForCompensation).toBe(true);
  });
});
