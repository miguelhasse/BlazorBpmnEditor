import { dia } from '@joint/core';
import { Bench } from 'tinybench';
import { it } from 'vitest';

import { ownerUnder, targetAt } from '../../src/editing/drop-resolver.js';
import { renderDocument } from '../../src/graph/document-renderer.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
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

const syntheticDocument = (count: number) => {
  const nodes = [];
  const shapes = [];
  for (let index = 0; index < count; index++) {
    const id = 'Task_' + String(index);
    nodes.push(flowNode(id, 'task'));
    shapes.push(shape(id, (index % COLUMNS) * 140, Math.floor(index / COLUMNS) * 110, 100, 80));
  }

  return emptyDocument({
    flowNodes: nodes,
    diagrams: [{ id: 'D', name: null, plane: { id: 'P', bpmnElement: 'Process_1', shapes, edges: [] } }],
  });
};

const document = syntheticDocument(SIZE);
const grid = { size: 10, isVisible: true, snapEnabled: true };

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
