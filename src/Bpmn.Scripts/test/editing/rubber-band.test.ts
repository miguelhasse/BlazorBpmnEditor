/**
 * @vitest-environment jsdom
 *
 * A real DOM, because this module is nothing but pointer events and an overlay element. Testing it
 * against a fake would be testing the fake.
 */
import { dia } from '@joint/core';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { RubberBandController } from '../../src/editing/rubber-band.js';

/**
 * Rectangular selection.
 *
 * Driven through real pointer events against a real DOM and a real graph, because the whole
 * feature is a gesture: the geometry is a handful of comparisons and every way it goes wrong is
 * about which events it takes and which it leaves alone.
 *
 * The paper is the one thing stubbed. `dia.Paper` cannot be constructed under jsdom — @joint/core's
 * circular imports leave the DOM helpers half-initialised there — and the only thing this module
 * asks of it is a host element, a graph, and a client-to-local transform. The stub makes that
 * transform the identity, which is what a paper at 100% with no translation does anyway.
 */
let graph: dia.Graph;
let paper: dia.Paper;
let host: HTMLElement;
let selected: string[][];
let controller: RubberBandController;

beforeEach(() => {
  graph = new dia.Graph();
  host = document.createElement('div');
  document.body.appendChild(host);

  paper = {
    el: host,
    model: graph,
    clientToLocalPoint: (point: { x: number; y: number }) => point,
  } as unknown as dia.Paper;

  selected = [];
  controller = new RubberBandController(paper, { selected: (ids) => { selected.push([...ids]); } });
});

const place = (
  id: string,
  box: { x: number; y: number; width: number; height: number },
): dia.Element => {
  const element = new dia.Element({
    id,
    type: 'standard.Rectangle',
    position: { x: box.x, y: box.y },
    size: { width: box.width, height: box.height },
  });

  graph.addCell(element);
  return element;
};

/** A pointer gesture: down on the host, moves and up on the document. */
const drag = (
  from: { x: number; y: number },
  to: { x: number; y: number },
  init: PointerEventInit = { shiftKey: true },
): void => {
  host.dispatchEvent(new PointerEvent('pointerdown', {
    clientX: from.x, clientY: from.y, button: 0, pointerId: 1, bubbles: true, ...init,
  }));
  document.dispatchEvent(new PointerEvent('pointermove', {
    clientX: to.x, clientY: to.y, pointerId: 1, bubbles: true, ...init,
  }));
  document.dispatchEvent(new PointerEvent('pointerup', {
    clientX: to.x, clientY: to.y, pointerId: 1, bubbles: true, ...init,
  }));
};

