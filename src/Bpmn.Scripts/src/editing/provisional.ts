import type { dia } from '@joint/core';

/**
 * The flag marking the link a connector gesture draws while it is in flight.
 *
 * It has to exist because the provisional link is a real cell on the real graph for the duration of
 * the drag — that is how JointJS draws one, and there is no shadow graph to put it on. So every
 * part of the editor that reacts to cells has to be able to tell that this one is scaffolding:
 *
 * - selecting it (on its `link:pointerdown`) removes the source element's tools, which destroys the
 *   very handle the gesture is being driven from and kills the drag mid-flight;
 * - labelling it announces a connector that does not exist to a screen-reader user;
 * - reporting its removal as a document change would undo the user's real selection.
 *
 * Exported as one predicate rather than duplicated as a string literal, so a module that reacts to
 * cells cannot forget which attribute to check.
 */
export const PROVISIONAL_FLAG = 'bpmnProvisional';

/** Whether a cell is the scaffolding a connector gesture draws, rather than real notation. */
export function isProvisional(cell: dia.Cell | undefined): boolean {
  return cell?.get(PROVISIONAL_FLAG) === true;
}
