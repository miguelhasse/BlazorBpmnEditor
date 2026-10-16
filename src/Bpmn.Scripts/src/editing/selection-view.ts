import type { dia } from '@joint/core';
import { highlighters } from '@joint/core';

import { findCell, findView } from '../graph/cells.js';
import { outlineSelectorOf } from '../shapes/elements.js';
import { connectTool } from './connect-tool.js';
import type { LinkEditController } from './link-tools.js';
import { anchorsFor, type ResizeController } from './resize-tool.js';
import type { Selection } from './selection.js';

/**
 * Draws the selection.
 *
 * Built on JointJS's own `mask` highlighter rather than hand-drawn rectangles, because that
 * highlighter traces the rendered geometry — an Event comes out round, a Gateway comes out
 * diamond-shaped, a connector follows its route. The specification asks for exactly that, and
 * re-deriving each outline here would mean a second source of truth for every shape's silhouette.
 *
 * Which node it traces is the whole point. Pointed at the view's `root` it traced the group, and a
 * group's geometry is the box around everything in it — the label sitting outside an Event, the
 * markers along the bottom of a Task — so every selection came out as a rectangle substantially
 * larger than the shape. It is pointed at the node that draws the silhouette instead, which each
 * shape family names for itself.
 */
export class SelectionView {
  /** One highlighter id, so adding a second time replaces rather than stacks. */
  static readonly #Id = 'bpmn-selected';

  readonly #paper: dia.Paper;
  readonly #showConnectTool: boolean;
  readonly #resize: ResizeController | null;
  readonly #linkEdit: LinkEditController | null;
  #shown: readonly string[] = [];

  /**
   * @param paper The paper to draw on.
   * @param showConnectTool
   * Whether a selected element gets a connector-initiation handle. Off in read-only, where
   * selection is retained but every mutation surface is gone (specification section 7).
   * Deliberately required rather than defaulted: a default meant the flag could be — and was —
   * added without ever being passed, and the handle silently never appeared.
   */
  /**
   * @param paper The paper to draw on.
   * @param showConnectTool
   * Whether a selected element gets a connector-initiation handle and resize handles. Off in
   * read-only, where selection is retained but every mutation surface is gone (section 7).
   * Deliberately required rather than defaulted: a default meant the flag could be — and was —
   * added without ever being passed, and the handle silently never appeared.
   * @param resize
   * Builds the resize handles, when the host allows resizing at all.
   * @param linkEdit
   * Builds the connector tools — bends, arrowheads and remove — or null in read-only.
   */
  public constructor(
    paper: dia.Paper,
    showConnectTool: boolean,
    resize: ResizeController | null = null,
    linkEdit: LinkEditController | null = null,
  ) {
    this.#paper = paper;
    this.#showConnectTool = showConnectTool;
    this.#resize = resize;
    this.#linkEdit = linkEdit;
  }

  /** Re-draws for the current selection. Safe to call on every change. */
  public render(selection: Selection): void {
    this.clear();

    for (const id of selection.ids) {
      const cell = findCell(this.#paper.model, id);
      if (cell === undefined) continue;

      const view = findView(this.#paper, cell);
      if (view === undefined) continue;

      if (this.#showConnectTool && cell.isElement()) {
        this.#attachTools(view as dia.ElementView, cell);
      }

      // A selected connector gets its own set: bends, arrowheads and remove.
      if (this.#linkEdit !== null && cell.isLink()) {
        view.addTools(this.#linkEdit.toolsFor());
      }

      highlighters.mask.add(view, outlineSelectorOf(cell), SelectionView.#Id, {
        // Far enough out to read as a halo around the border rather than a repaint of it, and
        // close enough not to collide with the connect handle sitting 12px off the outline.
        padding: 4,
        attrs: {
          stroke: 'var(--bpmn-selection-color)',
          'stroke-width': 2,
          'stroke-linejoin': 'round',
          // An open path such as a connector would otherwise leave the contour open at both ends.
          'stroke-linecap': 'round',
          // The mask is an outline, not a fill: a filled highlight would hide the notation it is
          // drawing attention to.
          fill: 'none',
        },
      });
    }

    this.#shown = selection.ids;
  }

  public clear(): void {
    for (const id of this.#shown) {
      const cell = findCell(this.#paper.model, id);
      const view = cell === undefined ? undefined : findView(this.#paper, cell);
      if (view === undefined) continue;

      highlighters.mask.remove(view, SelectionView.#Id);
      view.removeTools();
    }

    this.#shown = [];
  }

  /**
   * Adds the connector-initiation handle to a selected element.
   *
   * `elementTools.Connect` is JointJS's own: it starts a link drag through the paper's
   * `defaultLink` and `validateConnection`, so the BPMN rules judge it without this knowing how a
   * drag works. Placed outside the shape's right edge, where it cannot be mistaken for notation.
   */
  /**
   * Adds the tools a selected element offers.
   *
   * One `addTools` call, because a second replaces the first: JointJS keeps a single tools view
   * per cell view, so connect and resize have to be assembled into the same one.
   */
  #attachTools(view: dia.ElementView, cell: dia.Cell): void {
    const bpmn = cell.get('bpmn') as { readonly elementType?: string } | undefined;
    const anchors = this.#resize === null ? [] : anchorsFor(bpmn?.elementType ?? '');

    if (bpmn?.elementType === 'group') {
      if (anchors.length > 0 && this.#resize !== null) {
        view.addTools(this.#resize.toolsFor(anchors));
      }

      return;
    }

    const tools = connectTool();

    if (anchors.length > 0 && this.#resize !== null) {
      tools.options.tools = [
        ...(tools.options.tools ?? []),
        ...(this.#resize.toolsFor(anchors).options.tools ?? []),
      ];
    }

    view.addTools(tools);
  }

  public destroy(): void {
    this.clear();
  }
}
