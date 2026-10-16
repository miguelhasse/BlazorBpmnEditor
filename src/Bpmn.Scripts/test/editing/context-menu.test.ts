/**
 * @vitest-environment jsdom
 *
 * A real DOM, because this module is nothing but event suppression and target resolution. Both are
 * statements about the DOM, and testing them against a fake would be testing the fake.
 */
import { dia } from '@joint/core';
import { beforeEach, describe, expect, it } from 'vitest';

import { ContextMenuController } from '../../src/editing/context-menu.js';
import type { ContextMenuRequestPayload } from '../../src/interop/contracts.js';

/**
 * Context-menu suppression and routing (specification section 7).
 *
 * The rule under test is absolute — the browser's own menu must never be reachable from inside the
 * canvas root, and every request must produce a payload — so most of what is asserted here is that
 * there is no gap: not over a shape, not over blank paper, not over a tool layer, not from the
 * keyboard, and not in read-only. The one thing that must *not* be suppressed is everything outside
 * the canvas root.
 *
 * The paper is stubbed for the reason the rubber-band tests give: `dia.Paper` cannot be constructed
 * under jsdom, and all this module asks of it is a graph and a client-to-local transform. The stub
 * makes that transform the identity, which is what a paper at 100% with no translation does.
 */
let graph: dia.Graph;
let paper: dia.Paper;
let host: HTMLElement;
let requests: ContextMenuRequestPayload[];
let selection: string[];
let controller: ContextMenuController;

beforeEach(() => {
  graph = new dia.Graph();
  host = document.createElement('div');
  document.body.appendChild(host);

  paper = {
    el: host,
    model: graph,
    clientToLocalPoint: (point: { x: number; y: number }) => point,
    clientToLocalRect: (x: number, y: number, width: number, height: number) => ({ x, y, width, height }),
    scale: () => ({ sx: 1, sy: 1 }),
    findViewByModel: () => undefined,
  } as unknown as dia.Paper;

  requests = [];
  selection = [];

  controller = new ContextMenuController(host, paper, 500, {
    requested: (payload) => { requests.push(payload); },
    selection: () => selection,
  }).attach();
});

const place = (id: string): dia.Element => {
  const element = new dia.Element({
    id,
    type: 'standard.Rectangle',
    position: { x: 10, y: 20 },
    size: { width: 100, height: 80 },
  });

  graph.addCell(element);
  return element;
};

/** A node inside the canvas that JointJS has stamped with a cell id. */
const cellNode = (id: string, className = 'joint-cell'): HTMLElement => {
  const node = document.createElement('div');
  node.className = className;
  node.setAttribute('model-id', id);
  host.appendChild(node);
  return node;
};

const rightClick = (target: EventTarget, at = { x: 40, y: 50 }): MouseEvent => {
  const event = new MouseEvent('contextmenu', {
    bubbles: true,
    cancelable: true,
    clientX: at.x,
    clientY: at.y,
  });

  target.dispatchEvent(event);
  return event;
};

describe('suppressing the browser menu', () => {
  it('prevents the default over blank paper and still asks for a menu', () => {
    const event = rightClick(host);

    expect(event.defaultPrevented).toBe(true);
    expect(requests).toHaveLength(1);
    expect(requests[0]?.target).toBe('canvas');
    expect(requests[0]?.elementId).toBeNull();
  });

  it('gives a blank-canvas request a zero-size anchor at the point', () => {
    rightClick(host, { x: 120, y: 64 });

    // Section 7 names this exactly: the anchor is the invocation point, not a rectangle.
    expect(requests[0]?.anchor.bounds).toEqual({ x: 120, y: 64, width: 0, height: 0 });
  });

  it('leaves the browser menu alone outside the canvas root', () => {
    const outside = document.createElement('div');
    document.body.appendChild(outside);

    const event = rightClick(outside);

    expect(event.defaultPrevented).toBe(false);
    expect(requests).toHaveLength(0);
  });
});

