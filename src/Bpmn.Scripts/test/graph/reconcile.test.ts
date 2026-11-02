import { dia } from '@joint/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { renderDocument } from '../../src/graph/document-renderer.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import type { BpmnDocumentContract } from '../../src/model/contracts.js';
import { bpmnCellNamespace } from '../../src/shapes/index.js';
import * as factory from '../../src/shapes/registry.js';
import { emptyDocument, flowNode, linearProcess, pooledProcess, shape } from '../fixtures/documents.js';

/**
 * Rendering over a graph that already holds an earlier render must leave exactly what rendering from nothing would.
 *
 * The property is the whole safety argument for reconciling instead of resetting: for an edit from document A to document B, a graph that
 * was drawn from A and then reconciled to B equals a graph drawn from B alone, cell by cell. `z` is left out because it is allocated, not
 * described; everything else a cell carries is compared.
 */
const metrics = new TableFontMetrics();

function newGraph(): dia.Graph {
  return new dia.Graph({}, { cellNamespace: bpmnCellNamespace });
}

function describeGraph(graph: dia.Graph): Record<string, unknown> {
  const cells: Record<string, unknown> = {};
  for (const cell of graph.getCells()) {
    const { z: _z, embeds, ...rest } = cell.attributes as Record<string, unknown> & { embeds?: string[] };
    cells[String(cell.id)] = { ...rest, embeds: [...(embeds ?? [])].sort() };
  }
  return cells;
}

function expectReconciledEqualsFresh(before: BpmnDocumentContract, after: BpmnDocumentContract): dia.Graph {
  const fresh = newGraph();
  renderDocument(fresh, after, null, metrics);

  let reconciled = newGraph();
  for (const incremental of [false, true]) {
    reconciled = newGraph();
    renderDocument(reconciled, before, null, metrics);
    renderDocument(reconciled, after, null, metrics, incremental);
    expect(describeGraph(reconciled)).toEqual(describeGraph(fresh));
    const paintOrder = (graph: dia.Graph): string[] => graph.getCells().map((cell) => String(cell.id));
    expect(paintOrder(reconciled)).toEqual(paintOrder(fresh));
  }
  return reconciled;
}

const withoutNode = (document: BpmnDocumentContract, id: string): BpmnDocumentContract => ({
  ...document,
  flowNodes: document.flowNodes.filter((node) => node.id !== id),
  sequenceFlows: document.sequenceFlows.filter((flow) => flow.sourceRef !== id && flow.targetRef !== id),
  diagrams: document.diagrams.map((diagram) => ({
    ...diagram,
    plane: {
      ...diagram.plane,
      shapes: diagram.plane.shapes.filter((item) => item.bpmnElement !== id),
      edges: diagram.plane.edges.filter((edge) => !edge.bpmnElement.includes(id)),
    },
  })),
});

