import { dia } from '@joint/core';
import { beforeEach, describe, expect, it } from 'vitest';

import { creationPointIn, defaultSizeOf, targetAt } from '../../src/editing/drop-resolver.js';
import type { GridOptionsContract } from '../../src/interop/contracts.js';

/**
 * Stencil drop resolution.
 *
 * Run against a bare graph rather than a paper: the paper contributes only the client-to-local
 * conversion and the visible area, and everything that can be got wrong — where the pointer sits
 * relative to the new shape, when snapping applies, which container claims it — is graph geometry.
 */
const noSnap: GridOptionsContract = { size: 10, isVisible: true, snapEnabled: false };
const snapping: GridOptionsContract = { size: 10, isVisible: true, snapEnabled: true };

let graph: dia.Graph;

beforeEach(() => {
  graph = new dia.Graph();
});

/** A plain rectangle carrying the `bpmn` metadata the resolver reads. */
/**
 * A cell on the graph.
 *
 * A Sub-Process defaults to expanded here because that is what these tests mean by one — a
 * collapsed Sub-Process draws nothing inside it, so nothing can be dropped into it, and the two
 * cases that care say which they are.
 */
const place = (
  id: string,
  elementType: string,
  box: { x: number; y: number; width: number; height: number },
  bpmn: Record<string, unknown> = {},
): dia.Element => {
  const element = new dia.Element({
    id,
    type: 'standard.Rectangle',
    position: { x: box.x, y: box.y },
    size: { width: box.width, height: box.height },
    bpmn: { elementType, isExpanded: elementType.toLowerCase().includes('process') ? true : null, ...bpmn },
  });

  graph.addCell(element);
  return element;
};

describe('defaultSizeOf', () => {
  it('gives the notation sizes from section 4', () => {
    expect(defaultSizeOf('startEvent')).toEqual({ width: 36, height: 36 });
    expect(defaultSizeOf('exclusiveGateway')).toEqual({ width: 50, height: 50 });
    expect(defaultSizeOf('task')).toEqual({ width: 100, height: 80 });
    expect(defaultSizeOf('poolOrLane')).toEqual({ width: 600, height: 250 });
  });

  it('sizes artifacts and data references by their own notation, not the task default', () => {
    /*
     * All four fell through to the default and dropped at 100x80. Not a cosmetic difference: the
     * size decides which container the drop resolves into, and a Group that arrives Task-sized is
     * a frame too small to put anything in.
     */
    expect(defaultSizeOf('dataObjectReference')).toEqual({ width: 36, height: 50 });
    expect(defaultSizeOf('dataStoreReference')).toEqual({ width: 50, height: 50 });
    expect(defaultSizeOf('textAnnotation')).toEqual({ width: 100, height: 30 });
    expect(defaultSizeOf('group')).toEqual({ width: 300, height: 200 });
  });

  it('falls back to the task size for an unrecognised type', () => {
    // Better a plausible rectangle than a zero-sized shape the user cannot see or grab.
    expect(defaultSizeOf('somethingNew')).toEqual({ width: 100, height: 80 });
  });
});

