/**
 * The picture that follows the pointer out of the stencil.
 *
 * Left alone, the browser drags a snapshot of whatever carries the `draggable` attribute — here the
 * whole tool row, so a label, its padding and its background travelled across the canvas with the
 * shape. What the user is placing is the element, so that is what the preview shows.
 *
 * A delegated listener rather than a handler on each tool, and here rather than in the stencil
 * component, for one reason each. `setDragImage` lives on the live `DataTransfer`, and Blazor's
 * `DragEventArgs` is a serialized copy with no such object — so it has to be JavaScript. And the
 * stencil holds no module reference on purpose: nothing but the canvas talks to JavaScript, which
 * is what keeps one component responsible for this module's lifetime.
 *
 * The icon is already in the document and already rendered, so it is handed over as it stands. An
 * off-screen clone would need its own copy of the stylesheet to look like anything — the glyphs are
 * `currentcolor` against theme tokens declared further up the tree.
 */
export class StencilDragImage {
  readonly #root: Element;
  readonly #abort = new AbortController();

  /**
   * @param root The element to listen on; the whole editor, since the stencil is not in the canvas.
   */
  public constructor(root: Element) {
    this.#root = root;
    this.#root.addEventListener('dragstart', this.#handleDragStart, { signal: this.#abort.signal });
  }

  public destroy(): void {
    this.#abort.abort();
  }

  /**
   * Replaces the drag image with the tool's own icon.
   *
   * Everything here is guarded, and not out of politeness: a throw inside `dragstart` cancels the
   * drag, and a stencil whose tools cannot be dragged is a worse outcome than a preview that looks
   * wrong.
   */
  readonly #handleDragStart = (event: Event): void => {
    try {
      const drag = event as DragEvent;
      const target = drag.target;
      if (!(target instanceof Element)) return;

      const tool = target.closest('.bpmn-stencil__tool');
      const icon = tool?.querySelector('.bpmn-stencil__icon');
      if (icon === null || icon === undefined) return;

      const box = icon.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) return;

      // Centred on the pointer: the drop point is the element's centre, so the preview should be
      // the thing being aimed rather than something hanging off the corner of it.
      drag.dataTransfer?.setDragImage(icon, box.width / 2, box.height / 2);
    } catch {
      // The browser keeps its default image, which is the behaviour this replaces.
    }
  };
}
