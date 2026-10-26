import { BUILD_MODE } from './BuildConstants.js';
import { BpmnCanvasInstance } from './canvas/bpmn-canvas-instance.js';
import { CanvasHandle } from './canvas/canvas-handle.js';
import { validateAndAccept } from './config/accepted-config.js';
import { INTEROP_CONTRACT_VERSION } from './interop/contract-version.js';
import type { EditorConfigContract } from './interop/contracts.js';
import type { DotNetObjectRef } from './interop/dotnet.js';
import { BpmnEventBridge } from './interop/event-bridge.js';
import type { BpmnDocumentContract } from './model/contracts.js';

export { showDialog, closeDialog } from './interop/dialog.js';

/**
 * The public module surface imported by BpmnCanvas.
 *
 * Nothing else in this bundle is reachable from Blazor. `initialize` is the one entry point, and
 * it returns a `CanvasHandle`, which .NET receives as an `IJSObjectReference` (Blazor wraps the
 * return value itself when the call asks for one, so it must not be wrapped here as well). Every
 * later call is made on that handle, whose method names are part of the contract
 * (scripts/verify-bundle.mjs asserts they survive minification).
 *
 * Initialisation order matters and is deliberate: configuration is validated before any
 * JointJS object exists, so a rejected snapshot leaves nothing half-built and the canvas
 * never appears briefly with defaults the host did not choose (implementation plan
 * section 2, "Blazor-owned configuration flow").
 */

/**
 * @param hostElement
 * The canvas root. Context-menu suppression, keyboard handling and overlay coordinates are all
 * scoped to it, and Blazor owns its children.
 * @param paperElement
 * The element JointJS mounts into, which is a child of the host and is owned entirely by this
 * module. Separate because Blazor and JointJS cannot both manage one element's children.
 * @param document The document to render, or null to start empty.
 * @param config The validated configuration snapshot.
 * @param bridgeRef The .NET object every event is reported through.
 * @returns A reference to the canvas. When the configuration was rejected nothing was built, and
 * the handle says so through `getInitResult` and ignores every other call.
 */
export async function initialize(
  hostElement: HTMLElement,
  paperElement: HTMLElement,
  document: BpmnDocumentContract | null,
  config: EditorConfigContract,
  bridgeRef: DotNetObjectRef,
): Promise<CanvasHandle> {
  const bridge = new BpmnEventBridge(bridgeRef);
  const result = validateAndAccept(config);

  if (!result.ok) {
    await bridge.configRejected(result.rejection);

    return new CanvasHandle(null, {
      instanceId: '',
      contractVersion: INTEROP_CONTRACT_VERSION,
      configRevision: result.rejection.revision,
      accepted: false,
      rejection: result.rejection,
    });
  }

  const instance = new BpmnCanvasInstance(
    createInstanceId(),
    hostElement,
    paperElement,
    result.config,
    bridge,
  );

  const handle = new CanvasHandle(
    instance,
    {
      instanceId: instance.id,
      contractVersion: INTEROP_CONTRACT_VERSION,
      configRevision: result.config.revision,
      accepted: true,
      rejection: null,
    },
    () => { tracked.delete(hostElement); },
  );

  if (BUILD_MODE === 'Debug') tracked.set(hostElement, instance);

  try {
    if (document !== null) {
      const loaded = instance.loadDocument(document, null);
      await bridge.documentLoaded(loaded);
    }

    await bridge.initialized({ instanceId: instance.id, configRevision: result.config.revision });
  } catch (failure) {
    // No handle is returned for .NET to dispose, so nothing else will: the instance holds window listeners and a paper.
    try {
      handle.dispose();
    } catch {
      // The failure being reported is the one that matters.
    }

    throw failure;
  }

  return handle;
}

function createInstanceId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  return `bpmn-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

/**
 * Live canvases by host element, filled in Debug bundles only.
 *
 * Exists so tests can see a leaked instance and reach one by the id on its host element. .NET
 * never uses it: it holds a reference to its own canvas.
 */
const tracked = new Map<Element, BpmnCanvasInstance>();

/**
 * Debug-only hook that makes instance leaks observable from tests. Excluded from Release
 * bundles so it cannot become an accidental part of the public surface.
 */
export function __instanceCount(): number {
  return BUILD_MODE === 'Debug' ? tracked.size : -1;
}

/**
 * Debug-only hook that moves one element.
 *
 * Alignment guides respond to position changes rather than to a drag handler, so this drives the
 * same code path a pointer drag will and makes the guides verifiable in a browser before the
 * editing interactions exist. Debug-only for the same reason as the count above: moving elements
 * is not part of this milestone's public surface.
 */
export function __moveElement(instanceId: string, elementId: string, x: number, y: number): boolean {
  if (BUILD_MODE !== 'Debug') return false;

  for (const instance of tracked.values()) {
    if (instance.id === instanceId) return instance.moveElement(elementId, x, y);
  }

  return false;
}