describe('resolving the target', () => {
  it('resolves a shape to its element', () => {
    place('Task_1');
    rightClick(cellNode('Task_1'));

    expect(requests[0]?.target).toBe('element');
    expect(requests[0]?.elementId).toBe('Task_1');
  });

  it('suppresses the custom menu for a Group while still preventing the browser menu', () => {
    graph.addCell(new dia.Element({
      id: 'Group_1',
      type: 'bpmn.Group',
      bpmn: { elementType: 'group' },
    }));

    const event = rightClick(cellNode('Group_1'));

    expect(event.defaultPrevented).toBe(true);
    expect(requests).toHaveLength(0);
  });

  it('resolves a connector to a link target', () => {
    graph.addCell(new dia.Link({ id: 'Flow_1', type: 'standard.Link' }));
    rightClick(cellNode('Flow_1'));

    expect(requests[0]?.target).toBe('link');
    expect(requests[0]?.elementId).toBe('Flow_1');
  });

  it('resolves a tool handle to the element that owns it', () => {
    // Section 7: "overlay/tool targets resolve to their owning BPMN/DI object". JointJS stamps the
    // tools layer with the same model-id as the cell, which is what makes one rule cover both.
    place('Task_1');
    const tools = cellNode('Task_1', 'joint-tools');
    const handle = document.createElement('span');
    tools.appendChild(handle);

    rightClick(handle);

    expect(requests[0]?.elementId).toBe('Task_1');
  });

  it('resolves a Blazor overlay to the element it is editing', () => {
    place('Task_1');
    const overlay = document.createElement('div');
    overlay.setAttribute('data-bpmn-element', 'Task_1');
    host.appendChild(overlay);

    rightClick(overlay);

    expect(requests[0]?.elementId).toBe('Task_1');
  });

  it('falls back to the canvas for an id nothing in the graph answers to', () => {
    rightClick(cellNode('Ghost_1'));

    expect(requests[0]?.target).toBe('canvas');
    expect(requests[0]?.elementId).toBeNull();
  });
});

describe('the other ways in', () => {
  it('answers Shift+F10 and the Context Menu key', () => {
    for (const init of [{ key: 'F10', shiftKey: true }, { key: 'ContextMenu' }]) {
      const event = new KeyboardEvent('keydown', { ...init, bubbles: true, cancelable: true });
      host.dispatchEvent(event);
      expect(event.defaultPrevented).toBe(true);
    }

    expect(requests).toHaveLength(2);
  });

  it('anchors a keyboard request on the single selected element', () => {
    place('Task_1');
    selection = ['Task_1'];

    host.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true }));

    expect(requests[0]?.elementId).toBe('Task_1');
    expect(requests[0]?.source).toBe('keyboard');
  });

  it('does not open the custom menu for a selected Group from the keyboard', () => {
    graph.addCell(new dia.Element({
      id: 'Group_1',
      type: 'bpmn.Group',
      bpmn: { elementType: 'group' },
    }));
    selection = ['Group_1'];

    const event = new KeyboardEvent('keydown', {
      key: 'ContextMenu',
      bubbles: true,
      cancelable: true,
    });
    host.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
    expect(requests).toHaveLength(0);
  });

  it('ignores an unrelated key, and F10 without Shift', () => {
    for (const key of ['F10', 'Enter', 'Escape']) {
      host.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    }

    expect(requests).toHaveLength(0);
  });

  it('carries the selection the commands will act on', () => {
    place('Task_1');
    selection = ['Task_1', 'Task_2'];

    rightClick(cellNode('Task_1'));

    expect(requests[0]?.selection).toEqual(['Task_1', 'Task_2']);
  });
});

describe('disposal', () => {
  it('stops suppressing once disposed', () => {
    controller.dispose();

    const event = rightClick(host);

    expect(event.defaultPrevented).toBe(false);
    expect(requests).toHaveLength(0);
  });

  it('is safe to dispose twice', () => {
    controller.dispose();
    expect(() => { controller.dispose(); }).not.toThrow();
  });
});
