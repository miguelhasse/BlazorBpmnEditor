import type { dia } from '@joint/core';

/**
 * Whether a cell is a Lane.
 *
 * A Lane fills its Pool and is sized only by the splitters between Lanes, so several places need
 * to treat it differently from other shapes — it is never dragged on its own and has no resize
 * handles of its own.
 */
export function isLane(cell: dia.Cell): boolean {
  return (cell.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType === 'lane';
}
