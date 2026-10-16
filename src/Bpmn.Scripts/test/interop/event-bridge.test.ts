import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { ViewportChangedPayload } from '../../src/interop/contracts.js';
import type { DotNetObjectRef } from '../../src/interop/dotnet.js';
import { BpmnEventBridge, VIEWPORT_REPORT_INTERVAL_MS } from '../../src/interop/event-bridge.js';

/** The viewport reaches .NET at most every interval and always after the last change; discrete events stay immediate. */
function setup(): { bridge: BpmnEventBridge; calls: { method: string; payload: unknown }[] } {
  const calls: { method: string; payload: unknown }[] = [];
  const target = {
    invokeMethodAsync: vi.fn((method: string, payload: unknown) => {
      calls.push({ method, payload });
      return Promise.resolve(undefined);
    }),
  } as unknown as DotNetObjectRef;
  return { bridge: new BpmnEventBridge(target), calls };
}

const viewport = (zoom: number): ViewportChangedPayload => ({ zoom, translateX: 0, translateY: 0 });

describe('BpmnEventBridge viewport throttling', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('sends a burst of viewport changes as one call carrying the last one, after the interval', () => {
    const { bridge, calls } = setup();

    for (let i = 1; i <= 60; i++) bridge.viewportChanged(viewport(i));

    expect(calls).toEqual([]);

    vi.advanceTimersByTime(VIEWPORT_REPORT_INTERVAL_MS);

    expect(calls).toEqual([{ method: 'ViewportChanged', payload: viewport(60) }]);
  });

  it('keeps the rate at one call per interval during a long gesture, and delivers the end of it', () => {
    const { bridge, calls } = setup();

    for (let frame = 0; frame < 60; frame++) {
      bridge.viewportChanged(viewport(frame + 1));
      vi.advanceTimersByTime(16);
    }
    vi.advanceTimersByTime(VIEWPORT_REPORT_INTERVAL_MS);

    expect(calls.length).toBeLessThanOrEqual(11);
    expect(calls.at(-1)?.payload).toEqual(viewport(60));
  });

  it('delivers the pending viewport when disposed, then nothing more', () => {
    const { bridge, calls } = setup();

    bridge.viewportChanged(viewport(2));
    bridge.dispose();
    bridge.viewportChanged(viewport(3));
    vi.advanceTimersByTime(VIEWPORT_REPORT_INTERVAL_MS * 2);

    expect(calls).toEqual([{ method: 'ViewportChanged', payload: viewport(2) }]);
  });

  it('keeps selection changes on the microtask, coalesced to the latest', async () => {
    const { bridge, calls } = setup();

    bridge.selectionChanged({ ids: ['a'] } as never);
    bridge.selectionChanged({ ids: ['b'] } as never);
    await Promise.resolve();

    expect(calls).toEqual([{ method: 'SelectionChanged', payload: { ids: ['b'] } }]);
  });
});