describe('RubberBandController', () => {
  it('selects a shape the band fully contains', () => {
    place('Task_1', { x: 100, y: 100, width: 100, height: 80 });

    drag({ x: 50, y: 50 }, { x: 300, y: 300 });

    expect(selected).toEqual([['Task_1']]);
  });

  it('leaves out a shape the band only clips', () => {
    place('Task_1', { x: 100, y: 100, width: 100, height: 80 });

    // Containment, not intersection: a band that merely touches a shape would also select every
    // container it happens to be drawn inside.
    drag({ x: 50, y: 50 }, { x: 150, y: 150 });

    expect(selected).toEqual([[]]);
  });

  it('does not select the container the band was drawn inside', () => {
    place('Pool_1', { x: 0, y: 0, width: 800, height: 400 });
    place('Task_1', { x: 100, y: 100, width: 100, height: 80 });

    drag({ x: 50, y: 50 }, { x: 300, y: 300 });

    // The Pool encloses the band rather than the other way round, so pool-scoped selection needs
    // no special case at all.
    expect(selected).toEqual([['Task_1']]);
  });

  it('selects a container without also selecting its children', () => {
    const sub = place('Sub_1', { x: 100, y: 100, width: 300, height: 200 });
    const child = place('Task_1', { x: 140, y: 140, width: 100, height: 80 });
    sub.embed(child);

    drag({ x: 50, y: 50 }, { x: 500, y: 400 });

    // Both are inside the band. Keeping both would move the child twice when the selection is
    // dragged: once for itself and once with its parent.
    expect(selected).toEqual([['Sub_1']]);
  });

  it('reports an empty selection for a band that caught nothing', () => {
    place('Task_1', { x: 500, y: 500, width: 100, height: 80 });

    drag({ x: 0, y: 0 }, { x: 100, y: 100 });

    expect(selected).toEqual([[]]);
  });

  it('ignores a drag without Shift, which still pans', () => {
    place('Task_1', { x: 100, y: 100, width: 100, height: 80 });

    drag({ x: 50, y: 50 }, { x: 300, y: 300 }, { shiftKey: false });

    expect(selected).toEqual([]);
  });

  it('ignores a secondary-button drag', () => {
    place('Task_1', { x: 100, y: 100, width: 100, height: 80 });

    host.dispatchEvent(new PointerEvent('pointerdown', {
      clientX: 50, clientY: 50, button: 2, pointerId: 1, shiftKey: true, bubbles: true,
    }));
    document.dispatchEvent(new PointerEvent('pointerup', {
      clientX: 300, clientY: 300, pointerId: 1, shiftKey: true, bubbles: true,
    }));

    expect(selected).toEqual([]);
  });

  it('treats a Shift-click that never moved as a click, not a select-nothing', () => {
    place('Task_1', { x: 100, y: 100, width: 100, height: 80 });

    // Otherwise Shift-clicking a shape to add it would immediately clear the selection again.
    drag({ x: 50, y: 50 }, { x: 51, y: 51 });

    expect(selected).toEqual([]);
  });

  it('draws the band only once the drag is deliberate', () => {
    host.dispatchEvent(new PointerEvent('pointerdown', {
      clientX: 50, clientY: 50, button: 0, pointerId: 1, shiftKey: true, bubbles: true,
    }));
    document.dispatchEvent(new PointerEvent('pointermove', {
      clientX: 52, clientY: 52, pointerId: 1, shiftKey: true, bubbles: true,
    }));

    expect(host.querySelector('.bpmn-rubber-band')).toBeNull();

    document.dispatchEvent(new PointerEvent('pointermove', {
      clientX: 200, clientY: 200, pointerId: 1, shiftKey: true, bubbles: true,
    }));

    expect(host.querySelector('.bpmn-rubber-band')).not.toBeNull();
  });

  it('removes the band when the gesture ends', () => {
    drag({ x: 50, y: 50 }, { x: 300, y: 300 });

    expect(host.querySelector('.bpmn-rubber-band')).toBeNull();
    expect(controller.isActive).toBe(false);
  });

  it('abandons the band on Escape without reporting a selection', () => {
    place('Task_1', { x: 100, y: 100, width: 100, height: 80 });

    host.dispatchEvent(new PointerEvent('pointerdown', {
      clientX: 50, clientY: 50, button: 0, pointerId: 1, shiftKey: true, bubbles: true,
    }));
    document.dispatchEvent(new PointerEvent('pointermove', {
      clientX: 300, clientY: 300, pointerId: 1, shiftKey: true, bubbles: true,
    }));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(host.querySelector('.bpmn-rubber-band')).toBeNull();

    document.dispatchEvent(new PointerEvent('pointerup', {
      clientX: 300, clientY: 300, pointerId: 1, shiftKey: true, bubbles: true,
    }));

    expect(selected).toEqual([]);
  });

  it('ignores a gesture that began on a cell', () => {
    place('Task_1', { x: 100, y: 100, width: 100, height: 80 });

    const cell = document.createElement('div');
    cell.className = 'joint-cell';
    host.appendChild(cell);

    cell.dispatchEvent(new PointerEvent('pointerdown', {
      clientX: 50, clientY: 50, button: 0, pointerId: 1, shiftKey: true, bubbles: true,
    }));
    document.dispatchEvent(new PointerEvent('pointerup', {
      clientX: 300, clientY: 300, pointerId: 1, shiftKey: true, bubbles: true,
    }));

    expect(selected).toEqual([]);
  });

  it('stops listening once destroyed', () => {
    const report = vi.fn();
    const throwaway = new RubberBandController(paper, { selected: report });
    throwaway.destroy();

    drag({ x: 50, y: 50 }, { x: 300, y: 300 });

    expect(report).not.toHaveBeenCalled();
  });
});
