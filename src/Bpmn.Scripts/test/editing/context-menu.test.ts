/**
 * @vitest-environment jsdom
 *
 * A real DOM, because this module is nothing but event suppression and target resolution. Both are
 * statements about the DOM, and testing them against a fake would be testing the fake.
 */
import { dia } from '@joint/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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
let handlers: Map<string, () => void>;
let views: Map<string, dia.CellView>;
let pan: { x: number; y: number };
let scale: number;

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal('ResizeObserver', class {
    observe(): void { /* Resize is dispatched explicitly. */ }
    disconnect(): void { /* No native observer in jsdom. */ }
  });
  handlers = new Map();
  views = new Map();
  pan = { x: 0, y: 0 };
  scale = 1;
  graph = new dia.Graph();
  host = document.createElement('div');
  document.body.appendChild(host);

  paper = {
    el: host,
    model: graph,
    clientToLocalPoint: (point: { x: number; y: number }) => point,
    clientToLocalRect: (x: number, y: number, width: number, height: number) => ({ x, y, width, height }),
    scale: () => ({ sx: 1, sy: 1 }),
    findViewByModel: (cell: dia.Cell) => views.get(String(cell.id)),
    on: (events: string, callback: () => void) => {
      for (const event of events.split(' ')) handlers.set(event, callback);
    },
    off: (events: string) => {
      for (const event of events.split(' ')) handlers.delete(event);
    },
    localToClientRect: (x: number, y: number, width: number, height: number) => ({
      x: x * scale + pan.x, y: y * scale + pan.y, width: width * scale, height: height * scale,
    }),
  } as unknown as dia.Paper;

  requests = [];
  selection = [];

  controller = new ContextMenuController(host, paper, 500, {
    requested: (payload) => { requests.push(payload); },
    selection: () => selection,
  }).attach();
});

afterEach(() => {
  controller.dispose();
  host.remove();
  vi.unstubAllGlobals();
  vi.useRealTimers();
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

describe('keeping the open menu anchored', () => {
  function menuFor(id: string | null): HTMLDivElement {
    const menu = document.createElement('div');
    menu.className = 'bpmn-menu';
    if (id !== null) menu.dataset.bpmnElement = id;
    menu.innerHTML = '<input value="unsaved">';
    host.append(menu);
    return menu;
  }

  async function layout(): Promise<void> {
    await vi.advanceTimersByTimeAsync(40);
  }

  it('projects a blank menu point after pan and zoom, even when markup arrives later', async () => {
    rightClick(host, { x: 120, y: 64 });
    pan = { x: 20, y: -10 };
    scale = 2;
    handlers.get('translate')?.();
    const menu = menuFor(null);
    await layout();

    expect(menu.style.left).toBe('260px');
    expect(menu.style.top).toBe('118px');
    expect(requests).toHaveLength(1);
  });

  it.each(['element', 'link'])('remeasures a %s on scroll without reopening or moving focus', async (kind) => {
    const cell = kind === 'element' ? place('Task_1') : new dia.Link({ id: 'Task_1', type: 'standard.Link' });
    if (kind === 'link') graph.addCell(cell);
    const el = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    el.setAttribute('model-id', 'Task_1');
    let rect = new DOMRect(40, 50, 100, 80);
    el.getBoundingClientRect = () => rect;
    host.append(el);
    views.set('Task_1', { el, model: cell } as unknown as dia.CellView);
    rightClick(el);
    const menu = menuFor('Task_1');
    const input = menu.querySelector('input')!;
    input.focus();
    await layout();
    expect(menu.style.top).toBe('130px');

    rect = new DOMRect(80, 20, 120, 60);
    window.dispatchEvent(new Event('scroll'));
    await layout();
    expect(menu.style.left).toBe('80px');
    expect(menu.style.top).toBe('80px');
    expect(document.activeElement).toBe(input);
    expect(input.value).toBe('unsaved');
    expect(requests).toHaveLength(1);
  });

  it('repairs the inline position after a Blazor rerender without an observer loop', async () => {
    rightClick(host);
    const menu = menuFor(null);
    await layout();
    pan.x = 90;
    handlers.get('scale')?.();
    await layout();
    menu.style.left = '40px';
    await layout();
    expect(menu.style.left).toBe('130px');
    await layout();
    expect(vi.getTimerCount()).toBe(0);
  });

  it('includes native host scroll offsets and follows nested scrolling', async () => {
    rightClick(host);
    const menu = menuFor(null);
    host.scrollLeft = 25;
    host.scrollTop = 30;
    host.dispatchEvent(new Event('scroll'));
    await layout();
    expect(menu.style.left).toBe('65px');
    expect(menu.style.top).toBe('80px');
  });

  it('ignores markup for an old target and tracks the latest invocation after reopening', async () => {
    rightClick(host);
    const old = menuFor('Task_1');
    await layout();
    expect(old.style.left).toBe('');
    old.remove();
    rightClick(host, { x: 200, y: 100 });
    const current = menuFor(null);
    await layout();
    expect(current.style.left).toBe('200px');
    expect(old.style.left).toBe('');
  });

  it('cancels pending work and removes listeners when disposed', async () => {
    rightClick(host);
    const menu = menuFor(null);
    await layout();
    pan.x = 90;
    handlers.get('translate')?.();
    controller.dispose();
    window.dispatchEvent(new Event('scroll'));
    await layout();
    expect(menu.style.left).toBe('40px');
    expect(handlers.size).toBe(0);
    expect(vi.getTimerCount()).toBe(0);
  });
});

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
