import { dia } from '@joint/core';
import { Bench } from 'tinybench';
import { it } from 'vitest';

import { ownerUnder, targetAt } from '../../src/editing/drop-resolver.js';
import { renderDocument } from '../../src/graph/document-renderer.js';
import { buildEmbeddingTree } from '../../src/graph/hierarchy.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import type { BpmnEdgeContract, BpmnSequenceFlowContract } from '../../src/model/contracts.js';
import { BpmnDocumentIndex, BpmnPlaneIndex } from '../../src/model/document.js';
import { bpmnCellNamespace } from '../../src/shapes/index.js';
import { emptyDocument, flowNode, shape } from '../fixtures/documents.js';

/**
 * Micro-benchmarks for the model-level hot paths (run with `npm run bench`).
 *
 * Model only — no paper, no DOM — so these isolate scan/allocation cost from painting. The
 * browser-level numbers (frame time, first render) live in tools/perf-bench.mjs.
 */
const SIZE = 600;
const COLUMNS = 30;
const metrics = new TableFontMetrics();

const syntheticDocument = (count: number, connected = false) => {
  const nodes = [];
  const shapes = [];
  const flows: BpmnSequenceFlowContract[] = [];
  const edges: BpmnEdgeContract[] = [];
  for (let index = 0; index < count; index++) {
    const id = 'Task_' + String(index);
    nodes.push(flowNode(id, 'task', { name: connected ? `Task ${String(index)}` : null }));
    shapes.push(shape(id, (index % COLUMNS) * 140, Math.floor(index / COLUMNS) * 110, 100, 80));
    if (connected && index > 0) {
      const previous = shapes[index - 1]!.bounds;
      const current = shapes[index]!.bounds;
      const flowId = `Flow_${String(index)}`;
      flows.push({
        id: flowId, sourceRef: `Task_${String(index - 1)}`, targetRef: id, name: null,
        containerId: 'Process_1', conditionExpression: null, isDefault: false,
      });
      edges.push({
        id: `${flowId}_di`, bpmnElement: flowId, label: null,
        waypoints: [{ x: previous.x + 100, y: previous.y + 40 }, { x: current.x, y: current.y + 40 }],
      });
    }
  }

  return emptyDocument({
    flowNodes: nodes,
    sequenceFlows: flows,
    diagrams: [{ id: 'D', name: null, plane: { id: 'P', bpmnElement: 'Process_1', shapes, edges } }],
  });
};

for (const size of [602, 2000, 5000]) {
  it(`paired render work on ${String(size)} connected shapes`, async () => {
    const before = syntheticDocument(size, true);
    const after = { ...before, flowNodes: before.flowNodes.map((node, index) => index === 1 ? { ...node, name: 'Renamed' } : node) };
    const full = new dia.Graph({}, { cellNamespace: bpmnCellNamespace });
    const incremental = new dia.Graph({}, { cellNamespace: bpmnCellNamespace });
    renderDocument(full, before, null, metrics);
    renderDocument(incremental, before, null, metrics);
    let fullToggle = false;
    let patchToggle = false;
    const bench = new Bench({ time: 200, warmupTime: 100, iterations: 3, warmupIterations: 1 });
    bench.add('full rename', () => {
      fullToggle = !fullToggle;
      renderDocument(full, fullToggle ? after : before, null, metrics);
    }).add('incremental rename', () => {
      patchToggle = !patchToggle;
      renderDocument(incremental, patchToggle ? after : before, null, metrics, true);
    });
    await bench.run();
    console.table(bench.tasks.map((task) => {
      const result = task.result;
      return 'latency' in result ? {
        shapes: size, connectors: size - 1, task: task.name,
        'mean ms': Number(result.latency.mean.toFixed(3)), 'p99 ms': Number(result.latency.p99.toFixed(3)),
      } : { task: task.name };
    }));
    full.clear();
    incremental.clear();
  });
}

const document = syntheticDocument(SIZE);
const grid = { size: 10, isVisible: true, snapEnabled: true };

it('attributes collection-wide projection preparation on 5000 connected shapes', async () => {
  const document = syntheticDocument(5000, true);
  const index = new BpmnDocumentIndex(document);
  const plane = new BpmnPlaneIndex(document.diagrams[0]!);
  const ids = new Set(document.flowNodes.map((node) => node.id));
  const bench = new Bench({ time: 300, warmupTime: 100 });
  bench.add('document index', () => new BpmnDocumentIndex(document))
    .add('plane index', () => new BpmnPlaneIndex(document.diagrams[0]!))
    .add('embedding tree', () => buildEmbeddingTree(index, plane, ids));
  await bench.run();
  console.table(bench.tasks.map((task) => 'latency' in task.result
    ? { task: task.name, 'mean ms': Number(task.result.latency.mean.toFixed(3)) }
    : { task: task.name }));
});

const loaded = new dia.Graph({}, { cellNamespace: bpmnCellNamespace });
renderDocument(loaded, document, null, metrics);

it('hot paths on a ' + String(SIZE) + '-shape graph', async () => {
  const bench = new Bench({ time: 500, warmupTime: 200 });

  bench
    .add('renderDocument (cold graph)', () => {
      renderDocument(new dia.Graph({}, { cellNamespace: bpmnCellNamespace }), document, null, metrics);
    })
    .add('getElements + getBBox scan', () => {
      let hits = 0;
      for (const element of loaded.getElements()) {
        if (element.getBBox().x >= 0) hits++;
      }

      return hits;
    })
    .add('ownerUnder (drop resolution)', () => {
      ownerUnder(loaded, { x: 2000, y: 1000 }, { width: 100, height: 80 }, null);
    })
    .add('targetAt (stencil hover)', () => {
      targetAt(loaded, { x: 2000, y: 1000 }, 'task', grid);
    });

  await bench.run();

  console.table(bench.tasks.map((task) => {
    const result = task.result;
    if (!('latency' in result)) return { task: task.name };

    return {
      task: task.name,
      'mean ms': Number(result.latency.mean.toFixed(4)),
      'p99 ms': Number(result.latency.p99.toFixed(4)),
      'ops/s': Math.round(result.throughput.mean),
    };
  }));
});