describe('targetAt', () => {
  it('centres the new shape on the pointer', () => {
    const target = targetAt(graph, { x: 300, y: 200 }, 'task', noSnap);

    // Offering the top-left would put a 100x80 Task almost entirely below and right of the aim.
    expect(target.bounds).toEqual({ x: 250, y: 160, width: 100, height: 80 });
  });

  it('snaps the centre, not the corner', () => {
    const target = targetAt(graph, { x: 304, y: 197 }, 'task', snapping);

    // Centre 300,200 snapped from 304,197 — a corner-snapped 36px event would sit half off-grid.
    expect(target.bounds.x).toBe(250);
    expect(target.bounds.y).toBe(160);
  });

  it('reports no container for a drop on the bare plane', () => {
    const target = targetAt(graph, { x: 300, y: 200 }, 'task', noSnap);

    expect(target.containerId).toBeNull();
    expect(target.poolId).toBeNull();
  });

  it('reports the Sub-Process a drop landed inside', () => {
    place('SubProcess_1', 'subProcess', { x: 100, y: 100, width: 400, height: 300 });

    expect(targetAt(graph, { x: 300, y: 200 }, 'task', noSnap).containerId).toBe('SubProcess_1');
  });

  it('reports the deepest container, not the outermost', () => {
    const outer = place('SubProcess_1', 'subProcess', { x: 0, y: 0, width: 600, height: 500 });
    const inner = place('SubProcess_2', 'subProcess', { x: 100, y: 100, width: 400, height: 300 });
    outer.embed(inner);

    // A Sub-Process inside a Sub-Process is both, and only the innermost one owns the new element.
    expect(targetAt(graph, { x: 300, y: 200 }, 'task', noSnap).containerId).toBe('SubProcess_2');
  });

  it('never treats a Lane as the container, but still reports its Pool', () => {
    const pool = place('Pool_1', 'pool', { x: 0, y: 0, width: 800, height: 400 });
    const lane = place('Lane_1', 'lane', { x: 30, y: 0, width: 770, height: 200 });
    pool.embed(lane);

    const target = targetAt(graph, { x: 300, y: 100 }, 'task', noSnap);

    // A Lane classifies nodes without owning them: its Process is the container, which only the
    // host can resolve. The Pool is reported separately because Message Flow legality follows it.
    expect(target.containerId).toBeNull();
    expect(target.poolId).toBe('Pool_1');
  });

  it('resolves the combined Pool/Lane tool to a Lane anywhere inside a Pool', () => {
    const pool = place('Pool_1', 'pool', { x: 0, y: 0, width: 800, height: 400 });
    const lane = place('Lane_1', 'lane', { x: 30, y: 0, width: 770, height: 200 });
    const task = place('Task_1', 'task', { x: 200, y: 80, width: 100, height: 80 });
    pool.embed(lane);
    lane.embed(task);

    const target = targetAt(graph, { x: 240, y: 120 }, 'poolOrLane', noSnap);

    expect(target.bounds).toEqual({ x: -60, y: 57.5, width: 600, height: 125 });
    expect(target.poolId).toBe('Pool_1');
    expect(target.containerId).toBeNull();
  });

  it('resolves the combined Pool/Lane tool to a Pool outside all Pools', () => {
    place('Pool_1', 'pool', { x: 0, y: 0, width: 800, height: 400 });

    const target = targetAt(graph, { x: 900, y: 500 }, 'poolOrLane', noSnap);

    expect(target.bounds).toEqual({ x: 600, y: 375, width: 600, height: 250 });
    expect(target.poolId).toBeNull();
  });

  it('refuses a collapsed Sub-Process, whose inside is not on the canvas', () => {
    /*
     * Reparenting into something opaque moves an element somewhere the user cannot see it. It used
     * to be allowed: a Sub-Process counted as a container whatever its state, and the only thing
     * in the way was the size guard — a collapsed one is 100x80, exactly big enough for a Task.
     */
    place('SubProcess_1', 'subProcess', { x: 100, y: 100, width: 400, height: 300 }, { isExpanded: false });

    expect(targetAt(graph, { x: 300, y: 200 }, 'task', noSnap).containerId).toBeNull();
  });

  it('and one that says nothing about whether it is expanded', () => {
    place('SubProcess_1', 'subProcess', { x: 100, y: 100, width: 400, height: 300 }, { isExpanded: null });

    expect(targetAt(graph, { x: 300, y: 200 }, 'task', noSnap).containerId).toBeNull();
  });

  it('refuses a container smaller than the thing being dropped', () => {
    place('SubProcess_1', 'subProcess', { x: 280, y: 190, width: 40, height: 30 });

    expect(targetAt(graph, { x: 300, y: 200 }, 'task', noSnap).containerId).toBeNull();
  });
});

describe('creationPointIn', () => {
  it('uses the preferred point when nothing is there', () => {
    expect(creationPointIn(graph, { x: 300, y: 200 }, 'task', noSnap).bounds)
      .toEqual({ x: 250, y: 160, width: 100, height: 80 });
  });

  it('steps clear of an element already at the preferred point', () => {
    place('Task_1', 'task', { x: 250, y: 160, width: 100, height: 80 });

    const bounds = creationPointIn(graph, { x: 300, y: 200 }, 'task', noSnap).bounds;

    // Otherwise pressing Enter twice would stack the second element on the first and DI-011 would
    // refuse it — correct, and useless to a keyboard user with no way to aim elsewhere.
    expect(bounds.x).toBeGreaterThan(250);
    expect(bounds.y).toBeGreaterThan(160);
  });

  it('steps by the grid when snapping is on, so the result stays on the grid', () => {
    place('Task_1', 'task', { x: 250, y: 160, width: 100, height: 80 });

    const bounds = creationPointIn(graph, { x: 300, y: 200 }, 'task', snapping).bounds;

    expect(bounds.x % 10).toBe(0);
    expect(bounds.y % 10).toBe(0);
  });

  it('does not treat a container at the preferred point as occupied', () => {
    place('SubProcess_1', 'subProcess', { x: 100, y: 100, width: 400, height: 300 });

    const target = creationPointIn(graph, { x: 300, y: 200 }, 'task', noSnap);

    // Dropping into a Sub-Process is how one gets filled; stepping out of it would be the bug.
    expect(target.bounds).toEqual({ x: 250, y: 160, width: 100, height: 80 });
    expect(target.containerId).toBe('SubProcess_1');
  });

  it('gives up rather than searching forever on a saturated plane', () => {
    for (let index = 0; index < 60; index += 1) {
      place(`Task_${String(index)}`, 'task', { x: 250 + (index * 20), y: 160 + (index * 20), width: 100, height: 80 });
    }

    // Bounded: the creation is then refused by DI-011, which is a message the user can act on.
    // An unbounded walk is not.
    expect(creationPointIn(graph, { x: 300, y: 200 }, 'task', noSnap).bounds.x).toBe(250 + (40 * 20));
  });
});
