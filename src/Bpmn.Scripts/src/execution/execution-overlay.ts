import type { dia } from '@joint/core';
import { highlighters } from '@joint/core';

import { findCell, findView } from '../graph/cells.js';
import type { CanvasStringsContract, ElementExecutionContract, ExecutionStateContract } from '../interop/contracts.js';
import { outlineSelectorOf } from '../shapes/elements.js';
import { buildBadges, type DrawnStatus } from './badges.js';

/**
 * Draws an engine's view of a running instance over the diagram (implementation plan M6.5).
 *
 * Nothing here touches the model. An execution state is an observation about an engine, not a fact
 * about the document, so it is drawn on top and thrown away — it never enters history, never makes
 * the editor dirty, and never reaches the XML. That is also why the whole thing works in read-only:
 * watching a process run is the case that mode exists for.
 *
 * The overlay is re-applied after every render rather than being assumed to survive one, which is
 * what lets it outlive a switch to another plane and back. Elements not on the active plane simply
 * are not found; when that plane is shown again they are, and the same resident state draws itself
 * over them.
 */
/**
 * The two calls this module makes on `highlighters.addClass`, named locally.
 *
 * The library's own `add` is inherited as `static add<T>(this: { new(): T }, ...)`, and
 * `addClass`'s constructor takes an options object whose `className` is optional — which under
 * `exactOptionalPropertyTypes` makes the two `HighlighterView` instantiations mutually
 * unassignable and the call uncompilable. The behaviour is correct; only the signature is not, so
 * the whole disagreement is confined to this one declaration rather than spread over a cast at
 * every call site (implementation plan risk R2).
 */
interface ClassHighlighter {
  add(cellView: dia.CellView, selector: string, id: string, options: { className: string }): void;
  removeAll(paper: dia.Paper, id: string): void;
}

const classHighlighter = highlighters.addClass as unknown as ClassHighlighter;

export class ExecutionOverlay {
  /** One highlighter id per concern, so re-applying replaces rather than stacks. */
  static readonly #StatusId = 'bpmn-exec-status';
  static readonly #FlowId = 'bpmn-exec-flow';

  readonly #paper: dia.Paper;
  readonly #strings: CanvasStringsContract;

  #state: ExecutionStateContract | null = null;

  /** The badge groups this overlay put into cell views, so it can take them out again. */
  #badges: SVGGElement[] = [];

  /**
   * @param paper The paper to draw on.
   */
  public constructor(paper: dia.Paper, strings: CanvasStringsContract) {
    this.#paper = paper;
    this.#strings = strings;
  }

  /** The state currently drawn, or null when none is. */
  public get state(): ExecutionStateContract | null {
    return this.#state;
  }

  /** Replaces the drawn state with a whole new one. */
  public set(state: ExecutionStateContract): void {
    this.#state = state;
    this.draw();
  }

  /** Removes every overlay and forgets the state. */
  public clear(): void {
    this.#state = null;
    this.#erase();
  }

  /** Draws the resident state again, after something replaced the cell views. */
  public draw(): void {
    this.#erase();

    const state = this.#state;
    if (state === null) return;

    /*
     * The paper is asynchronous, so a document rendered a moment ago may have no views yet and
     * every lookup below would quietly find nothing. Flushing first is what `reroute.ts` does for
     * the same reason, and an overlay that silently drew nothing would be indistinguishable from
     * an engine that reported nothing.
     */
    this.#paper.updateViews();

    for (const element of state.elements) this.#drawElement(element);
    for (const flow of state.flows) {
      if (flow.traversed) this.#drawFlow(flow.flowId);
    }
  }

  /** Drops the overlay. */
  public destroy(): void {
    this.clear();
  }

  #drawElement(state: ElementExecutionContract): void {
    const cell = findCell(this.#paper.model, state.elementId);
    if (cell === undefined) return;

    const view = findView(this.#paper, cell);
    if (view === undefined) return;

    if (state.status !== 'None') {
      classHighlighter.add(view, outlineSelectorOf(cell), ExecutionOverlay.#StatusId, {
        className: statusClassOf(state.status),
      });
    }

    const badges = cell.isElement() ? buildBadges(state, cell.size().width, this.#strings) : null;
    if (badges !== null) {
      view.el.append(badges);
      this.#badges.push(badges);
    }
  }

  #drawFlow(flowId: string): void {
    const cell = findCell(this.#paper.model, flowId);
    if (cell === undefined) return;

    const view = findView(this.#paper, cell);
    if (view === undefined) return;

    classHighlighter.add(view, outlineSelectorOf(cell), ExecutionOverlay.#FlowId, {
      className: 'bpmn-exec-flow--traversed',
    });
  }

  #erase(): void {
    classHighlighter.removeAll(this.#paper, ExecutionOverlay.#StatusId);
    classHighlighter.removeAll(this.#paper, ExecutionOverlay.#FlowId);

    for (const badge of this.#badges) badge.remove();
    this.#badges = [];
  }
}

/**
 * The class a status draws with.
 *
 * Exhaustive on purpose: a status added to the contract without a class here is a compile error
 * rather than an element that silently draws like every other one.
 */
export function statusClassOf(status: DrawnStatus): string {
  switch (status) {
    case 'Active':
      return 'bpmn-exec--active';
    case 'Completed':
      return 'bpmn-exec--completed';
    case 'Failed':
      return 'bpmn-exec--failed';
    case 'Canceled':
      return 'bpmn-exec--canceled';
    case 'Waiting':
      return 'bpmn-exec--waiting';
  }
}
