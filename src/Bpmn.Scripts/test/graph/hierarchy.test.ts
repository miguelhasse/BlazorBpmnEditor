import { dia } from '@joint/core';
import { describe, expect, it } from 'vitest';
import { renderDocument } from '../../src/graph/document-renderer.js';
import { buildEmbeddingTree } from '../../src/graph/hierarchy.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import type { BpmnDocumentContract, BpmnLaneSetContract } from '../../src/model/contracts.js';
import { BpmnDocumentIndex, BpmnPlaneIndex } from '../../src/model/document.js';
import { bpmnCellNamespace } from '../../src/shapes/index.js';
import { pooledProcess } from '../fixtures/documents.js';
import { laneHeavyDocument, type LaneLayout } from '../fixtures/lane-heavy.js';

function tree(document: BpmnDocumentContract, hidden: readonly string[] = []) {
  const plane = new BpmnPlaneIndex(document.diagrams[0]!);
  return buildEmbeddingTree(new BpmnDocumentIndex(document), plane,
    new Set(plane.shapes.map((item) => item.bpmnElement).filter((id) => !hidden.includes(id))));
}

describe('lane ownership', () => {
  it.each(['none', 'root', 'sets', 'nested'] satisfies LaneLayout[])('matches ordered scan ownership for %s lanes', (layout) => {
    const document = laneHeavyDocument(90, layout);
    const index = new BpmnDocumentIndex(document);
    const expected = [];
    for (const item of document.diagrams[0]!.plane.shapes) {
      const element = index.find(item.bpmnElement);
      if (element?.kind === 'lane') {
        const set = document.laneSets.find((set) => set.id === element.value.laneSetId);
        const owner = document.lanes.find((lane) => lane.childLaneSetId === set?.id)?.id ?? 'Pool';
        expected.push([element.value.id, owner]);
      } else if (element?.kind === 'flowNode') {
        expected.push([element.value.id, index.laneOf(element.value.id) ?? 'Pool']);
      }
    }
    expect([...tree(document)]).toEqual(expected);
  });

  it('uses the first matching lane set rather than the last duplicate', () => {
    const document = pooledProcess();
    const changed = { ...document, laneSets: [...document.laneSets,
      { ...document.laneSets[0]!, containerId: 'MissingProcess' }] };
    expect(tree(changed).get('Lane_1')).toBe('Participant_1');
  });

  it('does not replace an unresolved first lane set with a later duplicate', () => {
    const document = pooledProcess();
    const changed = { ...document, laneSets: [
      { ...document.laneSets[0]!, containerId: 'MissingProcess' }, ...document.laneSets,
    ] };
    expect(tree(changed).has('Lane_1')).toBe(false);
  });

  it('uses the first parent lane and does not fall back when that parent is hidden', () => {
    const document = laneHeavyDocument(3, 'sets');
    const changed = { ...document, lanes: [
      { ...document.lanes[0]!, childLaneSetId: 'Set_2' },
      { ...document.lanes[1]!, childLaneSetId: 'Set_2' },
      document.lanes[2]!,
    ] };
    expect(tree(changed).get('Lane_2')).toBe('Lane_0');
    expect(tree(changed, ['Lane_0']).has('Lane_2')).toBe(false);
  });

  it('requires a lane set even when a parent lane claims it', () => {
    const document = laneHeavyDocument(3, 'nested');
    const changed = { ...document, laneSets: document.laneSets.filter((set) => set.id !== 'Set_1') };
    expect(tree(changed).has('Lane_1')).toBe(false);
  });

  it('uses only the supplied plane and rendered owners', () => {
    const document = pooledProcess();
    const other = { ...document.diagrams[0]!, id: 'Other', plane: {
      ...document.diagrams[0]!.plane,
      shapes: document.diagrams[0]!.plane.shapes.filter((item) => item.bpmnElement !== 'Participant_1'),
    } };
    const plane = new BpmnPlaneIndex(other);
    const result = buildEmbeddingTree(new BpmnDocumentIndex({ ...document, diagrams: [...document.diagrams, other] }),
      plane, new Set(plane.shapes.map((item) => item.bpmnElement)));
    expect(result.has('Lane_1')).toBe(false);
    expect(result.get('Task_1')).toBe('Lane_1');
  });

  it('rebuilds ownership after host array mutation, even when the index is reused', () => {
    const source = pooledProcess();
    const sets: BpmnLaneSetContract[] = [...source.laneSets];
    const document = { ...source, laneSets: sets };
    const index = new BpmnDocumentIndex(document);
    const plane = new BpmnPlaneIndex(document.diagrams[0]!);
    const ids = new Set(plane.shapes.map((item) => item.bpmnElement));
    const before = buildEmbeddingTree(index, plane, ids);
    sets[0] = { ...sets[0]!, containerId: 'MissingProcess' };
    expect(buildEmbeddingTree(index, plane, ids).has('Lane_1')).toBe(false);
    expect(before.get('Lane_1')).toBe('Participant_1');
  });

  it('observes changed child-lane claims between builds', () => {
    const source = laneHeavyDocument(3, 'sets');
    const lanes = [...source.lanes];
    const document = { ...source, lanes };
    const index = new BpmnDocumentIndex(document);
    const plane = new BpmnPlaneIndex(document.diagrams[0]!);
    const ids = new Set(plane.shapes.map((item) => item.bpmnElement));
    const before = buildEmbeddingTree(index, plane, ids);
    lanes[0] = { ...lanes[0]!, childLaneSetId: 'Set_2' };
    expect(buildEmbeddingTree(index, plane, ids).get('Lane_2')).toBe('Lane_0');
    expect(before.get('Lane_2')).toBe('Pool');
  });

  it('keeps nested lane parents through full and incremental rendering', () => {
    const document = laneHeavyDocument(60, 'nested');
    const graph = new dia.Graph({}, { cellNamespace: bpmnCellNamespace });
    const metrics = new TableFontMetrics();
    renderDocument(graph, document, null, metrics);
    const expected = [...tree(document)];
    expect(expected.map(([id]) => graph.getCell(id).parent())).toEqual(expected.map(([, parent]) => parent));
    renderDocument(graph, { ...document, flowNodes: document.flowNodes.map((node) => ({ ...node, name: 'Renamed' })) },
      null, metrics, true);
    expect(expected.map(([id]) => graph.getCell(id).parent())).toEqual(expected.map(([, parent]) => parent));
    graph.clear();
  });
});
