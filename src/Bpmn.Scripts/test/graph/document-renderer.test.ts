import { dia } from '@joint/core';
import { beforeEach, describe, expect, it } from 'vitest';

import { removeDocumentElements, renderDocument } from '../../src/graph/document-renderer.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { bpmnCellNamespace } from '../../src/shapes/index.js';
import { emptyDocument, flowNode, linearProcess, pooledProcess, shape } from '../fixtures/documents.js';

/**
 * Projection tests.
 *
 * These run against a bare dia.Graph with no paper and no DOM: the projection is a model
 * operation, and keeping it testable without a browser is what makes shape coverage cheap enough
 * to assert exhaustively later.
 */
const metrics = new TableFontMetrics();

let graph: dia.Graph;

beforeEach(() => {
  graph = new dia.Graph({}, { cellNamespace: bpmnCellNamespace });
});

describe('renderDocument', () => {
  it('creates one cell per BPMNShape and per drawable connector', () => {
    const result = renderDocument(graph, linearProcess(), null, metrics);

    expect(result.shapeCount).toBe(3);
    expect(result.edgeCount).toBe(2);
    expect(graph.getCells()).toHaveLength(5);
  });

  describe('removeDocumentElements', () => {
    it('removes the deletion closure from the graph and every diagram plane', () => {
      const document = linearProcess();
      const secondDiagram = {
        ...document.diagrams[0]!,
        id: 'Diagram_2',
        plane: { ...document.diagrams[0]!.plane, id: 'Plane_2' },
      };
      const multiDiagramDocument = { ...document, diagrams: [...document.diagrams, secondDiagram] };
      renderDocument(graph, multiDiagramDocument, 'Diagram_1', metrics);

      const updated = removeDocumentElements(graph, multiDiagramDocument, ['Task_1', 'Flow_1', 'Flow_2']);

      expect(updated).not.toBeNull();
      expect(updated?.flowNodes.map((node) => node.id)).toEqual(['StartEvent_1', 'EndEvent_1']);
      expect(updated?.sequenceFlows).toEqual([]);
      expect(updated?.diagrams.every((diagram) =>
        diagram.plane.shapes.every((shape) => shape.bpmnElement !== 'Task_1')
        && diagram.plane.edges.every((edge) => !['Flow_1', 'Flow_2'].includes(edge.bpmnElement)))).toBe(true);
      expect(graph.getCells().map((cell) => String(cell.id)).sort()).toEqual(['EndEvent_1', 'StartEvent_1']);

      const result = renderDocument(graph, updated!, 'Diagram_1', metrics);
      expect(result.shapeCount).toBe(2);
      expect(result.edgeCount).toBe(0);
      expect(graph.getCells().map((cell) => String(cell.id)).sort()).toEqual(['EndEvent_1', 'StartEvent_1']);
    });

    it('prunes removed nodes from surviving Lane membership', () => {
      const document = pooledProcess();
      renderDocument(graph, document, null, metrics);

      const updated = removeDocumentElements(graph, document, ['Task_1']);

      expect(updated?.lanes[0]?.flowNodeRefs).toEqual([]);
      expect(updated?.lanes[1]?.flowNodeRefs).toEqual(['Task_2']);
      expect(updated?.flowNodes.map((node) => node.id)).toEqual(['Task_2']);
      expect(graph.getCell('Task_1')).toBeUndefined();
      expect(graph.getCell('Task_2')).toBeDefined();
    });

    it('leaves the graph unchanged when a Lane requires full layout reconciliation', () => {
      const document = pooledProcess();
      renderDocument(graph, document, null, metrics);

      expect(removeDocumentElements(graph, document, ['Lane_1'])).toBeNull();
      expect(graph.getCell('Lane_1')).toBeDefined();
      expect(graph.getCell('Task_1')).toBeDefined();
    });

    it('leaves the graph unchanged when a surviving connector would be detached', () => {
      const document = linearProcess();
      renderDocument(graph, document, null, metrics);

      expect(removeDocumentElements(graph, document, ['Task_1'])).toBeNull();
      expect(graph.getCell('Task_1')).toBeDefined();
      expect(graph.getCell('Flow_1')).toBeDefined();
      expect(graph.getCell('Flow_2')).toBeDefined();
    });
  });

  it('uses the BPMN element id as the cell id', () => {
    renderDocument(graph, linearProcess(), null, metrics);

    // The semantic model is canonical and the cell is a projection of it; selection, commands
    // and DI write-back all depend on this identity holding.
    expect(graph.getCell('StartEvent_1')).toBeDefined();
    expect(graph.getCell('Task_1')).toBeDefined();
    expect(graph.getCell('Flow_1')).toBeDefined();
  });

  it('takes geometry verbatim from DI rather than recomputing it', () => {
    renderDocument(graph, linearProcess(), null, metrics);

    const task = graph.getCell('Task_1') as dia.Element;
    expect(task.position()).toEqual({ x: 200, y: 80 });
    expect(task.size()).toEqual({ width: 100, height: 80 });
  });

  it('gives a Start Event one ring and an Intermediate Event two', () => {
    const document = emptyDocument({
      flowNodes: [
        flowNode('Start_1', 'startEvent'),
        flowNode('Catch_1', 'intermediateCatchEvent'),
      ],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [shape('Start_1', 0, 0, 36, 36), shape('Catch_1', 100, 0, 36, 36)],
            edges: [],
          },
        },
      ],
    });

    renderDocument(graph, document, null, metrics);

    const start = graph.getCell('Start_1') as dia.Element;
    const catchEvent = graph.getCell('Catch_1') as dia.Element;

    expect(start.attr('innerRing/display')).toBe('none');
    expect(catchEvent.attr('innerRing/display')).toBe('inline');
  });

  it('dashes the rings of a non-interrupting Boundary Event', () => {
    const document = emptyDocument({
      flowNodes: [
        flowNode('Task_1', 'task'),
        flowNode('Boundary_1', 'boundaryEvent', { attachedToRef: 'Task_1', isInterrupting: false }),
      ],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [shape('Task_1', 0, 0, 100, 80), shape('Boundary_1', 80, 62, 36, 36)],
            edges: [],
          },
        },
      ],
    });

    renderDocument(graph, document, null, metrics);

    const boundary = graph.getCell('Boundary_1') as dia.Element;
    expect(boundary.attr('outerRing/strokeDasharray')).toBe('4 3');
  });

  it('embeds a Boundary Event in its host Activity (CT-005)', () => {
    const document = emptyDocument({
      flowNodes: [
        flowNode('Task_1', 'task'),
        flowNode('Boundary_1', 'boundaryEvent', { attachedToRef: 'Task_1' }),
      ],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [shape('Task_1', 0, 0, 100, 80), shape('Boundary_1', 80, 62, 36, 36)],
            edges: [],
          },
        },
      ],
    });

    renderDocument(graph, document, null, metrics);

    const boundary = graph.getCell('Boundary_1') as dia.Element;
    expect(boundary.parent()).toBe('Task_1');
  });

  it('builds the Pool to Lane to Task containment chain', () => {
    renderDocument(graph, pooledProcess(), null, metrics);

    const pool = graph.getCell('Participant_1') as dia.Element;
    const lane = graph.getCell('Lane_1') as dia.Element;
    const task = graph.getCell('Task_1') as dia.Element;

    expect(pool.parent()).toBeUndefined();
    expect(lane.parent()).toBe('Participant_1');
    expect(task.parent()).toBe('Lane_1');
  });

  it('does not draw elements the plane has no shape for', () => {
    // A collapsed Sub-Process's children are exactly this case: present in the model, absent
    // from DI, and therefore not on the canvas (section 2.7).
    const document = emptyDocument({
      flowNodes: [flowNode('Task_1', 'task'), flowNode('Hidden_1', 'task', { containerId: 'SubProcess_1' })],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [shape('Task_1', 0, 0, 100, 80)],
            edges: [],
          },
        },
      ],
    });

    const result = renderDocument(graph, document, null, metrics);

    expect(result.shapeCount).toBe(1);
    expect(graph.getCell('Hidden_1')).toBeUndefined();
  });

  it('draws nothing inside a collapsed Sub-Process, even when the plane has shapes for it', () => {
    /*
     * The case the test above does not cover, and the one that actually goes wrong. A document may
     * carry child DI on the same plane as a collapsed container — a hand edit, a tool that writes
     * it, or a collapse that flipped the flag before it moved the children — and drawing it puts
     * the contents over the closed box, parented to the Pool rather than to the container.
     */
    const document = emptyDocument({
      flowNodes: [
        flowNode('SubProcess_1', 'subProcess', { flowElementsOwned: true }),
        flowNode('Inner_1', 'task', { containerId: 'SubProcess_1' }),
      ],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [
              shape('SubProcess_1', 0, 0, 400, 300, { isExpanded: false }),
              shape('Inner_1', 40, 40, 100, 80),
            ],
            edges: [],
          },
        },
      ],
    });

    const result = renderDocument(graph, document, null, metrics);

    expect(graph.getCell('SubProcess_1')).toBeDefined();
    expect(graph.getCell('Inner_1')).toBeUndefined();
    expect(result.unrenderedElementIds).toContain('Inner_1');
  });

  it('and nothing inside one that says nothing about whether it is expanded', () => {
    const document = emptyDocument({
      flowNodes: [
        flowNode('SubProcess_1', 'subProcess', { flowElementsOwned: true }),
        flowNode('Inner_1', 'task', { containerId: 'SubProcess_1' }),
      ],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [shape('SubProcess_1', 0, 0, 400, 300), shape('Inner_1', 40, 40, 100, 80)],
            edges: [],
          },
        },
      ],
    });

    renderDocument(graph, document, null, metrics);

    expect(graph.getCell('Inner_1')).toBeUndefined();
  });

  it('but draws everything inside an expanded one', () => {
    // The mutation guard: a rule that hid content unconditionally would pass every test above.
    const document = emptyDocument({
      flowNodes: [
        flowNode('SubProcess_1', 'subProcess', { flowElementsOwned: true }),
        flowNode('Inner_1', 'task', { containerId: 'SubProcess_1' }),
      ],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [
              shape('SubProcess_1', 0, 0, 400, 300, { isExpanded: true }),
              shape('Inner_1', 40, 40, 100, 80),
            ],
            edges: [],
          },
        },
      ],
    });

    renderDocument(graph, document, null, metrics);

    expect(graph.getCell('Inner_1')).toBeDefined();
    expect(graph.getCell('Inner_1').parent()).toBe('SubProcess_1');
  });

  it('and a grandchild of a collapsed one is hidden too', () => {
    // Transitive: a Task inside a Sub-Process inside the collapsed one is just as undrawable.
    const document = emptyDocument({
      flowNodes: [
        flowNode('Outer_1', 'subProcess', { flowElementsOwned: true }),
        flowNode('Middle_1', 'subProcess', { containerId: 'Outer_1', flowElementsOwned: true }),
        flowNode('Deep_1', 'task', { containerId: 'Middle_1' }),
      ],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [
              shape('Outer_1', 0, 0, 400, 300, { isExpanded: false }),
              shape('Middle_1', 20, 20, 200, 150, { isExpanded: true }),
              shape('Deep_1', 40, 40, 100, 80),
            ],
            edges: [],
          },
        },
      ],
    });

    renderDocument(graph, document, null, metrics);

    expect(graph.getCell('Middle_1')).toBeUndefined();
    expect(graph.getCell('Deep_1')).toBeUndefined();
  });

  it('and a drilled-in plane still draws its own contents', () => {
    /*
     * The case the walk must not get wrong. A plane whose bpmnElement is the Sub-Process draws its
     * children without drawing the Sub-Process — that is the inside of a container, not the
     * outside of a closed one, and reading it as closed would render an empty diagram.
     */
    const document = emptyDocument({
      flowNodes: [
        flowNode('SubProcess_1', 'subProcess', { flowElementsOwned: true }),
        flowNode('Inner_1', 'task', { containerId: 'SubProcess_1' }),
      ],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'SubProcess_1',
            shapes: [shape('Inner_1', 40, 40, 100, 80)],
            edges: [],
          },
        },
      ],
    });

    renderDocument(graph, document, null, metrics);

    expect(graph.getCell('Inner_1')).toBeDefined();
  });

  it('skips a connector whose endpoints are not both drawn', () => {
    const document = linearProcess();
    const trimmed = {
      ...document,
      diagrams: [
        {
          ...document.diagrams[0]!,
          plane: {
            ...document.diagrams[0]!.plane,
            shapes: document.diagrams[0]!.plane.shapes.filter((s) => s.bpmnElement !== 'EndEvent_1'),
          },
        },
      ],
    };

    const result = renderDocument(graph, trimmed, null, metrics);

    expect(result.shapeCount).toBe(2);
    expect(result.edgeCount).toBe(1);
    expect(graph.getCell('Flow_2')).toBeUndefined();
  });

  it('reports DI entries whose element does not resolve (section 10.1)', () => {
    const document = emptyDocument({
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [shape('Ghost_1', 0, 0, 100, 80)],
            edges: [],
          },
        },
      ],
    });

    const result = renderDocument(graph, document, null, metrics);

    expect(result.unrenderedElementIds).toEqual(['Ghost_1']);
  });

  it('keeps interior waypoints as vertices and drops the endpoints', () => {
    const document = linearProcess();
    const routed = {
      ...document,
      diagrams: [
        {
          ...document.diagrams[0]!,
          plane: {
            ...document.diagrams[0]!.plane,
            edges: [
              {
                id: 'Flow_1_di',
                bpmnElement: 'Flow_1',
                waypoints: [
                  { x: 136, y: 118 },
                  { x: 170, y: 118 },
                  { x: 170, y: 60 },
                  { x: 200, y: 60 },
                ],
                label: null,
              },
            ],
          },
        },
      ],
    };

    renderDocument(graph, routed, null, metrics);

    const flow = graph.getCell('Flow_1') as dia.Link;
    expect(flow.vertices()).toEqual([
      { x: 170, y: 118 },
      { x: 170, y: 60 },
    ]);
  });

  it('keeps the semantic name when DI supplies label geometry', () => {
    // A BPMNDI label positions and styles a label; it never carries the string. Treating it as
    // a text source silently blanked every labelled shape in documents that emit BPMNLabel.
    const document = emptyDocument({
      flowNodes: [flowNode('Task_1', 'task', { name: 'Order Products' })],
      diagrams: [
        {
          id: 'Diagram_1',
          name: null,
          plane: {
            id: 'Plane_1',
            bpmnElement: 'Process_1',
            shapes: [
              shape('Task_1', 0, 0, 100, 80, {
                label: { id: 'Label_1', bounds: { x: 0, y: 90, width: 100, height: 20 }, styleId: null },
              }),
            ],
            edges: [],
          },
        },
      ],
    });

    renderDocument(graph, document, null, metrics);

    const task = graph.getCell('Task_1') as dia.Element;
    expect(task.attr('label/display')).toBe('inline');
    // Wrapped to two lines by the DI label's own 100px width, and nothing is lost.
    expect(task.attr('label/text')).toBe(['Order', 'Products'].join('\n'));
  });

  it('anchors connector endpoints at the persisted DI waypoints', () => {
    renderDocument(graph, linearProcess(), null, metrics);

    // Sections 10.6 and 11: imported geometry is honoured. Left to itself JointJS would anchor to the
    // shape centres and re-derive a boundary intersection, rerouting the imported diagram.
    const flow = graph.getCell('Flow_1') as dia.Link;

    // StartEvent_1 sits at (100,100) and the first waypoint is (136,118), so the anchor is
    // offset (36,18) from the shape's top-left. connectionPoint 'anchor' is what stops JointJS
    // pulling the line back to the outline afterwards.
    expect(flow.source()).toEqual({
      id: 'StartEvent_1',
      anchor: { name: 'topLeft', args: { dx: 36, dy: 18, useModelGeometry: true } },
      connectionPoint: { name: 'anchor' },
    });
  });

  it('puts an end that cannot belong to its shape on the outline, towards the other end', () => {
    // simple-process.bpmn really does declare every waypoint at the origin while its shapes sit
    // at (80,120) and (220,100). Honouring that collapses the connector to a point, and projecting
    // it piles every connector onto one corner, so each end leaves its shape on the line towards
    // the other one.
    const document = linearProcess();
    const bogus = {
      ...document,
      diagrams: [
        {
          ...document.diagrams[0]!,
          plane: {
            ...document.diagrams[0]!.plane,
            edges: [
              {
                id: 'Flow_1_di',
                bpmnElement: 'Flow_1',
                waypoints: [
                  { x: 0, y: 0 },
                  { x: 0, y: 0 },
                ],
                label: null,
              },
            ],
          },
        },
      ],
    };

    renderDocument(graph, bogus, null, metrics);

    const flow = graph.getCell('Flow_1') as dia.Link;
    const offset = (end: dia.Link.EndJSON): { dx: number; dy: number } => {
      const args = (end as unknown as { anchor?: { args?: { dx: number; dy: number } } }).anchor?.args;
      return { dx: args?.dx ?? Number.NaN, dy: args?.dy ?? Number.NaN };
    };

    // The event's right edge, and the task's left edge, both at the height between them.
    expect(offset(flow.source()).dx).toBeCloseTo(36, 0);
    expect(offset(flow.source()).dy).toBeCloseTo(18, 0);
    expect(offset(flow.target()).dx).toBeCloseTo(0, 0);
    expect(offset(flow.target()).dy).toBeGreaterThan(30);
  });

  it('leaves anchoring to JointJS when the document supplies no route', () => {
    const document = linearProcess();
    const withoutEdges = {
      ...document,
      diagrams: [
        {
          ...document.diagrams[0]!,
          plane: { ...document.diagrams[0]!.plane, edges: [] },
        },
      ],
    };

    renderDocument(graph, withoutEdges, null, metrics);

    const flow = graph.getCell('Flow_1') as dia.Link;

    expect(flow.source()).toEqual({ id: 'StartEvent_1' });
  });

  it('clears the graph for a document with no diagram', () => {
    renderDocument(graph, linearProcess(), null, metrics);
    const result = renderDocument(graph, emptyDocument({ diagrams: [] }), null, metrics);

    expect(result.diagramId).toBe('');
    expect(graph.getCells()).toHaveLength(0);
  });
});
