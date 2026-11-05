import { dia } from '@joint/core';
import { Bench } from 'tinybench';
import { expect, it } from 'vitest';
import { renderDocument } from '../../src/graph/document-renderer.js';
import { buildEmbeddingTree } from '../../src/graph/hierarchy.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { BpmnDocumentIndex, BpmnPlaneIndex } from '../../src/model/document.js';
import { bpmnCellNamespace } from '../../src/shapes/index.js';
import { laneHeavyDocument, type LaneLayout } from '../fixtures/lane-heavy.js';

it('measures lane ownership and complete lane rendering', async () => {
  const metrics = new TableFontMetrics();
  for (const count of [8, 60, 300, 1000, 2500]) {
    for (const layout of ['none', 'root', 'sets', 'nested'] satisfies LaneLayout[]) {
      const document = laneHeavyDocument(count, layout);
      const index = new BpmnDocumentIndex(document);
      const plane = new BpmnPlaneIndex(document.diagrams[0]!);
      const ids = new Set(plane.shapes.map((item) => item.bpmnElement));
      const entries = [...buildEmbeddingTree(index, plane, ids)];
      const expected = [];
      for (const item of plane.shapes) {
        const element = index.find(item.bpmnElement);
        if (element?.kind === 'lane') {
          const set = document.laneSets.find((set) => set.id === element.value.laneSetId);
          const owner = document.lanes.find((lane) => lane.childLaneSetId === set?.id)?.id ?? 'Pool';
          expected.push([element.value.id, owner]);
        } else if (element?.kind === 'flowNode') {
          expected.push([element.value.id, index.laneOf(element.value.id) ?? 'Pool']);
        }
      }
      expect(entries).toEqual(expected);
      let digest = 2166136261;
      for (const character of JSON.stringify(entries)) digest = Math.imul(digest ^ character.charCodeAt(0), 16777619) >>> 0;
      const bench = new Bench({ time: 100, warmupTime: 50, iterations: 5, warmupIterations: 3 });
      bench.add('embedding', () => buildEmbeddingTree(index, plane, ids));
      await bench.run();
      const result = bench.tasks[0]!.result;
      if (!('latency' in result)) throw new Error('Embedding benchmark failed');
      console.log(JSON.stringify({ count, layout, shapes: plane.shapes.length, task: 'embedding',
        mean: result.latency.mean, p99: result.latency.p99, digest }));

      const reused = new dia.Graph({}, { cellNamespace: bpmnCellNamespace });
      const renamed = { ...document, flowNodes: document.flowNodes.map((node) => ({ ...node, name: 'Renamed' })) };
      renderDocument(reused, document, null, metrics);
      let toggle = false;
      const render = new Bench({ time: 100, warmupTime: 50, iterations: 3, warmupIterations: 1 });
      render.add('fresh render', () => {
        const graph = new dia.Graph({}, { cellNamespace: bpmnCellNamespace });
        renderDocument(graph, document, null, metrics);
        graph.clear();
      }).add('incremental rename', () => {
        toggle = !toggle;
        renderDocument(reused, toggle ? renamed : document, null, metrics, true);
      });
      await render.run();
      for (const task of render.tasks) {
        if (!('latency' in task.result)) throw new Error('Render benchmark failed');
        console.log(JSON.stringify({ count, layout, shapes: plane.shapes.length, task: task.name,
          mean: task.result.latency.mean, p99: task.result.latency.p99 }));
      }
      expect(entries.map(([id]) => reused.getCell(id).parent())).toEqual(entries.map(([, parent]) => parent));
      reused.clear();
    }
  }
});
