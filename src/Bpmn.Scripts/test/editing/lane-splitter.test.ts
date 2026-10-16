import { describe, expect, it } from 'vitest';
import { dia, shapes } from '@joint/core';

import { splittersOf } from '../../src/editing/lane-splitter.js';
import { anchorsFor } from '../../src/editing/resize-tool.js';

/**
 * Lanes always fill their Pool, so the splitters between them are the only way to size them.
 * These pin where a splitter sits and how far it may travel for each orientation, and that a Lane
 * offers no resize handles of its own.
 */
function rectangle(
  id: string,
  elementType: string,
  isHorizontal: boolean,
  box: readonly [number, number, number, number],
): dia.Element {
  return new shapes.standard.Rectangle({
    id,
    position: { x: box[0], y: box[1] },
    size: { width: box[2], height: box[3] },
    bpmn: { elementType, isHorizontal },
  });
}

function graphOf(isHorizontal: boolean, lanes: readonly (readonly [number, number, number, number])[]): dia.Graph {
  const graph = new dia.Graph({}, { cellNamespace: shapes });
  const pool = rectangle('Pool', 'pool', isHorizontal, [0, 0, 600, 250]);
  graph.addCell(pool);

  lanes.forEach((box, index) => {
    const lane = rectangle(`Lane_${String(index + 1)}`, 'lane', isHorizontal, box);
    graph.addCell(lane);
    pool.embed(lane);
  });

  return graph;
}

describe('splittersOf', () => {
  it('places a horizontal splitter on the shared edge of stacked Lanes', () => {
    const graph = graphOf(true, [[30, 0, 570, 125], [30, 125, 570, 125]]);

    expect(splittersOf(graph)).toEqual([{
      upperLaneId: 'Lane_1',
      isHorizontal: true,
      position: 125,
      from: 30,
      length: 570,
      min: 40,
      max: 210,
    }]);
  });

  it('places a vertical splitter on the shared edge of side-by-side Lanes', () => {
    const graph = graphOf(false, [[0, 30, 200, 220], [200, 30, 400, 220]]);

    expect(splittersOf(graph)).toEqual([{
      upperLaneId: 'Lane_1',
      isHorizontal: false,
      position: 200,
      from: 30,
      length: 220,
      min: 40,
      max: 560,
    }]);
  });

  it('offers one splitter fewer than there are Lanes, and none for a lone Lane', () => {
    expect(splittersOf(graphOf(true, [[30, 0, 570, 250]]))).toEqual([]);
    expect(splittersOf(graphOf(true, [[30, 0, 570, 80], [30, 80, 570, 80], [30, 160, 570, 90]]))).toHaveLength(2);
  });
});

describe('anchorsFor', () => {
  it('gives a Lane no resize handles, because it is sized with a splitter', () => {
    expect(anchorsFor('lane')).toEqual([]);
  });

  it('still gives a Pool all eight', () => {
    expect(anchorsFor('pool')).toHaveLength(8);
  });
});
