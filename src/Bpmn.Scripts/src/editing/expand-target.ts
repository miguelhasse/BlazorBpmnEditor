import type { dia } from '@joint/core';

import { isProvisional } from './provisional.js';

/**
 * What a double-click on the canvas means.
 *
 * Two gestures share the event — collapsing an expanded Sub-Process and editing a label — and they
 * are told apart here, once, so neither controller has to know the other is listening and the two
 * cannot drift into both claiming the same click.
 */

/** The `bpmn` metadata every cell carries, as much of it as this module reads. */
interface CellFacts {
  readonly isExpanded?: boolean | null;
}

/**
 * The Sub-Process a double-click is asking to collapse, or null when it is asking for something
 * else.
 *
 * Section 1.2.1 allows the gesture on "an expanded embedded Sub-Process's own empty body, border,
 * or header", and rules out double-clicks that originate "on a nested child, connector, label,
 * marker, resize/link tool, phase/milestone overlay, or active HTML editor". Each of those is
 * reachable for a different reason, which is why the test is in three parts:
 *
 * - **A nested child** has a view of its own and JointJS reports the deepest one, so the parent
 *   never hears about it. Asking "is *this* cell an expanded Sub-Process" is what stops a
 *   double-click on an inner Task collapsing the container around it.
 * - **A resize handle or a connect handle** is mounted in the paper's tools layer, outside the
 *   cell's own element, but carries the cell's `model-id` so `findView` resolves it to this very
 *   cell. `view.el.contains` is the only thing that tells them apart from the shape itself.
 * - **The markers, the type glyph and the label** are inside the cell's element, so they need the
 *   selector test. It is an allow-list rather than a deny-list deliberately: a decoration added to
 *   the markup later is excluded by default, instead of silently becoming a collapse target.
 *
 * Blazor's own overlays — the inline label editor, the context menu — are not in the paper's SVG
 * at all and fail the `contains` test without being named.
 */
export function collapseTargetOf(view: dia.CellView, event: dia.Event): string | null {
  if (isProvisional(view.model)) return null;
  if (!view.model.isElement()) return null;

  // Only a Sub-Process ever reports true, so this excludes every Task, Event, Gateway and Pool in
  // one clause as well as every collapsed Sub-Process.
  const facts = view.model.get('bpmn') as CellFacts | undefined;
  if (facts?.isExpanded !== true) return null;

  // `dia.Event` types its target as `any`; narrowing it here is what makes the rest of this
  // function type-safe rather than merely looking it.
  const target: unknown = event.target;
  if (!(target instanceof Element) || !view.el.contains(target)) return null;

  if (target !== view.el) {
    const owner = target.closest('[joint-selector]');
    const selector = owner === null ? null : owner.getAttribute('joint-selector');

    if (selector !== 'body' && selector !== 'innerBorder') return null;
  }

  return String(view.model.id);
}
