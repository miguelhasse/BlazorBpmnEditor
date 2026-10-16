import type { ActivityStatus, CanvasStringsContract, ElementExecutionContract, IncidentContract } from '../interop/contracts.js';
import { canvasText } from '../a11y/canvas-text.js';

/**
 * What an element carrying execution state has drawn on it.
 *
 * Kept as a pure builder taking a width and returning a detached group: it makes the markup
 * testable without a paper, and it keeps the overlay controller about *when* an element is
 * decorated rather than what the decoration looks like.
 *
 * Two visible badges, each drawn only when there is something to say. A token counter appears when
 * at least one token sits on the element — a badge reading "0" and no badge at all mean different
 * things, and only the second is true of an element nothing has reached. An incident indicator
 * appears when the engine has raised anything against it.
 *
 * And always a `<title>`, which is the part that is not optional. The status itself is drawn as a
 * coloured, dashed outline, and colour and line style are both invisible to a reader using a
 * screen reader. The title is how that reader is told the same thing.
 */

const SVG_NS = 'http://www.w3.org/2000/svg';

const BADGE_RADIUS = 9;

/** Nudged outside the top-right corner, where BPMN itself draws nothing. */
const CORNER_OFFSET = 4;

/** The gap between two adjacent badges. */
const BADGE_GAP = 3;

function element<K extends keyof SVGElementTagNameMap>(
  name: K,
  attributes: Readonly<Record<string, string | number>>,
): SVGElementTagNameMap[K] {
  const node = document.createElementNS(SVG_NS, name);
  for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, String(value));
  return node;
}

/**
 * A digit centred on a point.
 *
 * `y` is a parameter because the two badges do not share a centre. A disc's is its own centre; a
 * triangle's is a third of the way up from its base, and a digit placed at zero sits on the
 * sloping edge with its lower half outside the shape.
 */
function counter(x: number, y: number, text: string): SVGTextElement {
  const node = element('text', {
    'class': 'bpmn-exec-badge__count',
    x,
    y,
    'text-anchor': 'middle',
    'dy': '0.36em',
  });
  node.textContent = text;
  return node;
}

/** "3 incidents", and the first message when there is one to quote. */
export function incidentSummary(incidents: readonly IncidentContract[], strings: CanvasStringsContract): string {
  const count = incidents.length;
  const summary = canvasText(strings, count === 1 ? 'Incident.One' : 'Incident.Many', count);
  const first = incidents[0];

  return first === undefined ? summary : canvasText(strings, 'IncidentMessage', summary, first.message);
}

/**
 * The sentence a screen reader is given for an element carrying execution state.
 *
 * Status first, because it is the thing the outline is saying in colour and the reader cannot see
 * it. Counts after, in the order they are drawn, so what is heard matches what is there.
 */
export function executionSummary(state: ElementExecutionContract, strings: CanvasStringsContract): string {
  const parts: string[] = [];

  if (state.status !== 'None') parts.push(canvasText(strings, `Status.${state.status}`));
  if (state.tokenCount > 0) parts.push(canvasText(strings, state.tokenCount === 1 ? 'Token.One' : 'Token.Many', state.tokenCount));
  if (state.incidents.length > 0) parts.push(incidentSummary(state.incidents, strings));

  return parts.join(canvasText(strings, 'ListSeparator'));
}

/** Whether this element has anything at all to draw. */
export function hasOverlay(state: ElementExecutionContract): boolean {
  return state.status !== 'None' || state.tokenCount > 0 || state.incidents.length > 0;
}

/** The status word, narrowed to the ones that draw something. */
export type DrawnStatus = Exclude<ActivityStatus, 'None'>;

/**
 * Builds the overlay group for one element, in the element's own coordinates.
 *
 * @param state What the engine reported.
 * @param width The element's width, which the top-right corner is measured from.
 * @returns A detached `<g>`, or null when the engine reported nothing about this element.
 */
export function buildBadges(state: ElementExecutionContract, width: number, strings: CanvasStringsContract): SVGGElement | null {
  if (!hasOverlay(state)) return null;

  const group = element('g', { 'class': 'bpmn-exec-badges', 'pointer-events': 'none' });

  const title = document.createElementNS(SVG_NS, 'title');
  title.textContent = executionSummary(state, strings);
  group.append(title);

  let x = width + CORNER_OFFSET - BADGE_RADIUS;

  if (state.tokenCount > 0) {
    const tokens = element('g', { class: 'bpmn-exec-badge bpmn-exec-badge--token' });
    tokens.append(element('circle', { class: 'bpmn-exec-badge__disc', cx: x, cy: 0, r: BADGE_RADIUS }));
    tokens.append(counter(x, 0, String(state.tokenCount)));
    group.append(tokens);

    // The next badge clears this one rather than overlapping it.
    x += BADGE_RADIUS * 2 + BADGE_GAP;
  }

  if (state.incidents.length > 0) {
    const incidents = element('g', { class: 'bpmn-exec-badge bpmn-exec-badge--incident' });

    /*
     * A triangle, not a second disc. The two badges are adjacent and frequently both present, so
     * the shape is what tells them apart when the palette cannot — in high contrast, in a
     * greyscale print, or for a reader who does not distinguish the two hues.
     */
    const apex = -BADGE_RADIUS;
    const base = BADGE_RADIUS * 0.8;
    incidents.append(
      element('path', {
        'class': 'bpmn-exec-badge__sign',
        'd': `M ${String(x)} ${String(apex)} L ${String(x + BADGE_RADIUS)} ${String(base)} `
          + `L ${String(x - BADGE_RADIUS)} ${String(base)} Z`,
      }),
    );

    // The centroid, which is where the triangle is widest enough to hold a digit.
    incidents.append(counter(x, (apex + base + base) / 3, String(state.incidents.length)));
    group.append(incidents);
  }

  return group;
}
