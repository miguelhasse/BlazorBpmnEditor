import { describe, expect, it } from 'vitest';

import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { createFlowNodeCell } from '../../src/shapes/registry.js';
import { flowNode, shape } from '../fixtures/documents.js';
import type { BpmnFlowNodeType } from '../../src/model/contracts.js';

/**
 * How a Sub-Process is drawn in each of its two states (specification section 1.2).
 *
 * Collapsed it is a Task: a rounded rectangle with its name in the middle and a "+" along the
 * bottom edge. Expanded it is a container, and this editor draws no name on one at all — a
 * deliberate departure from section 1.2.1, which assumes there is a label to double-click. The
 * name stays in the model, the property panel and the accessible tree; it is only not painted,
 * because an expanded container's interior belongs to its children.
 */
const context = { metrics: new TableFontMetrics() };

interface LabelAttrs {
  readonly display?: string;
  readonly text?: string;
  readonly refY?: unknown;
}

const labelOf = (type: BpmnFlowNodeType, isExpanded: boolean | null, name: string | null = 'Handle order'): LabelAttrs =>
  createFlowNodeCell(
    flowNode('Sub_1', type, { name }),
    shape('Sub_1', 100, 100, 350, 200, { isExpanded }),
    context,
  ).attr('label') as LabelAttrs;

describe('a collapsed Sub-Process', () => {
  it('draws its name', () => {
    expect(labelOf('subProcess', false).text).toBe('Handle order');
  });

  it('and draws it in the middle, where a Task draws one', () => {
    const collapsed = labelOf('subProcess', false);
    const task = createFlowNodeCell(
      flowNode('Task_1', 'task', { name: 'Handle order' }),
      shape('Task_1', 100, 100, 350, 200),
      context,
    ).attr('label') as LabelAttrs;

    expect(collapsed.display).toBe(task.display);
    expect(collapsed.refY).toBe(task.refY);
  });

  it('and says it is collapsed on the cell', () => {
    const cell = createFlowNodeCell(
      flowNode('Sub_1', 'subProcess'),
      shape('Sub_1', 100, 100, 350, 200, { isExpanded: false }),
      context,
    );

    expect((cell.get('bpmn') as { isExpanded: boolean | null }).isExpanded).toBe(false);
  });
});

describe('an expanded Sub-Process', () => {
  it('draws no name', () => {
    expect(labelOf('subProcess', true).display).toBe('none');
  });

  it.each(['transaction', 'adHocSubProcess', 'eventSubProcess'] as const)(
    'and neither does an expanded %s',
    (type) => {
      expect(labelOf(type, true).display).toBe('none');
    },
  );

  it('and says it is expanded on the cell', () => {
    const cell = createFlowNodeCell(
      flowNode('Sub_1', 'subProcess'),
      shape('Sub_1', 100, 100, 350, 200, { isExpanded: true }),
      context,
    );

    expect((cell.get('bpmn') as { isExpanded: boolean | null }).isExpanded).toBe(true);
  });
});

describe('everything else', () => {
  it('still draws its name, however big it is', () => {
    // The mutation guard. Hiding the label is keyed on being an expanded Sub-Process, not on the
    // shape being large, and a Task the size of a container must not lose its name.
    expect(labelOf('task', null).text).toBe('Handle order');
    expect(labelOf('task', null).display).not.toBe('none');
  });

  it('and a Call Activity keeps its name whatever its shape says', () => {
    // It is never expandable — its content belongs to a global Process — so isExpanded on one is
    // meaningless and must not hide the only thing identifying it.
    expect(labelOf('callActivity', true).text).toBe('Handle order');
  });

  it('and reports no expansion state at all', () => {
    const cell = createFlowNodeCell(flowNode('Task_1', 'task'), shape('Task_1', 0, 0, 100, 80), context);

    expect((cell.get('bpmn') as { isExpanded: boolean | null }).isExpanded).toBeNull();
  });
});

describe('the cell carries the element name', () => {
  it('so accessibility does not depend on a label being painted', () => {
    // canvas-accessibility read the drawn label, which an expanded Sub-Process no longer has. A
    // name that vanishes when the notation changes was never really coming from the model.
    const cell = createFlowNodeCell(
      flowNode('Sub_1', 'subProcess', { name: 'Handle order' }),
      shape('Sub_1', 100, 100, 350, 200, { isExpanded: true }),
      context,
    );

    expect((cell.get('bpmn') as { name: string | null }).name).toBe('Handle order');
  });
});
