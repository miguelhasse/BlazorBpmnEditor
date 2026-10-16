import { describe, expect, it, vi } from 'vitest';

import type { BpmnCanvasInstance } from '../../src/canvas/bpmn-canvas-instance.js';
import { CanvasHandle } from '../../src/canvas/canvas-handle.js';
import { INTEROP_CONTRACT_VERSION } from '../../src/interop/contract-version.js';
import type { CanvasInitResult } from '../../src/interop/contracts.js';

/**
 * The handle is what .NET holds for a canvas, so these pin the two things it owns: forwarding
 * exactly the contract's calls, and answering neutrally once there is no canvas behind it.
 */
function initResult(accepted: boolean): CanvasInitResult {
  return {
    instanceId: accepted ? 'canvas-1' : '',
    contractVersion: INTEROP_CONTRACT_VERSION,
    configRevision: 1,
    accepted,
    rejection: null,
  };
}

function fakeInstance(): { instance: BpmnCanvasInstance; dispose: ReturnType<typeof vi.fn>; setViewport: ReturnType<typeof vi.fn> } {
  const dispose = vi.fn();
  const setViewport = vi.fn();

  const instance = {
    dispose,
    setViewport,
    editLabel: vi.fn(() => true),
    loadDocument: vi.fn(() => ({ diagramId: 'd', shapeCount: 1, edgeCount: 0, unrenderedElementIds: [] })),
  } as unknown as BpmnCanvasInstance;

  return { instance, dispose, setViewport };
}

describe('CanvasHandle', () => {
  it('reports what initialisation produced', () => {
    const handle = new CanvasHandle(fakeInstance().instance, initResult(true));

    expect(handle.getInitResult()).toEqual(initResult(true));
  });

  it('forwards calls to its canvas', () => {
    const { instance, setViewport } = fakeInstance();
    const handle = new CanvasHandle(instance, initResult(true));

    handle.setViewport({ kind: 'zoomIn' });

    expect(setViewport).toHaveBeenCalledWith({ kind: 'zoomIn' });
    expect(handle.editLabel('Task_1')).toBe(true);
    expect(handle.loadDocument({} as never, null)?.shapeCount).toBe(1);
  });

  it('answers neutrally when the configuration was rejected and nothing was built', () => {
    const handle = new CanvasHandle(null, initResult(false));

    expect(handle.getInitResult().accepted).toBe(false);
    expect(handle.loadDocument({} as never, null)).toBeNull();
    expect(handle.editLabel('Task_1')).toBe(false);
    expect(handle.resolveDrop(0, 0, 'task')).toBeNull();
    expect(() => { handle.setViewport({ kind: 'zoomIn' }); }).not.toThrow();
  });

  it('disposes its canvas once, and answers neutrally afterwards', () => {
    const { instance, dispose } = fakeInstance();
    const onDisposed = vi.fn();
    const handle = new CanvasHandle(instance, initResult(true), onDisposed);

    handle.dispose();
    handle.dispose();

    expect(dispose).toHaveBeenCalledTimes(1);
    expect(onDisposed).toHaveBeenCalledTimes(1);
    expect(handle.editLabel('Task_1')).toBe(false);
  });

  it('is left inert even when disposing the canvas throws', () => {
    const { instance, dispose } = fakeInstance();
    dispose.mockImplementation(() => { throw new Error('boom'); });
    const handle = new CanvasHandle(instance, initResult(true));

    expect(() => { handle.dispose(); }).toThrow('boom');
    expect(handle.editLabel('Task_1')).toBe(false);
  });
});
