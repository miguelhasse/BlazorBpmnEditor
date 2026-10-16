import type {
  CanvasInitializedPayload,
  ConfigAppliedPayload,
  ConfigRejection,
  ContextMenuRequestPayload,
  FileDroppedPayload,
  DocumentLoadedPayload,
  EditRejectedPayload,
  EditRequest,
  LabelEditRequestPayload,
  SelectionAnchorChangedPayload,
  SelectionChangedPayload,
  ShortcutInvokedPayload,
  ViewportChangedPayload,
} from './contracts.js';
import type { DotNetObjectRef } from './dotnet.js';

/** The most often the host hears of the viewport while a gesture is moving it. */
export const VIEWPORT_REPORT_INTERVAL_MS = 100;

/**
 * The single TypeScript -> .NET channel.
 *
 * Every method here maps 1:1 onto a `[JSInvokable]` method of the same name on
 * `BpmnCanvasEventBridge`; there is deliberately no generic `dispatch(name, payload)`
 * escape hatch, so both sides of the contract are enumerable and testable
 * (implementation plan §5).
 *
 * Lifecycle events are sent immediately and awaited. Continuous events (viewport,
 * and later selection/hover) go through a microtask-coalescing queue that keeps only
 * the most recent payload per event name, so a drag or a wheel gesture can never turn
 * into per-pixel interop chatter.
 */
export class BpmnEventBridge {
  readonly #target: DotNetObjectRef;
  #coalesced = new Map<string, unknown>();
  #flushScheduled = false;
  #disposed = false;
  #viewportPending: ViewportChangedPayload | null = null;
  #viewportTimer: ReturnType<typeof setTimeout> | undefined;

  public constructor(target: DotNetObjectRef) {
    this.#target = target;
  }

  // --- lifecycle (immediate) ------------------------------------------------

  public initialized(payload: CanvasInitializedPayload): Promise<void> {
    return this.#send('Initialized', payload);
  }

  public configApplied(payload: ConfigAppliedPayload): Promise<void> {
    return this.#send('ConfigApplied', payload);
  }

  public configRejected(payload: ConfigRejection): Promise<void> {
    return this.#send('ConfigRejected', payload);
  }

  public documentLoaded(payload: DocumentLoadedPayload): Promise<void> {
    return this.#send('DocumentLoaded', payload);
  }

  /**
   * A bound key fired a command the canvas does not handle itself.
   *
   * Immediate rather than coalesced: it is a discrete user action, and coalescing would drop the
   * second of two quick presses.
   */
  public shortcutInvoked(payload: ShortcutInvokedPayload): Promise<void> {
    return this.#send('ShortcutInvoked', payload);
  }

  /**
   * The canvas is asking the host to apply an edit.
   *
   * Immediate and awaited: the host owns the document, and a coalesced edit would be an edit
   * silently dropped.
   */
  public editRequested(payload: EditRequest): Promise<void> {
    return this.#send('EditRequested', payload);
  }

  /** A gesture was refused before it was reported, and the canvas has already rolled back. */
  public editRejected(payload: EditRejectedPayload): Promise<void> {
    return this.#send('EditRejected', payload);
  }

  /**
   * The native context menu was suppressed and the Blazor one should open.
   *
   * Immediate and awaited. Coalescing it would be worse than dropping an event: the browser menu
   * is already prevented by the time this is sent, so a lost request leaves the user with a
   * right-click that did nothing at all.
   */
  public contextMenuRequested(payload: ContextMenuRequestPayload): Promise<void> {
    return this.#send('ContextMenuRequested', payload);
  }

  /**
   * A BPMN file was dropped on the canvas.
   *
   * Reading it is the canvas's part; deciding what to do about it is not. Discrete, and never
   * coalesced — a dropped file is a deliberate act and losing one is not recoverable by repeating
   * the gesture, because the user has already let go of it.
   */
  public fileDropped(payload: FileDroppedPayload): Promise<void> {
    return this.#send('FileDropped', payload);
  }

  /** A label should be edited in place (section 4.5). Discrete, so immediate. */
  public labelEditRequested(payload: LabelEditRequestPayload): Promise<void> {
    return this.#send('LabelEditRequested', payload);
  }

  // --- continuous (coalesced) -----------------------------------------------

  /**
   * Throttled: the latest viewport is delivered at most once every {@link VIEWPORT_REPORT_INTERVAL_MS}, and always after the last change.
   *
   * A wheel or pinch gesture changes the viewport every frame, and each delivery costs an interop call and a Blazor render. The host
   * wants to know where the gesture ended and roughly what it passed through, not sixty states a second, so a trailing timer replaces
   * the microtask that used to send one call per event.
   */
  public viewportChanged(payload: ViewportChangedPayload): void {
    if (this.#disposed) return;

    this.#viewportPending = payload;
    this.#viewportTimer ??= setTimeout(this.#flushViewport, VIEWPORT_REPORT_INTERVAL_MS);
  }

  /**
   * Coalesced: a rubber-band or a rapid series of clicks should deliver where the selection
   * ended up, not every state it passed through.
   */
  public selectionChanged(payload: SelectionChangedPayload): void {
    this.#coalesce('SelectionChanged', payload);
  }

  /**
   * Coalesced: the selected element moved on screen, or the view under it did. Kept apart from
   * `selectionChanged` so a host listening for selection changes is not told about one that did
   * not happen.
   */
  public selectionAnchorChanged(payload: SelectionAnchorChangedPayload): void {
    this.#coalesce('SelectionAnchorChanged', payload);
  }

  /**
   * Stops further delivery. Called from `dispose` so an in-flight gesture cannot
   * invoke into a `DotNetObjectReference` the canvas has already released.
   */
  public dispose(): void {
    // The last viewport is delivered first, so a rebuild that restores it does not land on the one before the final gesture.
    this.#flushViewport();
    this.#disposed = true;
    this.#coalesced.clear();
  }

  readonly #flushViewport = (): void => {
    if (this.#viewportTimer !== undefined) clearTimeout(this.#viewportTimer);
    this.#viewportTimer = undefined;

    const payload = this.#viewportPending;
    this.#viewportPending = null;
    if (payload === null || this.#disposed) return;

    // Best effort: the host may already be tearing the component down.
    this.#send('ViewportChanged', payload).catch(() => undefined);
  };

  async #send(method: string, payload: unknown): Promise<void> {
    if (this.#disposed) return;
    await this.#target.invokeMethodAsync(method, payload);
  }

  #coalesce(method: string, payload: unknown): void {
    if (this.#disposed) return;
    this.#coalesced.set(method, payload);
    if (this.#flushScheduled) return;

    this.#flushScheduled = true;
    queueMicrotask(() => {
      this.#flushScheduled = false;
      // Iterated in place: a send is awaited by nobody here, so nothing can add to the map while it runs.
      const pending = this.#coalesced;
      this.#coalesced = new Map();
      for (const [method_, payload_] of pending) {
        void this.#send(method_, payload_);
      }
    });
  }
}
