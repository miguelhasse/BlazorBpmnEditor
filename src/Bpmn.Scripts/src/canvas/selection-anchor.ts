import type { AnchorContract } from '../interop/contracts.js';

/** What the reporter needs from the canvas it serves. */
export interface SelectionAnchorHost {
  /** The current selection. */
  readonly selection: () => readonly string[];

  /** Where an element is now, or null when it is not drawn. */
  readonly anchorOf: (elementId: string) => AnchorContract | null;

  /** Sends where the selected element now is, or null to hide what sits beside it. */
  readonly report: (elementId: string, anchor: AnchorContract | null) => void;
}

/**
 * Keeps the anchor of the one selected element current while its geometry changes.
 *
 * The anchor rides on the selection report, and the selection only changes when *which* elements
 * are selected does — so a selected element that was dragged, resized, redrawn or scrolled under a
 * pan kept the anchor it had when it was clicked, and the quick-action strip stayed where the
 * element used to be.
 *
 * Re-sending it on every frame of a gesture would make a round trip per pointer move on a Blazor
 * Server circuit for a strip that is in the way while the gesture lasts anyway. So a gesture hides
 * the strip (one report with no anchor) and the anchor is sent again once the pointer is released
 * and the geometry has been still for a moment. The pause is what ends a wheel zoom, which has no
 * release to wait for.
 */
export class SelectionAnchorReporter {
  readonly #target: EventTarget;
  readonly #window: Window;
  readonly #host: SelectionAnchorHost;
  readonly #delayMilliseconds: number;

  /** The anchor last sent, so an unchanged one is not sent again. Null when hidden or unknown. */
  #last: AnchorContract | null = null;
  #pressed = false;
  #timer: ReturnType<typeof setTimeout> | null = null;

  /**
   * @param target Where a gesture starts: the paper's element.
   * @param host The canvas whose selection is reported.
   * @param delayMilliseconds How long the geometry must be still before the anchor is sent again.
   * @param window_ Where a gesture ends; a release can land outside the paper.
   */
  public constructor(
    target: EventTarget,
    host: SelectionAnchorHost,
    delayMilliseconds = 150,
    window_: Window = window,
  ) {
    this.#target = target;
    this.#host = host;
    this.#delayMilliseconds = delayMilliseconds;
    this.#window = window_;
  }

  public attach(): this {
    this.#target.addEventListener('pointerdown', this.#handleDown, true);
    this.#window.addEventListener('pointerup', this.#handleUp, true);
    this.#window.addEventListener('pointercancel', this.#handleUp, true);
    return this;
  }

  /** Records an ordinary selection report, which carries its own fresh anchor. */
  public selectionReported(anchor: AnchorContract | null): void {
    this.#cancel();
    this.#last = anchor;
  }

  /** The selected element's geometry, or the view onto it, is changing: hide the strip. */
  public moved(): void {
    const single = this.#single();
    if (single !== null && this.#last !== null) {
      this.#last = null;
      this.#host.report(single, null);
    }

    this.scheduleShow();
  }

  /** Sends the anchor again once the pointer is up and nothing has moved for a moment. */
  public scheduleShow(): void {
    this.#cancel();
    this.#timer = setTimeout(() => {
      this.#timer = null;
      this.#show();
    }, this.#delayMilliseconds);
  }

  public destroy(): void {
    this.#cancel();
    this.#target.removeEventListener('pointerdown', this.#handleDown, true);
    this.#window.removeEventListener('pointerup', this.#handleUp, true);
    this.#window.removeEventListener('pointercancel', this.#handleUp, true);
  }

  #show(): void {
    // The release reschedules, so a gesture still under way is simply waited out.
    if (this.#pressed) return;

    const single = this.#single();
    if (single === null) return;

    const anchor = this.#host.anchorOf(single);
    if (anchor === null || sameAnchor(anchor, this.#last)) return;

    this.#last = anchor;
    this.#host.report(single, anchor);
  }

  #single(): string | null {
    const selection = this.#host.selection();
    return selection.length === 1 ? selection[0] ?? null : null;
  }

  #cancel(): void {
    if (this.#timer === null) return;
    clearTimeout(this.#timer);
    this.#timer = null;
  }

  readonly #handleDown = (): void => {
    this.#pressed = true;
  };

  readonly #handleUp = (): void => {
    if (!this.#pressed) return;
    this.#pressed = false;
    this.scheduleShow();
  };
}

function sameAnchor(a: AnchorContract, b: AnchorContract | null): boolean {
  if (b === null) return false;
  return a.client.x === b.client.x
    && a.client.y === b.client.y
    && a.client.width === b.client.width
    && a.client.height === b.client.height;
}
