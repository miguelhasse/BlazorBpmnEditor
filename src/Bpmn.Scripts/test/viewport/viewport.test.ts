// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { dia } from '@joint/core';

import type { AcceptedConfig } from '../../src/config/accepted-config.js';
import type { ViewportChangedPayload } from '../../src/interop/contracts.js';
import { ViewportController } from '../../src/viewport/viewport.js';

/**
 * Wheel and pointer input lands once per animation frame; commands land at once and after the input that preceded them.
 *
 * The paper is a stand-in that records its transform: what is being pinned is the order and the number of writes, not JointJS.
 */
function fakePaper(): { paper: dia.Paper; el: HTMLElement; writes: string[]; state: { sx: number; tx: number; ty: number } } {
  const el = document.createElement('div');
  const state = { sx: 1, tx: 0, ty: 0 };
  const writes: string[] = [];
  const area = { containsRect: () => true, clone: () => area, inflate: () => area, width: 100, height: 100 };

  const paper = {
    el,
    scale: (sx?: number) => {
      if (sx !== undefined) {
        state.sx = sx;
        writes.push(`scale ${sx.toFixed(3)}`);
      }
      return { sx: state.sx, sy: state.sx };
    },
    translate: (tx?: number, ty?: number) => {
      if (tx !== undefined && ty !== undefined) {
        state.tx = tx;
        state.ty = ty;
        writes.push(`translate ${tx.toFixed(1)},${ty.toFixed(1)}`);
      }
      return { tx: state.tx, ty: state.ty };
    },
    getArea: () => area,
    updateCellsVisibility: vi.fn(),
    model: { getCell: () => undefined },
  } as unknown as dia.Paper;

  return { paper, el, writes, state };
}

function config(overrides: Partial<AcceptedConfig['viewport']> = {}): AcceptedConfig {
  return {
    isReadOnly: false,
    viewport: {
      minZoom: 0.2,
      maxZoom: 4,
      zoomStep: 1.1,
      fitPadding: 20,
      wheelMode: 'pan',
      wheelPanSpeed: 1,
      dragPanEnabled: true,
      touchEnabled: true,
      ...overrides,
    },
  } as unknown as AcceptedConfig;
}

function wheel(el: HTMLElement, init: WheelEventInit): void {
  el.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, ...init }));
}

describe('ViewportController input coalescing', () => {
  let reports: ViewportChangedPayload[];

  beforeEach(() => {
    vi.useFakeTimers();
    reports = [];
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function controller(options: Partial<AcceptedConfig['viewport']> = {}) {
    const fake = fakePaper();
    const viewport = new ViewportController(fake.paper, config(options), (payload) => reports.push(payload));
    return { ...fake, viewport };
  }

  it('applies a burst of wheel pans as one translate and one report at the next frame', () => {
    const { el, writes, viewport } = controller();

    for (let i = 0; i < 5; i++) wheel(el, { deltaY: 10 });

    expect(writes).toEqual([]);
    expect(reports).toEqual([]);

    vi.advanceTimersToNextFrame();

    expect(writes).toEqual(['translate 0.0,-50.0']);
    expect(reports).toHaveLength(1);
    viewport.destroy();
  });

  it('multiplies the zoom factors of a burst and clamps the product once', () => {
    const { el, state, viewport } = controller({ wheelMode: 'zoom' });

    for (let i = 0; i < 3; i++) wheel(el, { deltaY: -1 });
    vi.advanceTimersToNextFrame();

    expect(state.sx).toBeCloseTo(1.1 ** 3, 6);
    expect(reports).toHaveLength(1);

    for (let i = 0; i < 100; i++) wheel(el, { deltaY: -1 });
    vi.advanceTimersToNextFrame();

    expect(state.sx).toBe(4);
    viewport.destroy();
  });

  it('applies the pan before the zoom, so the focus point is where the pointer is now', () => {
    const { el, writes, viewport } = controller({ wheelMode: 'pan' });

    wheel(el, { deltaY: 20 });
    wheel(el, { deltaY: -1, ctrlKey: true });
    vi.advanceTimersToNextFrame();

    expect(writes[0]).toMatch(/^translate /);
    expect(writes.at(-1)).toMatch(/^scale /);
    viewport.destroy();
  });

  it('applies what is pending before a command, so a command is ordered after the input before it', () => {
    const { el, state, viewport } = controller();

    wheel(el, { deltaY: 30 });
    viewport.execute({ kind: 'panBy', dx: 5, dy: 5 });

    // The wheel's -30 landed first, then the command's +5; and nothing is left for the frame.
    expect(state.ty).toBe(-25);
    const reported = reports.length;
    vi.advanceTimersToNextFrame();
    expect(reports).toHaveLength(reported);
    viewport.destroy();
  });

  it('reports the committed transform when asked for the state mid-frame', () => {
    const { el, viewport } = controller();

    wheel(el, { deltaY: 40 });

    expect(viewport.state.translateY).toBe(-40);
    viewport.destroy();
  });

  it('applies and reports what is pending when it is destroyed, and nothing after', () => {
    const { el, state, viewport } = controller();

    wheel(el, { deltaY: 40 });
    viewport.destroy();

    expect(state.ty).toBe(-40);
    expect(reports).toHaveLength(1);

    wheel(el, { deltaY: 40 });
    vi.advanceTimersToNextFrame();

    expect(state.ty).toBe(-40);
    expect(reports).toHaveLength(1);
  });
});
