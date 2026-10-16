/**
 * Pointer-gesture geometry, kept pure so the multi-touch arithmetic is testable.
 *
 * Touch reaches the canvas as pointer events, not as a separate touch API: one active pointer is
 * a pan, two are a pinch. Keeping the maths here rather than inside the event handlers is what
 * makes "two fingers apart by 1.5x zooms by 1.5x about their midpoint" something a test can state.
 */

/** One active pointer, in element-relative coordinates. */
export interface PointerSample {
  readonly id: number;
  readonly x: number;
  readonly y: number;
}

/** The distance and midpoint of a two-pointer gesture. */
export interface PinchSnapshot {
  readonly distance: number;
  readonly centerX: number;
  readonly centerY: number;
}

/** What an updated set of pointers means. */
export type GestureIntent =
  | { readonly kind: 'none' }
  | { readonly kind: 'pan'; readonly dx: number; readonly dy: number }
  | {
    readonly kind: 'pinch';
    readonly scale: number;
    readonly centerX: number;
    readonly centerY: number;
    readonly dx: number;
    readonly dy: number;
  };

/** Distance and midpoint between two pointers. */
export function pinchSnapshot(first: PointerSample, second: PointerSample): PinchSnapshot {
  return {
    distance: Math.hypot(second.x - first.x, second.y - first.y),
    centerX: (first.x + second.x) / 2,
    centerY: (first.y + second.y) / 2,
  };
}

/**
 * Tracks active pointers and turns each update into a pan or a pinch.
 *
 * A pinch reports both a scale and a centre translation, because two fingers usually move as they
 * spread; handling only the scale makes the gesture feel like it is fighting the user.
 */
export class GestureTracker {
  readonly #pointers = new Map<number, PointerSample>();
  #pinch: PinchSnapshot | null = null;

  public get count(): number {
    return this.#pointers.size;
  }

  public get isPinching(): boolean {
    return this.#pointers.size >= 2;
  }

  public start(sample: PointerSample): void {
    this.#pointers.set(sample.id, sample);
    this.#pinch = this.#currentPinch();
  }

  public end(id: number): void {
    this.#pointers.delete(id);

    // Re-baselined rather than cleared: lifting one of three fingers leaves a valid pinch, and
    // the remaining pair's distance is the new reference rather than a jump from the old one.
    this.#pinch = this.#currentPinch();
  }

  public clear(): void {
    this.#pointers.clear();
    this.#pinch = null;
  }

  /** Updates one pointer and reports what the change means. */
  public move(sample: PointerSample): GestureIntent {
    const previous = this.#pointers.get(sample.id);
    if (previous === undefined) return { kind: 'none' };

    this.#pointers.set(sample.id, sample);

    if (this.#pointers.size === 1) {
      return { kind: 'pan', dx: sample.x - previous.x, dy: sample.y - previous.y };
    }

    const next = this.#currentPinch();
    const baseline = this.#pinch;
    this.#pinch = next;

    if (next === null || baseline === null || baseline.distance === 0) return { kind: 'none' };

    return {
      kind: 'pinch',
      scale: next.distance / baseline.distance,
      centerX: next.centerX,
      centerY: next.centerY,
      dx: next.centerX - baseline.centerX,
      dy: next.centerY - baseline.centerY,
    };
  }

  /** The first two pointers, in insertion order, so a third finger does not change the reference pair. */
  #currentPinch(): PinchSnapshot | null {
    if (this.#pointers.size < 2) return null;

    const [first, second] = [...this.#pointers.values()];
    return first === undefined || second === undefined ? null : pinchSnapshot(first, second);
  }
}
