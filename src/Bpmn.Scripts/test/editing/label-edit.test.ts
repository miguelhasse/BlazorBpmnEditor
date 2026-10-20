/** @vitest-environment jsdom */
import { dia } from '@joint/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { LabelEditController } from '../../src/editing/label-edit.js';
import * as constants from '../../src/geometry/constants.js';
import type { LabelEditRequestPayload } from '../../src/interop/contracts.js';
import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { createDataObjectCell, createDataStoreCell, createFlowNodeCell } from '../../src/shapes/registry.js';
import { flowNode, shape } from '../fixtures/documents.js';

describe('inline label geometry and live layout', () => {
  let host: HTMLElement;
  let graph: dia.Graph;
  let paper: dia.Paper;
  let controller: LabelEditController;
  let requests: LabelEditRequestPayload[];
  let views: Map<string, dia.CellView>;
  let text: string;
  let scale: number;
  let pan: { x: number; y: number };
  let handlers: Map<string, () => void>;
  const lineHeight = constants.LABEL_FONT_SIZE * constants.LABEL_LINE_HEIGHT;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal('SVGGraphicsElement', SVGElement);
    vi.stubGlobal('ResizeObserver', class {
      observe(): void { /* Layout changes are dispatched explicitly in these tests. */ }
      disconnect(): void { /* No native observation under jsdom. */ }
    });
    host = document.createElement('div');
    document.body.append(host);
    graph = new dia.Graph();
    requests = [];
    views = new Map();
    text = '';
    scale = 1;
    pan = { x: 0, y: 0 };
    handlers = new Map();
    paper = {
      model: graph,
      on: (events: string, callback: () => void) => {
        for (const event of events.split(' ')) handlers.set(event, callback);
      },
      off: (events: string) => {
        for (const event of events.split(' ')) handlers.delete(event);
      },
      scale: () => ({ sx: scale, sy: scale }),
      clientToLocalRect: (x: number, y: number, width: number, height: number) => ({
        x: (x - pan.x) / scale, y: (y - pan.y) / scale, width: width / scale, height: height / scale,
      }),
      localToClientRect: (x: number, y: number, width: number, height: number) => ({
        x: x * scale + pan.x, y: y * scale + pan.y, width: width * scale, height: height * scale,
      }),
      findViewByModel: (cell: dia.Cell) => views.get(String(cell.id)),
    } as unknown as dia.Paper;
    controller = new LabelEditController(host, paper, {
      editRequested: (request) => { requests.push(request); },
      textOf: () => text,
    }).attach();
  });

  afterEach(() => {
    controller.dispose();
    host.remove();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  function addShape(type: 'task' | 'startEvent' | 'exclusiveGateway' = 'task'): dia.Element {
    const cell = createFlowNodeCell(
      flowNode('Shape_1', type, { name: null }),
      shape('Shape_1', 100, 200, 120, 80, {
        label: { bounds: { x: 0, y: 0, width: 1000, height: 500 } },
      }),
      { metrics: new TableFontMetrics() },
    );
    graph.addCell(cell);
    addView(cell);
    return cell;
  }

  function addView(cell: dia.Cell, markup = ''): dia.CellView {
    const el = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    el.innerHTML = markup;
    host.append(el);
    const view = {
      model: cell,
      el,
      getPointAtRatio: () => ({ x: 450, y: 600 }),
    } as unknown as dia.CellView;
    views.set(String(cell.id), view);
    return view;
  }

  async function openInput(): Promise<HTMLTextAreaElement> {
    controller.edit('Shape_1');
    const box = document.createElement('div');
    box.className = 'bpmn-label-editor';
    box.dataset.bpmnElement = 'Shape_1';
    const input = document.createElement('textarea');
    input.value = 'unsaved';
    Object.defineProperty(input, 'scrollHeight', {
      get: () => input.value.split('\n').length * lineHeight * scale,
    });
    box.append(input);
    host.append(box);
    input.focus();
    await vi.advanceTimersByTimeAsync(20);
    return input;
  }

  it('opens an empty activity as one centered line, ignoring oversized imported bounds', () => {
    addShape();
    expect(controller.edit('Shape_1')).toBe(true);
    const box = requests[0]!.anchor.bounds;
    expect(box.height).toBeCloseTo(lineHeight);
    expect(box.width).toBeLessThanOrEqual(constants.EXTERNAL_LABEL_WIDTH);
    expect(box.x + box.width / 2).toBe(160);
    expect(box.y + box.height / 2).toBeCloseTo(240);
  });

  it.each(['startEvent', 'exclusiveGateway'] as const)('places an empty %s below its shape', (type) => {
    addShape(type);
    controller.edit('Shape_1');
    expect(requests[0]!.anchor.bounds.y).toBeGreaterThan(280);
    expect(requests[0]!.anchor.bounds.height).toBeCloseTo(lineHeight);
  });

  it.each(['object', 'store'] as const)('places an empty data %s label outside its shape', (kind) => {
    const context = { metrics: new TableFontMetrics() };
    const bounds = shape('Shape_1', 100, 200, 50, 60);
    const cell = kind === 'object'
      ? createDataObjectCell({
        id: 'Shape_1', name: null, dataObjectRef: null, state: null, isCollection: false, containerId: 'Process_1',
      }, bounds, context)
      : createDataStoreCell({
        id: 'Shape_1', name: null, dataStoreRef: null, containerId: 'Process_1',
      }, bounds, context);
    graph.addCell(cell);
    addView(cell);
    controller.edit('Shape_1');
    expect(requests[0]!.anchor.bounds.y).toBeGreaterThan(260);
    expect(requests[0]!.anchor.bounds.height).toBeCloseTo(lineHeight);
  });

  it('keeps an empty boundary label on the side facing away from its host', () => {
    const cell = createFlowNodeCell(
      flowNode('Shape_1', 'boundaryEvent', { attachedToRef: 'Host_1' }),
      shape('Shape_1', 132, 182, 36, 36),
      {
        metrics: new TableFontMetrics(),
        boundsOf: () => ({ x: 100, y: 200, width: 100, height: 80 }),
      },
    );
    graph.addCell(cell);
    addView(cell);
    controller.edit('Shape_1');
    const box = requests[0]!.anchor.bounds;
    expect(box.y + box.height).toBeLessThan(182);
    expect(box.x + box.width / 2).toBeCloseTo(150);
  });

  it('places an empty connector at its route midpoint, never over its full extent', () => {
    const link = new dia.Link({ id: 'Flow_1', type: 'bpmn.Connector' });
    graph.addCell(link);
    addView(link);
    controller.edit('Flow_1');
    const box = requests[0]!.anchor.bounds;
    expect(box.x + box.width / 2).toBe(450);
    expect(box.y + box.height / 2).toBeCloseTo(600);
    expect(box.height).toBeCloseTo(lineHeight);
  });

  it('does not open an editor before the connector path is available', () => {
    const link = new dia.Link({ id: 'Flow_1', type: 'bpmn.Connector' });
    graph.addCell(link);
    const view = addView(link);
    vi.spyOn(view as dia.LinkView, 'getPointAtRatio').mockReturnValue(null!);
    expect(controller.edit('Flow_1')).toBe(false);
    expect(requests).toHaveLength(0);
  });

  it('finds populated connector text and keeps font size in unscaled SVG units', () => {
    const link = new dia.Link({ id: 'Flow_1', type: 'bpmn.Connector' });
    graph.addCell(link);
    const view = addView(link, '<g class="label" label-idx="0"><text joint-selector="text" style="font-size:12px">name</text></g>');
    const label = view.el.querySelector('text')!;
    vi.spyOn(label, 'getBoundingClientRect').mockReturnValue(new DOMRect(200, 300, 90, 30));
    text = 'name';
    scale = 2;
    controller.edit('Flow_1');
    expect(requests[0]!.anchor.client).toEqual({ x: 200, y: 300, width: 90, height: 30 });
    expect(requests[0]!.fontSize).toBe(12);
    expect(requests[0]!.text).toBe('name');
  });

  it('grows and shrinks for explicit and trailing line breaks without reseeding the draft', async () => {
    addShape();
    const input = await openInput();
    input.value = 'one\ntwo\n';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await vi.advanceTimersByTimeAsync(20);
    expect(Number.parseFloat(input.style.height)).toBeCloseTo(lineHeight * 3);
    expect(document.activeElement).toBe(input);
    input.value = '';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await vi.advanceTimersByTimeAsync(20);
    expect(Number.parseFloat(input.style.height)).toBeCloseTo(lineHeight);
    expect(requests).toHaveLength(1);
  });

  it('tracks pan and zoom without replacing the textarea or losing its caret', async () => {
    addShape();
    const input = await openInput();
    input.setSelectionRange(2, 4);
    const box = input.parentElement!;
    const before = Number.parseFloat(box.style.left);
    pan.x = 100;
    handlers.get('translate')!();
    await vi.advanceTimersByTimeAsync(20);
    expect(Number.parseFloat(box.style.left)).toBeCloseTo(before + 100);
    scale = 2;
    handlers.get('scale')!();
    await vi.advanceTimersByTimeAsync(20);
    expect(Number.parseFloat(input.style.fontSize)).toBe(constants.LABEL_FONT_SIZE * 2);
    expect(input.value).toBe('unsaved');
    expect(input.selectionStart).toBe(2);
    expect(input.selectionEnd).toBe(4);
    expect(document.activeElement).toBe(input);
    expect(requests).toHaveLength(1);
  });

  it('reprojects the stored anchor when panning culls the view', async () => {
    addShape();
    const input = await openInput();
    const before = Number.parseFloat(input.parentElement!.style.left);
    views.clear();
    pan.x = -500;
    handlers.get('translate')!();
    await vi.advanceTimersByTimeAsync(20);
    expect(Number.parseFloat(input.parentElement!.style.left)).toBeCloseTo(before - 500);
    expect(input.value).toBe('unsaved');
  });

  it('retains the latest measured position when the moved shape is subsequently culled', async () => {
    const cell = addShape();
    const input = await openInput();
    cell.position(300, 200);
    await vi.advanceTimersByTimeAsync(20);
    const before = Number.parseFloat(input.parentElement!.style.left);
    views.clear();
    pan.x = -500;
    handlers.get('translate')!();
    await vi.advanceTimersByTimeAsync(20);
    expect(Number.parseFloat(input.parentElement!.style.left)).toBeCloseTo(before - 500);
    expect(input.value).toBe('unsaved');
  });

  it('removes viewport listeners and cancels queued layout when disposed', async () => {
    addShape();
    const input = await openInput();
    const before = input.parentElement!.style.left;
    pan.x = 100;
    handlers.get('translate')!();
    controller.dispose();
    await vi.advanceTimersByTimeAsync(20);
    expect(input.parentElement!.style.left).toBe(before);
    expect(handlers.size).toBe(0);
  });
});
