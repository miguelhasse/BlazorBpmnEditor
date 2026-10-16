// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest';
import type { dia } from '@joint/core';

import { ExpandControls } from '../../src/editing/expand-controls.js';
import { canvasStrings } from '../fixtures/canvas-strings.js';

describe('ExpandControls', () => {
  it('uses translated button text and refreshes names when model changes', () => {
    const host = document.createElement('div');
    let name = 'Orders {0}';
    const model = {
      id: 'Sub_1', get: () => ({ name, expandMarker: { x: 0, y: 0, size: 14 } }),
      position: () => ({ x: 0, y: 0 }), findView: () => ({}),
    };
    const on = vi.fn();
    const paper = {
      on, off: vi.fn(), localToClientPoint: (point: unknown) => point,
      model: { on: vi.fn(), off: vi.fn(), getElements: () => [model] },
      findViewByModel: () => ({}),
    } as unknown as dia.Paper;
    let frame: FrameRequestCallback | undefined;
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => { frame = callback; return 1; });
    vi.stubGlobal('cancelAnimationFrame', vi.fn());
    const requested = vi.fn();
    const controls = new ExpandControls(paper, host, { expandRequested: requested, collapseRequested: vi.fn() },
      { values: { ...canvasStrings.values, ExpandNamed: 'Ouvrir {0}' } }).attach();
    frame?.(0);

    const button = host.querySelector('button');
    expect(button?.getAttribute('aria-label')).toBe('Ouvrir Orders {0}');
    expect(button?.title).toBe('Ouvrir Orders {0}');
    button?.click();
    expect(requested).toHaveBeenCalledWith('Sub_1');
    name = 'Renamed';
    const schedule = on.mock.calls[0]?.[1] as () => void;
    schedule();
    frame?.(0);
    expect(button?.getAttribute('aria-label')).toBe('Ouvrir Renamed');
    controls.dispose();
    expect(host.children).toHaveLength(0);
    vi.unstubAllGlobals();
  });
});
