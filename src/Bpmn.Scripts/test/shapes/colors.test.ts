import { dia } from '@joint/core';
import { describe, expect, it } from 'vitest';

import { renderDocument } from '../../src/graph/document-renderer.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { applyDiColors } from '../../src/shapes/colors.js';
import { bpmnCellNamespace } from '../../src/shapes/index.js';
import { BpmnConnectorLink, BpmnGroupElement, BpmnTextAnnotationElement } from '../../src/shapes/elements.js';
import * as markers from '../../src/markers/connector-markers.js';
import { linearProcess, pooledProcess, shape } from '../fixtures/documents.js';

const metrics = new TableFontMetrics();
const graphOf = (): dia.Graph => new dia.Graph({}, { cellNamespace: bpmnCellNamespace });

describe('DI colors', () => {
  it('colors shapes and labels, then clears all authored attributes on reconciliation', () => {
    const original = linearProcess();
    const colored = {
      ...original,
      diagrams: original.diagrams.map((diagram) => ({ ...diagram, plane: {
        ...diagram.plane,
        shapes: diagram.plane.shapes.map((item) => ({
          ...item, backgroundColor: '#abcdef', borderColor: '#123456',
          label: { bounds: null, color: '#654321' },
        })),
      } })),
    };
    const graph = graphOf();

    renderDocument(graph, colored, null, metrics);

    expect(graph.getCell('Task_1').attr('body/fill')).toBe('#abcdef');
    expect(graph.getCell('Task_1').attr('body/stroke')).toBe('#123456');
    expect(graph.getCell('StartEvent_1').attr('outerRing/fill')).toBe('#abcdef');
    expect(graph.getCell('Task_1').attr('label/fill')).toBe('#654321');
    renderDocument(graph, original, null, metrics);
    expect(graph.getCell('Task_1').attr('body/fill')).toBe('var(--bpmn-fill)');
    expect(graph.getCell('Task_1').attr('root/style')).toBeUndefined();
    expect(graph.getCell('Task_1').attr('label/data-bpmn-color-ink')).toBeNull();
  });

  it('colors swimlane body and header', () => {
    const document = pooledProcess();
    const colored = { ...document, diagrams: document.diagrams.map((diagram) => ({
      ...diagram, plane: { ...diagram.plane, shapes: diagram.plane.shapes.map((item) => ({
        ...item, backgroundColor: '#abcdef', borderColor: '#123456',
      })) },
    })) };
    const graph = graphOf();
    renderDocument(graph, colored, null, metrics);
    const swimlanes = graph.getElements().filter((cell) => cell.get('type') === 'bpmn.Swimlane');
    expect(swimlanes.length).toBeGreaterThan(1);
    for (const swimlane of swimlanes) {
      expect(swimlane.attr('body/fill')).toBe('#abcdef');
      expect(swimlane.attr('header/fill')).toBe('#abcdef');
    }
  });

  it.each([
    markers.sequenceFlowArrow, markers.messageFlowArrow, markers.associationArrow,
    markers.messageFlowCircle, markers.conditionalDiamond,
  ])('colors cloned marker definitions without changing hollows or shared constants: %j', (marker) => {
    const link = new BpmnConnectorLink({ attrs: { line: { targetMarker: marker } } });
    const other = new BpmnConnectorLink({ attrs: { line: { targetMarker: marker } } });

    applyDiColors(link, { id: 'E', bpmnElement: 'F', waypoints: [], label: null, borderColor: '#123456' });

    expect(link.attr('line/stroke')).toBe('#123456');
    expect(JSON.stringify(link.attr('line/targetMarker'))).toContain('#123456');
    expect(JSON.stringify(other.attr('line/targetMarker'))).not.toContain('#123456');
    expect(JSON.stringify(marker)).not.toContain('#123456');
    if (marker.fill === 'none') expect(link.attr('line/targetMarker/fill')).toBe('none');
    if (marker.fill === 'var(--bpmn-fill)') expect(link.attr('line/targetMarker/fill')).toBe('var(--bpmn-fill)');
  });

  it('colors connector text, default ticks, source markers and arrowheads independently', () => {
    const document = linearProcess();
    const colored = {
      ...document,
      sequenceFlows: document.sequenceFlows.map((flow) => ({
        ...flow, name: 'Next', isDefault: true, conditionExpression: 'condition',
      })),
      diagrams: document.diagrams.map((diagram) => ({ ...diagram, plane: {
        ...diagram.plane, edges: diagram.plane.edges.map((edge, index) => ({
          ...edge, borderColor: index === 0 ? '#112233' : '#445566', label: { bounds: null, color: '#abcdef' },
        })),
      } })),
    };
    const graph = graphOf();

    renderDocument(graph, colored, null, metrics);

    const first = graph.getCell('Flow_1') as dia.Link;
    expect(first.attr('line/targetMarker/fill')).toBe('#112233');
    expect(first.attr('line/sourceMarker/stroke')).toBe('#112233');
    expect(first.attr('defaultMarker/stroke')).toBe('#112233');
    expect(first.labels()[0]?.attrs?.text?.fill).toBe('#abcdef');
    expect(graph.getCell('Flow_2').attr('line/targetMarker/fill')).toBe('#445566');
  });

  it.each([new BpmnGroupElement(), new BpmnTextAnnotationElement()])('colors open shapes without changing hit areas', (cell) => {
    applyDiColors(cell, shape('Shape', 0, 0, 100, 80, { backgroundColor: '#abcdef', borderColor: '#123456' }));
    expect(cell.attr('hitArea/stroke')).toBe('transparent');
    const selector = cell.get('type') === 'bpmn.Group' ? 'body' : 'background';
    expect(cell.attr(`${selector}/fill`)).toBe('#abcdef');
  });

  it('never uses unvalidated values as CSS or SVG attributes', () => {
    const cell = new BpmnTextAnnotationElement();
    applyDiColors(cell, shape('Shape', 0, 0, 100, 80, {
      backgroundColor: 'red', borderColor: 'url(https://example.invalid)', label: { bounds: null, color: '#abc' },
    }));
    expect(cell.attr('bracket/stroke')).toBe('var(--bpmn-stroke)');
    expect(cell.attr('root/style')).toBeUndefined();
  });
});
