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

function fakeInstance(): {
  instance: BpmnCanvasInstance;
  deleteElements: ReturnType<typeof vi.fn>;
  dispose: ReturnType<typeof vi.fn>;
  setViewport: ReturnType<typeof vi.fn>;
  loadDocument: ReturnType<typeof vi.fn>;
  patchDocument: ReturnType<typeof vi.fn>;
} {
  const deleteElements = vi.fn(() => true);
  const dispose = vi.fn();
  const setViewport = vi.fn();
  const loadDocument = vi.fn(() => ({ diagramId: 'd', shapeCount: 1, edgeCount: 0, unrenderedElementIds: [] }));
  const patchDocument = vi.fn(() => ({ diagramId: 'd', shapeCount: 1, edgeCount: 0, unrenderedElementIds: [] }));

  const instance = {
    dispose,
    deleteElements,
    setViewport,
    editLabel: vi.fn(() => true),
    loadDocument,
    patchDocument,
  } as unknown as BpmnCanvasInstance;

  return { instance, deleteElements, dispose, setViewport, loadDocument, patchDocument };
}

describe('CanvasHandle', () => {
  it('reports what initialisation produced', () => {
    const handle = new CanvasHandle(fakeInstance().instance, initResult(true));

    expect(handle.getInitResult()).toEqual(initResult(true));
  });

  it('forwards calls to its canvas', () => {
    const { instance, deleteElements, setViewport, loadDocument, patchDocument } = fakeInstance();
    const handle = new CanvasHandle(instance, initResult(true));

    handle.setViewport({ kind: 'zoomIn' });

    expect(setViewport).toHaveBeenCalledWith({ kind: 'zoomIn' });
    expect(handle.editLabel('Task_1')).toBe(true);
    expect(handle.loadDocument({} as never, null)?.shapeCount).toBe(1);
    expect(handle.deleteElements(['Task_1', 'Flow_1'])).toBe(true);
    expect(deleteElements).toHaveBeenCalledWith(['Task_1', 'Flow_1'], undefined);
    const patch = { baseRevision: 0, revision: 1, diagramId: null, collections: [], planes: [] };
    expect(handle.patchDocument(patch)?.shapeCount).toBe(1);
    expect(patchDocument).toHaveBeenCalledWith(patch);
    handle.loadDocument({} as never, 'd', 7);
    expect(loadDocument).toHaveBeenCalledWith({}, 'd', 7);
  });

  it('answers neutrally when the configuration was rejected and nothing was built', () => {
    const handle = new CanvasHandle(null, initResult(false));

    expect(handle.getInitResult().accepted).toBe(false);
    expect(handle.loadDocument({} as never, null)).toBeNull();
    expect(handle.patchDocument({} as never)).toBeNull();
    expect(handle.deleteElements(['Task_1'])).toBe(false);
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
