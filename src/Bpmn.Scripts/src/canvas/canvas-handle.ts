import type {
  AnchorContract,
  CanvasInitResult,
  ContextMenuSource,
  DocumentLoadedPayload,
  DropTargetContract,
  ExecutionStateContract,
  ViewportCommand,
} from '../interop/contracts.js';
import type { BpmnDocumentContract } from '../model/contracts.js';
import type { BpmnCanvasInstance } from './bpmn-canvas-instance.js';

/**
 * What .NET holds for one canvas: an `IJSObjectReference` to this object.
 *
 * Every method here is called by name from `BpmnCanvas`, so the names and their arguments are the
 * interop contract. It is deliberately narrower than `BpmnCanvasInstance` — the instance has
 * controllers and internals that are not part of the contract — and it owns the "no canvas"
 * answers: a call that arrives after disposal, or on a canvas whose configuration was rejected,
 * is a race rather than a programming error, so it returns the neutral result instead of throwing.
 */
export class CanvasHandle {
  #instance: BpmnCanvasInstance | null;

  readonly #initResult: CanvasInitResult;

  readonly #onDisposed: () => void;

  /**
   * @param instance The canvas, or null when the configuration was rejected and nothing was built.
   * @param initResult What `initialize` reports back to .NET.
   * @param onDisposed Called once when the canvas is torn down.
   */
  public constructor(
    instance: BpmnCanvasInstance | null,
    initResult: CanvasInitResult,
    onDisposed: () => void = () => undefined,
  ) {
    this.#instance = instance;
    this.#initResult = initResult;
    this.#onDisposed = onDisposed;
  }

  public getInitResult(): CanvasInitResult {
    return this.#initResult;
  }

  public loadDocument(document: BpmnDocumentContract, diagramId: string | null): DocumentLoadedPayload | null {
    return this.#instance?.loadDocument(document, diagramId) ?? null;
  }

  public deleteElements(elementIds: readonly string[]): boolean {
    return this.#instance?.deleteElements(elementIds) ?? false;
  }

  public setActiveDiagram(diagramId: string): DocumentLoadedPayload | null {
    return this.#instance?.setActiveDiagram(diagramId) ?? null;
  }

  /** Resolves a stencil drop into paper geometry and an owning container. */
  public resolveDrop(clientX: number, clientY: number, elementType: string): DropTargetContract | null {
    return this.#instance?.resolveDrop(clientX, clientY, elementType) ?? null;
  }

  /** Where a keyboard-armed stencil tool should be created. */
  public resolveCreationPoint(elementType: string): DropTargetContract | null {
    return this.#instance?.resolveCreationPoint(elementType) ?? null;
  }

  /** Where an element appended to another one should be created. */
  public resolveAppendPoint(sourceId: string, elementType: string): DropTargetContract | null {
    return this.#instance?.resolveAppendPoint(sourceId, elementType) ?? null;
  }

  /**
   * Draws an engine's view of a running instance over the diagram. Available in read-only, and not
   * a mutation: an execution state changes nothing about the document.
   */
  public setExecutionState(state: ExecutionStateContract): void {
    this.#instance?.setExecutionState(state);
  }

  /** Removes every execution overlay. */
  public clearExecutionState(): void {
    this.#instance?.clearExecutionState();
  }

  /** Replaces the selection. */
  public setSelection(elementIds: readonly string[]): void {
    this.#instance?.setSelection(elementIds);
  }

  /** Opens the context menu for a target the host resolved itself (the keyboard path). */
  public requestContextMenu(elementId: string | null, source: ContextMenuSource): void {
    this.#instance?.requestContextMenu(elementId, source);
  }

  /** Opens the inline label editor for one element. False when it has no label to edit. */
  public editLabel(elementId: string): boolean {
    return this.#instance?.editLabel(elementId) ?? false;
  }

  /** Where an element is, in paper and client space, for chrome that has to sit beside it. */
  public anchorOf(elementId: string): AnchorContract | null {
    return this.#instance?.anchorOf(elementId) ?? null;
  }

  public setViewport(command: ViewportCommand): void {
    this.#instance?.setViewport(command);
  }

  /** Safe to call twice: circuit teardown and an explicit disposal can both reach it. */
  public dispose(): void {
    const instance = this.#instance;
    if (instance === null) return;

    // Released before disposal so a dispose that throws still leaves the handle inert.
    this.#instance = null;
    instance.dispose();
    this.#onDisposed();
  }
}
