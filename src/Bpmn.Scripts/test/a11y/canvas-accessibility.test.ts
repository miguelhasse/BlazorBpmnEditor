// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest';
import type { dia } from '@joint/core';

import { CanvasAccessibility } from '../../src/a11y/canvas-accessibility.js';
import { canvasText } from '../../src/a11y/canvas-text.js';
import { canvasStrings } from '../fixtures/canvas-strings.js';
import { liveRuleIds } from '../../src/rules/connection-rules.js';
import type { BpmnFlowNodeType } from '../../src/model/contracts.js';

describe('canvas resources', () => {
  it('formats translated templates without interpreting model text', () => {
    expect(canvasText({ values: { Name: '{1} — {0}' } }, 'Name', 'Task', '<script>{0}</script>'))
      .toBe('<script>{0}</script> — Task');
  });

  it('has a display resource for every live rule', () => {
    for (const kind of ['sequenceFlow', 'messageFlow', 'association', 'dataAssociation'] as const) {
      for (const id of liveRuleIds(kind)) expect(canvasStrings.values[`Rule.${id}`]).toBeDefined();
    }
  });

  it('has a caption for every rendered flow-node type', () => {
    const captions: Record<BpmnFlowNodeType, string | undefined> = {
      startEvent: canvasStrings.values['Type.startEvent'],
      endEvent: canvasStrings.values['Type.endEvent'],
      intermediateCatchEvent: canvasStrings.values['Type.intermediateCatchEvent'],
      intermediateThrowEvent: canvasStrings.values['Type.intermediateThrowEvent'],
      boundaryEvent: canvasStrings.values['Type.boundaryEvent'],
      task: canvasStrings.values['Type.task'],
      userTask: canvasStrings.values['Type.userTask'],
      serviceTask: canvasStrings.values['Type.serviceTask'],
      scriptTask: canvasStrings.values['Type.scriptTask'],
      manualTask: canvasStrings.values['Type.manualTask'],
      businessRuleTask: canvasStrings.values['Type.businessRuleTask'],
      sendTask: canvasStrings.values['Type.sendTask'],
      receiveTask: canvasStrings.values['Type.receiveTask'],
      subProcess: canvasStrings.values['Type.subProcess'],
      eventSubProcess: canvasStrings.values['Type.eventSubProcess'],
      transaction: canvasStrings.values['Type.transaction'],
      adHocSubProcess: canvasStrings.values['Type.adHocSubProcess'],
      callActivity: canvasStrings.values['Type.callActivity'],
      exclusiveGateway: canvasStrings.values['Type.exclusiveGateway'],
      inclusiveGateway: canvasStrings.values['Type.inclusiveGateway'],
      parallelGateway: canvasStrings.values['Type.parallelGateway'],
      complexGateway: canvasStrings.values['Type.complexGateway'],
      eventBasedGateway: canvasStrings.values['Type.eventBasedGateway'],
    };
    for (const caption of Object.values(captions)) expect(caption).toBeDefined();
  });
});

describe('CanvasAccessibility', () => {
  it('uses localized counts and captions but preserves diagram and element names', () => {
    const element = document.createElement('g');
    const cell = {
      get: () => ({ elementType: 'userTask', name: 'Approve <order>' }),
      attr: () => undefined,
      isLink: () => false,
      findView: () => ({ model: cell, el: element }),
    };
    const on = vi.fn();
    const off = vi.fn();
    const paper = { on, off, model: { getElements: () => [cell], getLinks: () => [] } } as unknown as dia.Paper;
    const host = document.createElement('div');
    const strings = { values: {
      ...canvasStrings.values,
      'Summary': '{0} — {1}; {2}',
      'Shape.One': '{0} forme',
      'Connector.Many': '{0} liens',
      'Type.userTask': 'Tâche utilisateur',
    } };
    const accessibility = new CanvasAccessibility(paper, host, strings);

    accessibility.describe('Orders {0}', 1, 2);
    const labelCells = on.mock.calls[0]?.[1] as () => void;
    labelCells();

    expect(host.textContent).toBe('Orders {0} — 1 forme; 2 liens');
    expect(element.getAttribute('aria-label')).toBe('Tâche utilisateur: Approve <order>');
    accessibility.destroy();
    expect(host.children).toHaveLength(0);
    expect(off).toHaveBeenCalledWith('render:done', labelCells);
  });
});
