/**
 * The selection set.
 *
 * Pure, and view state rather than document state: what is selected is not part of the BPMN
 * document and never reaches the XML. It lives here because it is driven by the pointer, and is
 * reported to Blazor so the chrome can follow it.
 */

/** How a selection gesture combines with what was already selected. */
export type SelectionMode = 'replace' | 'toggle';

/** An immutable selection set. */
export class Selection {
  readonly #ids: ReadonlySet<string>;

  private constructor(ids: ReadonlySet<string>) {
    this.#ids = ids;
  }

  public static readonly empty = new Selection(new Set());

  public static of(ids: Iterable<string>): Selection {
    return new Selection(new Set(ids));
  }

  public get size(): number {
    return this.#ids.size;
  }

  public get isEmpty(): boolean {
    return this.#ids.size === 0;
  }

  public has(id: string): boolean {
    return this.#ids.has(id);
  }

  /** The selected ids, sorted, so a payload is stable regardless of click order. */
  public get ids(): readonly string[] {
    return this.#sorted ??= [...this.#ids].sort();
  }

  #sorted: readonly string[] | undefined;

  /**
   * Applies a gesture on one element.
   *
   * `replace` on something already selected keeps the whole selection rather than collapsing it to
   * that one element — otherwise pressing down on a member of a multi-selection to drag it would
   * throw the rest away before the drag started.
   */
  public apply(id: string, mode: SelectionMode): Selection {
    if (mode === 'toggle') {
      const next = new Set(this.#ids);
      if (!next.delete(id)) next.add(id);
      return new Selection(next);
    }

    return this.#ids.has(id) ? this : new Selection(new Set([id]));
  }

  public clear(): Selection {
    return this.isEmpty ? this : Selection.empty;
  }

  /** Drops ids that are no longer in the graph, after a delete or a reload. */
  public retaining(present: (id: string) => boolean): Selection {
    const kept = [...this.#ids].filter(present);
    return kept.length === this.#ids.size ? this : new Selection(new Set(kept));
  }

  public equals(other: Selection): boolean {
    if (other.#ids.size !== this.#ids.size) return false;
    for (const id of this.#ids) {
      if (!other.#ids.has(id)) return false;
    }

    return true;
  }
}
