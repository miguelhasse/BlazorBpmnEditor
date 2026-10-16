// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { dia, shapes } from '@joint/core';

import { SnapController } from '../../src/snapping/snap-controller.js';

/**
 * Alignment guides answer a user moving an element, and not the document putting it somewhere: an undo or a reload that lands a task a few
 * pixels from its neighbour must leave it where the document says.
 */
function fakePaper(): dia.Paper {
  const layer = document.createElementNS('http://www.w3.org/2000/svg', 'g');
  return { getLayerView: () => ({ el: layer }), scale: () => ({ sx: 1, sy: 1 }) } as unknown as dia.Paper;
}

function element(id: string, x: number, y: number): dia.Element {
  return new shapes.standard.Rectangle({ id, position: { x, y }, size: { width: 100, height: 80 } });
}

describe('SnapController', () => {
  let graph: dia.Graph;

  beforeEach(() => {
    vi.useFakeTimers();
    graph = new dia.Graph({}, { cellNamespace: shapes });
    graph.addCells([element('Peer', 0, 0), element('Moving', 300, 40)]);
    new SnapController(graph, fakePaper(), { alignmentGuidesEnabled: true, tolerance: 6 });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('snaps an element that a user moves to within tolerance of a peer', () => {
    const moving = graph.getCell('Moving') as dia.Element;

    moving.position(300, 3);

    expect(moving.position().y).toBe(0);
  });

  it('leaves alone an element that a render positions near a peer', () => {
    const moving = graph.getCell('Moving') as dia.Element;

    moving.set('position', { x: 300, y: 3 }, { reconcile: true });

    expect(moving.position().y).toBe(3);
  });
});
