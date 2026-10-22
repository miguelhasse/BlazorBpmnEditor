import { util, type dia } from '@joint/core';

import type { BpmnEdgeContract, BpmnShapeContract } from '../model/contracts.js';

/** Apply validated DI colors to a fresh cell, including marker definitions rather than marker inheritance. */
export function applyDiColors(cell: dia.Cell, di: BpmnShapeContract | BpmnEdgeContract): void {
  const valid = (value: string | null | undefined): string | null =>
    value !== undefined && value !== null && /^#[0-9a-fA-F]{6}$/u.test(value) ? value : null;
  const background = valid('backgroundColor' in di ? di.backgroundColor : null);
  const border = valid(di.borderColor);
  const text = valid(di.label?.color);

  // Clone nested marker bags: factories reuse marker constants between links.
  const recolor = (value: unknown): void => {
    if (value === null || typeof value !== 'object') return;
    for (const item of Object.values(value)) recolor(item);
    const result = value as Record<string, unknown>;
    if (result.stroke === 'var(--bpmn-stroke)') {
      result.stroke = border ?? result.stroke;
      result['data-bpmn-color-stroke'] = border === null ? null : 'true';
    }
    if (result.fill === 'var(--bpmn-stroke)') {
      result.fill = border ?? result.fill;
      result['data-bpmn-color-ink'] = border === null ? null : 'true';
    }
    if (result.fill === 'var(--bpmn-fill)' || result.fill === 'var(--bpmn-header-fill)') {
      result.fill = background ?? result.fill;
      result['data-bpmn-color-background'] = background === null ? null : 'true';
    }
  };
  const attrs = util.cloneDeep(cell.get('attrs'));
  recolor(attrs);
  cell.set('attrs', attrs);
  if (cell.get('type') === 'bpmn.Group' || cell.get('type') === 'bpmn.Swimlane')
    cell.attr('body', {
      ...(background === null ? {} : { fill: background }),
      'data-bpmn-color-background': background === null ? null : 'true',
    });
  if (cell.get('type') === 'bpmn.TextAnnotation')
    cell.attr('background', {
      ...(background === null ? {} : { fill: background }),
      'data-bpmn-color-background': background === null ? null : 'true',
    });
  // Explicit defaults remove prior DOM attributes too: JointJS does not clear attributes merely
  // because the next model's bag omits them.
  if (cell.isElement())
    cell.attr('label', { fill: text ?? 'var(--bpmn-label-color)', 'data-bpmn-color-ink': text === null ? null : 'true' });
  if (cell.isLink()) {
    cell.labels(cell.labels().map((label) => ({
      ...label,
      attrs: {
        ...label.attrs,
        text: {
          ...label.attrs?.text,
          fill: text ?? 'var(--bpmn-label-color)',
          'data-bpmn-color-ink': text === null ? null : 'true',
        },
      },
    })));
  }
}
