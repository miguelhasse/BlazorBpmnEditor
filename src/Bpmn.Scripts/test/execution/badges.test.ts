// @vitest-environment jsdom
//
// The badge builder makes real SVG nodes, which is the point: the markup is what the browser and
// a screen reader are handed, so asserting against a DOM is asserting against the thing itself
// rather than a description of it.

import { describe, expect, it } from 'vitest';

import {
  buildBadges,
  executionSummary,
  hasOverlay,
  incidentSummary,
} from '../../src/execution/badges.js';
import { statusClassOf } from '../../src/execution/execution-overlay.js';
import type { ActivityStatus, ElementExecutionContract, IncidentContract } from '../../src/interop/contracts.js';
import { canvasStrings } from '../fixtures/canvas-strings.js';

/**
 * What an element carrying execution state gets drawn on it.
 *
 * The overlay's geometry needs a paper and is covered end to end; this is the part that does not,
 * and it is the part with the judgement calls in it — when a badge exists at all, and what a
 * reader who cannot see any of it is told.
 */
function state(overrides: Partial<ElementExecutionContract> = {}): ElementExecutionContract {
  return { elementId: 'Task_1', status: 'None', tokenCount: 0, incidents: [], ...overrides };
}

function incident(message: string): IncidentContract {
  return { id: `incident-${message}`, message, severity: 'Error' };
}

describe('hasOverlay', () => {
  it('draws nothing for an element the engine said nothing about', () => {
    expect(hasOverlay(state())).toBe(false);
    expect(buildBadges(state(), 100, canvasStrings)).toBeNull();
  });

  it.each<ActivityStatus>(['Active', 'Completed', 'Failed', 'Canceled', 'Waiting'])(
    'draws for a %s element even with no tokens and no incidents',
    (status) => {
      expect(hasOverlay(state({ status }))).toBe(true);
    },
  );
});

describe('buildBadges', () => {
  it('omits the token counter at zero', () => {
    /*
     * The distinction the whole badge turns on: "0" and no badge say different things, and an
     * element nothing has reached is described by the second one. A counter that always rendered
     * would put a nought on every completed element in the diagram.
     */
    const group = buildBadges(state({ status: 'Completed' }), 100, canvasStrings);

    expect(group).not.toBeNull();
    expect(group?.querySelector('.bpmn-exec-badge--token')).toBeNull();
  });

  it('draws the token counter with the count', () => {
    const group = buildBadges(state({ status: 'Active', tokenCount: 3 }), 100, canvasStrings);

    expect(group?.querySelector('.bpmn-exec-badge--token .bpmn-exec-badge__count')?.textContent).toBe('3');
  });

  it('draws incidents as a triangle rather than a second disc', () => {
    // Shape, not hue: the two badges sit side by side, and the palette is the signal that is lost
    // first — in high contrast, in greyscale, and for a reader who does not separate the colours.
    const group = buildBadges(state({ status: 'Failed', tokenCount: 1, incidents: [incident('Boom')] }), 100, canvasStrings);

    expect(group?.querySelector('.bpmn-exec-badge--token .bpmn-exec-badge__disc')).not.toBeNull();
    expect(group?.querySelector('.bpmn-exec-badge--incident .bpmn-exec-badge__sign')?.tagName).toBe('path');
  });

  it('clears the token badge rather than overlapping it', () => {
    const group = buildBadges(state({ tokenCount: 2, incidents: [incident('Boom')] }), 100, canvasStrings);

    const token = group?.querySelector('.bpmn-exec-badge--token .bpmn-exec-badge__disc');
    const sign = group?.querySelector('.bpmn-exec-badge--incident .bpmn-exec-badge__count');

    const tokenX = Number(token?.getAttribute('cx'));
    const signX = Number(sign?.getAttribute('x'));

    // A radius each plus the gap; anything less and the two badges would touch.
    expect(signX - tokenX).toBeGreaterThanOrEqual(18);
  });

  it('starts outside the element rather than on top of it', () => {
    const group = buildBadges(state({ tokenCount: 1 }), 100, canvasStrings);
    const disc = group?.querySelector('.bpmn-exec-badge__disc');

    // Centre plus radius must clear the right edge, or the badge sits over the element's own label.
    expect(Number(disc?.getAttribute('cx')) + Number(disc?.getAttribute('r'))).toBeGreaterThan(100);
  });

  it('never intercepts a pointer meant for the element', () => {
    const group = buildBadges(state({ tokenCount: 1 }), 100, canvasStrings);

    expect(group?.getAttribute('pointer-events')).toBe('none');
  });
});

describe('executionSummary', () => {
  it('localizes display status and counts without changing engine incident text', () => {
    const strings = { values: {
      ...canvasStrings.values,
      'Status.Failed': 'Échec',
      'Token.Many': '{0} jetons',
      'Incident.One': '{0} incident',
    } };
    const execution = state({ status: 'Failed', tokenCount: 2, incidents: [incident('Engine {0} failure')] });

    expect(executionSummary(execution, strings)).toBe('Échec, 2 jetons, 1 incident: Engine {0} failure');
    expect(execution.status).toBe('Failed');
    expect(execution.incidents[0]?.message).toBe('Engine {0} failure');
  });
  it('is the only thing a screen reader gets, so it names the status', () => {
    // Status is drawn as colour and a dash pattern, and both are invisible to a reader who is
    // being read to. If it is not in the title it is not conveyed at all.
    const group = buildBadges(state({ status: 'Waiting' }), 100, canvasStrings);

    expect(group?.querySelector('title')?.textContent).toBe('Waiting');
  });

  it('reads status, then tokens, then incidents, in the order they are drawn', () => {
    const summary = executionSummary(state({ status: 'Failed', tokenCount: 2, incidents: [incident('Timed out')] }), canvasStrings);

    expect(summary).toBe('Failed, 2 tokens, 1 incident: Timed out');
  });

  it('says nothing about counts that are not there', () => {
    expect(executionSummary(state({ status: 'Active' }), canvasStrings)).toBe('Active');
  });

  it('singularises one token', () => {
    expect(executionSummary(state({ tokenCount: 1 }), canvasStrings)).toBe('1 token');
  });
});

describe('incidentSummary', () => {
  it('quotes the first message', () => {
    expect(incidentSummary([incident('Broke'), incident('Also broke')], canvasStrings)).toBe('2 incidents: Broke');
  });

  it('counts without a message when there is none to quote', () => {
    expect(incidentSummary([], canvasStrings)).toBe('0 incidents');
  });
});

describe('statusClassOf', () => {
  it.each([
    ['Active', 'bpmn-exec--active'],
    ['Completed', 'bpmn-exec--completed'],
    ['Failed', 'bpmn-exec--failed'],
    ['Canceled', 'bpmn-exec--canceled'],
    ['Waiting', 'bpmn-exec--waiting'],
  ] as const)('maps %s to its own class', (status, expected) => {
    // Distinct classes because each status is a distinct line weight and dash pattern, not just a
    // different colour. Two statuses sharing a class would be two statuses a reader cannot separate.
    expect(statusClassOf(status)).toBe(expected);
  });
});