describe('renderDocument over an existing render', () => {
  let graph: dia.Graph;

  beforeEach(() => {
    graph = newGraph();
  });

  afterEach(() => { vi.restoreAllMocks(); });

  it('constructs one changed cell, not the diagram, for a rename', () => {
    const shapeFactory = vi.spyOn(factory, 'createFlowNodeCell');
    const linkFactory = vi.spyOn(factory, 'createSequenceFlowLink');
    const before = linearProcess();
    renderDocument(graph, before, null, metrics);
    expect(shapeFactory).toHaveBeenCalledTimes(3);
    expect(linkFactory).toHaveBeenCalledTimes(2);
    shapeFactory.mockClear();
    linkFactory.mockClear();
    const after = { ...before, flowNodes: before.flowNodes.map((node) => node.id === 'Task_1' ? { ...node, name: 'Renamed' } : node) };

    renderDocument(graph, after, null, metrics, true);

    expect(shapeFactory).toHaveBeenCalledTimes(1);
    expect(linkFactory).not.toHaveBeenCalled();
  });

  it('does no label measurement for a patch with identical projection inputs', () => {
    const localMetrics = new TableFontMetrics();
    const measure = vi.spyOn(localMetrics, 'measure');
    const document = linearProcess();
    renderDocument(graph, document, null, localMetrics);
    expect(measure).toHaveBeenCalled();
    measure.mockClear();

    renderDocument(graph, document, null, localMetrics, true);

    expect(measure).not.toHaveBeenCalled();
  });

  it('retains the graph when every projected cell is reused', () => {
    const document = linearProcess();
    renderDocument(graph, document, null, metrics);
    const original = graph.getCells();
    const reset = vi.spyOn(graph, 'resetCells');

    renderDocument(graph, document, null, metrics, true);

    expect(reset).not.toHaveBeenCalled();
    expect(graph.getCells()).toEqual(original);
  });

  it('rebuilds changed labels but not unrelated labels', () => {
    const localMetrics = new TableFontMetrics();
    const measure = vi.spyOn(localMetrics, 'measure');
    const before = linearProcess();
    renderDocument(graph, before, null, localMetrics);
    measure.mockClear();
    const after = { ...before, flowNodes: before.flowNodes.map((node) => node.id === 'Task_1' ? { ...node, name: 'New label' } : node) };

    renderDocument(graph, after, null, localMetrics, true);

    expect(measure).toHaveBeenCalled();
    expect(measure.mock.calls.every(([text]) => !text.includes('Order'))).toBe(true);
    expect(graph.getCell('Task_1').attr('label/text')).toBe('New label');
  });

  it('restores dirty live previews on an incremental patch and a full resync', () => {
    const document = linearProcess();
    renderDocument(graph, document, null, metrics);
    const task = graph.getCell('Task_1') as dia.Element;
    task.position(999, 999);
    renderDocument(graph, document, null, metrics, true);
    expect(task.position()).toEqual({ x: 200, y: 80 });
    task.attr('label/text', 'Uncommitted');
    renderDocument(graph, document, null, metrics);
    expect(task.attr('label/text')).toBe('Check stock');
  });

  it('does not share cached projections across graphs or font metric providers', () => {
    const document = linearProcess();
    renderDocument(graph, document, null, metrics);
    const other = newGraph();
    const localMetrics = new TableFontMetrics();
    const measure = vi.spyOn(localMetrics, 'measure');
    renderDocument(other, document, null, localMetrics, true);
    expect(measure).toHaveBeenCalled();
    measure.mockClear();
    renderDocument(graph, document, null, localMetrics, true);
    expect(measure).toHaveBeenCalled();
    expect(other.getCell('Task_1')).not.toBe(graph.getCell('Task_1'));
  });

  it('keeps the view of a cell that did not change', () => {
    renderDocument(graph, linearProcess(), null, metrics);
    const task = graph.getCell('Task_1');

    renderDocument(graph, linearProcess(), null, metrics);

    expect(graph.getCell('Task_1')).toBe(task);
  });

  it('equals a fresh render when nothing changed', () => {
    expectReconciledEqualsFresh(linearProcess(), linearProcess());
  });

  it('equals a fresh render after a rename', () => {
    const before = linearProcess();
    const after = { ...before, flowNodes: before.flowNodes.map((node) => (node.id === 'Task_1' ? { ...node, name: 'Renamed' } : node)) };

    const reconciled = expectReconciledEqualsFresh(before, after);

    expect(reconciled.getCell('Task_1').attr('label/text')).toBe('Renamed');
  });

  it('equals a fresh render after a move and a resize', () => {
    const before = linearProcess();
    const after = {
      ...before,
      diagrams: before.diagrams.map((diagram) => ({
        ...diagram,
        plane: {
          ...diagram.plane,
          shapes: diagram.plane.shapes.map((item) => (item.bpmnElement === 'Task_1' ? { ...item, bounds: { x: 260, y: 120, width: 140, height: 90 } } : item)),
        },
      })),
    };

    expectReconciledEqualsFresh(before, after);
  });

  it('clears explicit DI colors back to theme defaults', () => {
    const original = linearProcess();
    const before = {
      ...original,
      diagrams: original.diagrams.map((diagram) => ({
        ...diagram, plane: { ...diagram.plane, shapes: diagram.plane.shapes.map((item) =>
          item.bpmnElement === 'Task_1' ? { ...item, backgroundColor: '#112233', borderColor: '#445566' } : item) },
      })),
    };
    expectReconciledEqualsFresh(before, original);
  });

  it('recomputes boundary labels when only host geometry changes', () => {
    const original = linearProcess();
    const before = {
      ...original,
      flowNodes: [...original.flowNodes, flowNode('Boundary', 'boundaryEvent', { attachedToRef: 'Task_1', name: 'Timeout' })],
      diagrams: original.diagrams.map((diagram) => ({
        ...diagram, plane: { ...diagram.plane, shapes: [...diagram.plane.shapes, shape('Boundary', 250, 140, 36, 36)] },
      })),
    };
    const after = {
      ...before, diagrams: before.diagrams.map((diagram) => ({
        ...diagram, plane: { ...diagram.plane, shapes: diagram.plane.shapes.map((item) =>
          item.bpmnElement === 'Task_1' ? { ...item, bounds: { ...item.bounds, y: 180 } } : item) },
      })),
    };
    expectReconciledEqualsFresh(before, after);
  });

  it('recomputes flow-attached associations when their flow DI changes', () => {
    const original = linearProcess();
    const before: BpmnDocumentContract = {
      ...original,
      textAnnotations: [{ id: 'Note', text: 'Review', containerId: 'Process_1' }],
      associations: [{ id: 'Association', sourceRef: 'Flow_1', targetRef: 'Note', direction: 'none', containerId: 'Process_1' }],
      diagrams: original.diagrams.map((diagram) => ({
        ...diagram, plane: {
          ...diagram.plane, shapes: [...diagram.plane.shapes, shape('Note', 150, 200, 80, 40)],
          edges: [...diagram.plane.edges, {
            id: 'Association_di', bpmnElement: 'Association', label: null,
            waypoints: [{ x: 170, y: 118 }, { x: 170, y: 200 }],
          }],
        },
      })),
    };
    const after = {
      ...before, diagrams: before.diagrams.map((diagram) => ({
        ...diagram, plane: { ...diagram.plane, edges: diagram.plane.edges.map((edge) =>
          edge.bpmnElement === 'Flow_1' ? { ...edge, waypoints: [{ x: 136, y: 118 }, { x: 170, y: 160 }, { x: 200, y: 118 }] } : edge) },
      })),
    };
    expectReconciledEqualsFresh(before, after);
  });

  it.each([false, true])('restores flow-attached associations after endpoint replacement or reconnection (remove=%s)', (remove) => {
    const original = linearProcess();
    const before: BpmnDocumentContract = {
      ...original,
      textAnnotations: [{ id: 'Note', text: 'Review', containerId: 'Process_1' }],
      associations: [{ id: 'Association', sourceRef: 'Flow_1', targetRef: 'Note', direction: 'none', containerId: 'Process_1' }],
      diagrams: original.diagrams.map((diagram) => ({
        ...diagram, plane: {
          ...diagram.plane, shapes: [...diagram.plane.shapes, shape('Note', 150, 200, 80, 40)],
          edges: [...diagram.plane.edges, {
            id: 'Association_di', bpmnElement: 'Association', label: null,
            waypoints: [{ x: 170, y: 118 }, { x: 170, y: 200 }],
          }],
        },
      })),
    };
    const after: BpmnDocumentContract = {
      ...before,
      flowNodes: remove ? before.flowNodes.filter((node) => node.id !== 'Task_1')
        : before.flowNodes.map((node) => node.id === 'Task_1' ? { ...node, type: 'exclusiveGateway' } : node),
      sequenceFlows: remove ? before.sequenceFlows.filter((flow) => flow.id === 'Flow_1')
        .map((flow) => ({ ...flow, targetRef: 'EndEvent_1' })) : before.sequenceFlows,
      diagrams: remove ? before.diagrams.map((diagram) => ({
        ...diagram, plane: { ...diagram.plane, shapes: diagram.plane.shapes.filter((item) => item.bpmnElement !== 'Task_1') },
      })) : before.diagrams,
    };

    expectReconciledEqualsFresh(before, after);
  });

  it('hides and restores cached children when a sub-process collapses and expands', () => {
    const before = emptyDocument({
      flowNodes: [flowNode('Sub', 'subProcess'), flowNode('Child', 'task', { containerId: 'Sub' })],
      diagrams: [{
        id: 'D', name: null, plane: {
          id: 'P', bpmnElement: 'Process_1', edges: [],
          shapes: [shape('Sub', 0, 0, 400, 200, { isExpanded: true }), shape('Child', 40, 40, 100, 80)],
        },
      }],
    });
    const collapsed = {
      ...before, diagrams: before.diagrams.map((diagram) => ({
        ...diagram, plane: { ...diagram.plane, shapes: diagram.plane.shapes.map((item) =>
          item.bpmnElement === 'Sub' ? { ...item, isExpanded: false } : item) },
      })),
    };
    expectReconciledEqualsFresh(before, collapsed);
    expectReconciledEqualsFresh(collapsed, before);
  });

  it('equals a fresh render after a delete, including the connectors it took with it', () => {
    const before = linearProcess();
    const after = withoutNode(before, 'Task_1');

    const reconciled = expectReconciledEqualsFresh(before, after);

    expect(reconciled.getCell('Task_1')).toBeUndefined();
    expect(reconciled.getCells().filter((cell) => cell.isLink())).toHaveLength(0);
  });

  it('equals a fresh render after a create', () => {
    const before = linearProcess();
    const after = {
      ...before,
      flowNodes: [...before.flowNodes, flowNode('Task_New', 'userTask', { name: 'Added' })],
      diagrams: before.diagrams.map((diagram) => ({
        ...diagram,
        plane: { ...diagram.plane, shapes: [...diagram.plane.shapes, shape('Task_New', 400, 300, 100, 80)] },
      })),
    };

    expectReconciledEqualsFresh(before, after);
  });

  it('paints in the same order as a full render after a create, shapes under connectors', () => {
    const before = linearProcess();
    const after = {
      ...before,
      flowNodes: [...before.flowNodes, flowNode('Task_New', 'task')],
      diagrams: before.diagrams.map((diagram) => ({
        ...diagram,
        plane: { ...diagram.plane, shapes: [...diagram.plane.shapes, shape('Task_New', 400, 300, 100, 80)] },
      })),
    };
    renderDocument(graph, before, null, metrics);
    renderDocument(graph, after, null, metrics);
    const fresh = newGraph();
    renderDocument(fresh, after, null, metrics);

    const paintOrder = (g: dia.Graph): string[] => [...g.getCells()].sort((a, b) => (a.get('z')!) - (b.get('z')!)).map((cell) => String(cell.id));

    expect(paintOrder(graph)).toEqual(paintOrder(fresh));
  });

  it('paints in the same order as a full render after the document reorders what it draws', () => {
    const before = linearProcess();
    const after = {
      ...before,
      diagrams: before.diagrams.map((diagram) => ({
        ...diagram,
        plane: { ...diagram.plane, shapes: [...diagram.plane.shapes].reverse() },
      })),
    };
    renderDocument(graph, before, null, metrics);
    renderDocument(graph, after, null, metrics);
    const fresh = newGraph();
    renderDocument(fresh, after, null, metrics);

    const paintOrder = (g: dia.Graph): string[] => [...g.getCells()].sort((x, y) => (x.get('z')!) - (y.get('z')!)).map((cell) => String(cell.id));

    expect(paintOrder(graph)).toEqual(paintOrder(fresh));
  });

  it('keeps every cell at a distinct paint order after many insertions between the same neighbours', () => {
    let document = linearProcess();
    renderDocument(graph, document, null, metrics);

    for (let created = 0; created < 80; created++) {
      const id = `Task_New_${String(created)}`;
      document = {
        ...document,
        flowNodes: [...document.flowNodes, flowNode(id, 'task')],
        diagrams: document.diagrams.map((diagram) => {
          // Always just before the first connector, so the gap between the same two neighbours halves each time.
          const shapes = [...diagram.plane.shapes, shape(id, 400 + created, 300, 100, 80)];
          return { ...diagram, plane: { ...diagram.plane, shapes } };
        }),
      };
      renderDocument(graph, document, null, metrics);
    }

    const orders = graph.getCells().map((cell) => cell.get('z')!);
    expect(new Set(orders).size).toBe(orders.length);
  });

  it('equals a fresh render after a lane is deleted and its tasks stay, drawn in the pool', () => {
    const before = pooledProcess();
    const after: BpmnDocumentContract = {
      ...before,
      laneSets: before.laneSets.map((set) => ({ ...set, laneIds: ['Lane_2'] })),
      lanes: before.lanes.filter((lane) => lane.id !== 'Lane_1'),
      flowNodes: before.flowNodes.map((node) => (node.id === 'Task_1' ? { ...node, laneId: null } : node)),
      diagrams: before.diagrams.map((diagram) => ({
        ...diagram,
        plane: { ...diagram.plane, shapes: diagram.plane.shapes.filter((item) => item.bpmnElement !== 'Lane_1') },
      })),
    };

    // Joint removes what is embedded in a removed cell; Task_1 was embedded in the lane and has to survive it.
    const reconciled = expectReconciledEqualsFresh(before, after);

    expect(reconciled.getCell('Lane_1')).toBeUndefined();
    expect(reconciled.getCell('Task_1')).toBeDefined();
    expect(reconciled.getCell('Task_2')).toBeDefined();
  });

  it('equals a fresh render after an expanded sub-process is removed and its children stay', () => {
    const before = emptyDocument({
      flowNodes: [
        flowNode('Sub_1', 'subProcess', { flowElementsOwned: true }),
        flowNode('Inner_1', 'task', { containerId: 'Sub_1' }),
      ],
      diagrams: [
        {
          id: 'D1',
          name: null,
          plane: {
            id: 'P1',
            bpmnElement: 'Process_1',
            shapes: [shape('Sub_1', 0, 0, 300, 200, { isExpanded: true }), shape('Inner_1', 50, 50, 100, 80)],
            edges: [],
          },
        },
      ],
    });
    const after: BpmnDocumentContract = {
      ...before,
      flowNodes: [flowNode('Inner_1', 'task')],
      diagrams: before.diagrams.map((diagram) => ({
        ...diagram,
        plane: { ...diagram.plane, shapes: diagram.plane.shapes.filter((item) => item.bpmnElement !== 'Sub_1') },
      })),
    };

    const reconciled = expectReconciledEqualsFresh(before, after);

    expect(reconciled.getCell('Sub_1')).toBeUndefined();
    expect(reconciled.getCell('Inner_1')).toBeDefined();
  });

  it('removes and adds replaced cells inside the render batch, which is what lets the selection ignore them', () => {
    const before = linearProcess();
    renderDocument(graph, before, null, metrics);
    const seen: boolean[] = [];
    graph.on('remove', () => seen.push((graph as unknown as { hasActiveBatch(name: string): boolean }).hasActiveBatch('render')));
    const after = { ...before, flowNodes: before.flowNodes.map((node) => (node.id === 'Task_1' ? { ...node, type: 'exclusiveGateway' as const } : node)) };

    renderDocument(graph, after, null, metrics);

    expect(seen.length).toBeGreaterThan(0);
    expect(seen.every(Boolean)).toBe(true);
  });

  it('marks the positions it sets as its own, so nothing that answers a user moving a cell answers it', () => {
    const before = linearProcess();
    renderDocument(graph, before, null, metrics);
    const options: unknown[] = [];
    graph.on('change:position', (_cell: dia.Cell, _value: unknown, option: unknown) => options.push(option));
    const after = {
      ...before,
      diagrams: before.diagrams.map((diagram) => ({
        ...diagram,
        plane: {
          ...diagram.plane,
          shapes: diagram.plane.shapes.map((item) => (item.bpmnElement === 'Task_1' ? { ...item, bounds: { ...item.bounds, x: item.bounds.x + 25 } } : item)),
        },
      })),
    };

    renderDocument(graph, after, null, metrics);

    expect(options).toHaveLength(1);
    expect(options[0]).toMatchObject({ reconcile: true });
  });

  it('equals a fresh render after an element changes kind', () => {
    const before = linearProcess();
    const after = { ...before, flowNodes: before.flowNodes.map((node) => (node.id === 'Task_1' ? { ...node, type: 'exclusiveGateway' as const } : node)) };

    expectReconciledEqualsFresh(before, after);
  });

  it('equals a fresh render when what a lane contains changes, so embedding moves', () => {
    const before = pooledProcess();
    const moved = before.lanes.map((lane, index) => (index === 0
      ? { ...lane, flowNodeRefs: lane.flowNodeRefs.slice(0, -1) }
      : { ...lane, flowNodeRefs: [...lane.flowNodeRefs, ...(before.lanes[0]?.flowNodeRefs.slice(-1) ?? [])] }));
    const after = { ...before, lanes: moved };

    expectReconciledEqualsFresh(before, after);
  });

  it('equals a fresh render when everything changes, as on a switch to an unrelated document', () => {
    expectReconciledEqualsFresh(linearProcess(), pooledProcess());
  });

  it('resets once for a document with nothing in common with what is drawn, and never for an edit', () => {
    renderDocument(graph, linearProcess(), null, metrics);
    const reset = vi.spyOn(graph, 'resetCells');

    const renamed = linearProcess();
    renderDocument(graph, { ...renamed, flowNodes: renamed.flowNodes.map((node) => ({ ...node, name: 'Other' })) }, null, metrics);
    expect(reset).not.toHaveBeenCalled();

    const unrelated = emptyDocument({
      flowNodes: [flowNode('Elsewhere_1', 'task')],
      diagrams: [{ id: 'D2', name: null, plane: { id: 'P2', bpmnElement: 'Process_1', shapes: [shape('Elsewhere_1', 0, 0, 100, 80)], edges: [] } }],
    });
    renderDocument(graph, unrelated, null, metrics);
    expect(reset).toHaveBeenCalledTimes(1);
  });

  it('empties the graph for a document with nothing to draw', () => {
    renderDocument(graph, linearProcess(), null, metrics);

    renderDocument(graph, emptyDocument(), null, metrics);

    expect(graph.getCells()).toHaveLength(0);
  });
});
