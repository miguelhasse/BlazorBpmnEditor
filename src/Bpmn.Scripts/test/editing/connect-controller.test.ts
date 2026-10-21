// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest';
import { dia } from '@joint/core';

import { ConnectController } from '../../src/editing/connect-controller.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { createFlowNodeCell } from '../../src/shapes/registry.js';
import { flowNode, shape } from '../fixtures/documents.js';
import { canvasStrings } from '../fixtures/canvas-strings.js';

describe('live connection display', () => {
  it.each(['sequenceFlow', 'messageFlow', 'association', 'dataAssociation'])('ignores %s snap candidates for ordinary shapes without replacing their refusal', (type) => {
    const source = { model: new dia.Element({ bpmn: { elementType: 'task' } }) } as unknown as dia.CellView;
    const target = { model: new dia.Element({ bpmn: { elementType: 'startEvent' } }) } as unknown as dia.CellView;
    const line = { model: new dia.Link({ bpmn: { elementType: type } }) } as unknown as dia.CellView;
    const controller = new ConnectController(
      { size: 10, isVisible: true, snapEnabled: false },
      { mode: 'manhattan', step: 10, padding: 20, parallelSeparation: 12, followPoolOrientation: true },
      canvasStrings,
    );

    expect(controller.validate(source, null, target, null)).toBe(false);
    expect(controller.validate(source, null, line, null)).toBe(false);
    expect(controller.takeLastRejection()?.id).toBe('SF-003');
  });

  it.each(['sequenceFlow', 'messageFlow', 'association', 'dataAssociation'])('checks annotation-to-%s with shared rules', (type) => {
    const source = new dia.Element({ bpmn: { elementType: 'textAnnotation' } });
    const target = new dia.Link({ bpmn: { elementType: type } });
    const sourceView = { model: source } as unknown as dia.CellView;
    const targetView = { model: target } as unknown as dia.CellView;
    const controller = new ConnectController(
      { size: 10, isVisible: true, snapEnabled: false },
      { mode: 'manhattan', step: 10, padding: 20, parallelSeparation: 12, followPoolOrientation: true },
      canvasStrings,
    );
    const allowed = type === 'sequenceFlow' || type === 'messageFlow';
    expect(controller.validate(sourceView, null, targetView, null)).toBe(allowed);
    expect(controller.takeLastRejection()?.id ?? null).toBe(allowed ? null : 'A-002');
    if (allowed) expect(controller.validate(targetView, null, sourceView, null)).toBe(true);
    const preview = controller.defaultLink(sourceView);
    expect(preview.attr('line/targetMarker')).toBeNull();
    expect(preview.get('router')).toMatchObject({ name: 'normal' });
  });

  it('localizes tooltip text but keeps rejection data invariant, and clears after the gesture', () => {
    const host = document.createElement('div');
    document.body.append(host);
    const sourceElement = document.createElement('div');
    const targetElement = document.createElement('div');
    host.append(sourceElement, targetElement);
    const source = createFlowNodeCell(flowNode('Source', 'task'), shape('Source', 0, 0, 100, 80),
      { metrics: new TableFontMetrics() });
    const target = createFlowNodeCell(flowNode('Target', 'startEvent'), shape('Target', 200, 0, 36, 36),
      { metrics: new TableFontMetrics() });
    const sourceView = { model: source, el: sourceElement } as unknown as dia.CellView;
    const targetView = { model: target, el: targetElement } as unknown as dia.CellView;
    const paper = {
      el: host, on: vi.fn(), off: vi.fn(),
      findView: (element: Element) => element === targetElement ? targetView : sourceView,
    } as unknown as dia.Paper;
    const strings = { values: { ...canvasStrings.values, 'Rule.SF-003': 'Cible interdite.' } };
    const controller = new ConnectController(
      { size: 10, isVisible: true, snapEnabled: false },
      { mode: 'manhattan', step: 10, padding: 20, parallelSeparation: 12, followPoolOrientation: true },
      strings,
    ).attach(paper, { connectorRequested: vi.fn(), connectorRejected: vi.fn() });
    const originalElementFromPoint = Object.getOwnPropertyDescriptor(document, 'elementFromPoint');
    document.elementFromPoint = vi.fn(() => targetElement);

    expect(controller.validate(sourceView, null, targetView, null)).toBe(false);
    // MouseEvent supplies the PointerEvent fields consumed by the handler in jsdom.
    sourceElement.dispatchEvent(new MouseEvent('pointermove', { bubbles: true, buttons: 1, clientX: 210, clientY: 10 }));

    expect(host.querySelector('[role="tooltip"]')?.textContent).toBe('Cible interdite.');
    expect(controller.takeLastRejection()).toMatchObject({
      id: 'SF-003', message: 'Target must not be a Start Event.',
    });
    sourceElement.dispatchEvent(new MouseEvent('pointerup', { bubbles: true }));
    expect(host.querySelector('[role="tooltip"]')).toBeNull();
    controller.destroy();
    host.remove();
    if (originalElementFromPoint === undefined) {
      Reflect.deleteProperty(document, 'elementFromPoint');
    } else {
      Object.defineProperty(document, 'elementFromPoint', originalElementFromPoint);
    }
    vi.restoreAllMocks();
  });
});
