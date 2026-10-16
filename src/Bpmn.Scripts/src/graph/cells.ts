import type { dia } from '@joint/core';

/**
 * Lookups that admit failure.
 *
 * `dia.Graph.getCell` and `dia.Paper.findViewByModel` are both typed as always succeeding, and
 * neither does: an id that is not in the graph returns nothing, and a cell the asynchronous paper
 * has not drawn yet has no view. Widening the result in one place is better than an assertion at
 * every call site, and better than writing code that pretends the failure cannot happen.
 */

/**
 * The cell with this id, or `undefined`.
 *
 * The widening happens in the declared return type rather than through an assertion, so nothing
 * here claims to know better than the library — it just admits the case the signature omits.
 */
export function findCell(graph: dia.Graph, id: string): dia.Cell | undefined {
  return graph.getCell(id);
}

/** The element with this id, or `undefined` if it is missing or is a link. */
export function findElement(graph: dia.Graph, id: string): dia.Element | undefined {
  const cell = findCell(graph, id);
  return cell?.isElement() === true ? cell : undefined;
}

/** The rendered view for a cell, or `undefined` if it has not been drawn. */
export function findView(paper: dia.Paper, cell: dia.Cell): dia.CellView | undefined {
  return paper.findViewByModel(cell);
}

/** A cell's position as a plain object, since `position()` returns a class instance. */
export function positionOf(element: dia.Element): { x: number; y: number } {
  const { x, y } = element.position();
  return { x, y };
}
