import { describe, expect, it } from 'vitest';

import * as constants from '../../src/geometry/constants.js';
import { layoutActivityMarkers, markersFor } from '../../src/markers/activity-markers.js';
import type { BpmnActivityMarker } from '../../src/icons/registry.js';
import { flowNode } from '../fixtures/documents.js';

/**
 * Specification section 4.3 composition rules.
 *
 * These are notational, not cosmetic: showing two loop markers, or ordering them differently
 * between renders, changes what the diagram says about the Activity.
 */
describe('markersFor', () => {
  it('shows no marker on a plain Task', () => {
    expect(markersFor(flowNode('Task_1', 'task'), null)).toEqual([]);
  });

  it.each([
    ['standard', 'loop'],
    ['multiInstanceParallel', 'multiInstanceParallel'],
    ['multiInstanceSequential', 'multiInstanceSequential'],
  ] as const)('maps %s loop characteristics to the %s marker', (loop, expected) => {
    expect(markersFor(flowNode('Task_1', 'task', { loopCharacteristics: loop }), null)).toEqual([expected]);
  });

  it('never shows more than one loop or multi-instance marker', () => {
    // The three are mutually exclusive in the model, so this is really a guard against a future
    // change making them additive.
    const markers = markersFor(
      flowNode('Task_1', 'task', { loopCharacteristics: 'multiInstanceSequential' }),
      null,
    );

    const loopMarkers: BpmnActivityMarker[] = ['loop', 'multiInstanceParallel', 'multiInstanceSequential'];
    expect(markers.filter((marker) => loopMarkers.includes(marker))).toHaveLength(1);
  });

  it('shows compensation alongside a loop marker', () => {
    const markers = markersFor(
      flowNode('Task_1', 'task', { isForCompensation: true, loopCharacteristics: 'standard' }),
      null,
    );

    // Compensation comes first: section 4.3 fixes the order so layout is deterministic.
    expect(markers).toEqual(['compensation', 'loop']);
  });

  it('shows the collapsed marker on a Sub-Process that is not expanded', () => {
    expect(markersFor(flowNode('Sub_1', 'subProcess'), false)).toEqual(['collapsed']);
    expect(markersFor(flowNode('Sub_1', 'subProcess'), null)).toEqual(['collapsed']);
    expect(markersFor(flowNode('Sub_1', 'subProcess'), true)).toEqual([]);
  });

  it('does not show the collapsed marker on a Call Activity', () => {
    // A Call Activity is opaque too, but its notation is the thick border (section 1.2).
    expect(markersFor(flowNode('Call_1', 'callActivity'), false)).toEqual([]);
  });

  it('shows the ad-hoc marker only on an Ad-Hoc Sub-Process', () => {
    expect(markersFor(flowNode('Sub_1', 'adHocSubProcess'), true)).toEqual(['adHoc']);
    expect(markersFor(flowNode('Sub_1', 'subProcess'), true)).toEqual([]);
  });

  it('orders four simultaneous markers as compensation, loop, collapsed, ad-hoc', () => {
    const markers = markersFor(
      flowNode('Sub_1', 'adHocSubProcess', {
        isForCompensation: true,
        loopCharacteristics: 'multiInstanceParallel',
      }),
      false,
    );

    expect(markers).toEqual(['compensation', 'multiInstanceParallel', 'collapsed', 'adHoc']);
  });
});

describe('layoutActivityMarkers', () => {
  const bounds = { x: 40, y: 60, width: 100, height: 80 };

  it('places nothing when there are no markers', () => {
    expect(layoutActivityMarkers([], bounds)).toEqual([]);
  });

  it('centres a single marker on the bottom edge', () => {
    const [placement] = layoutActivityMarkers(['collapsed'], bounds);

    // Section 4.3: 14px box, 20px above the bottom edge, centred on the shape.
    expect(placement?.x).toBe(bounds.width / 2 - constants.ACTIVITY_MARKER_BOX / 2);
    expect(placement?.y).toBe(bounds.height - constants.ACTIVITY_MARKER_BASELINE_OFFSET);
    expect(placement?.size).toBe(constants.ACTIVITY_MARKER_BOX);
  });

  it('spaces a group by the configured gap and centres the whole group', () => {
    const placements = layoutActivityMarkers(['compensation', 'loop', 'collapsed'], bounds);

    const box = constants.ACTIVITY_MARKER_BOX;
    const gap = constants.ACTIVITY_MARKER_GAP;
    const groupWidth = box * 3 + gap * 2;

    expect(placements).toHaveLength(3);
    expect(placements[0]?.x).toBe(bounds.width / 2 - groupWidth / 2);
    expect(placements[1]?.x).toBe(bounds.width / 2 - groupWidth / 2 + box + gap);
    expect(placements[2]?.x).toBe(bounds.width / 2 - groupWidth / 2 + (box + gap) * 2);
  });

  it('keeps the group centred regardless of how many markers it holds', () => {
    for (const markers of [
      ['collapsed'],
      ['loop', 'collapsed'],
      ['compensation', 'loop', 'collapsed'],
      ['compensation', 'loop', 'collapsed', 'adHoc'],
    ] as BpmnActivityMarker[][]) {
      const placements = layoutActivityMarkers(markers, bounds);
      const first = placements[0];
      const last = placements.at(-1);
      expect(first).toBeDefined();
      expect(last).toBeDefined();

      const centre = ((first?.x ?? 0) + (last?.x ?? 0) + constants.ACTIVITY_MARKER_BOX) / 2;
      expect(centre).toBeCloseTo(bounds.width / 2, 9);
    }
  });

  it('resolves real path data for every marker', () => {
    const placements = layoutActivityMarkers(
      ['compensation', 'loop', 'multiInstanceParallel', 'multiInstanceSequential', 'collapsed', 'adHoc'],
      bounds,
    );

    expect(placements).toHaveLength(6);
    for (const placement of placements) {
      expect(placement.pathData).toMatch(/^M/);
      expect(placement.viewBox).toBe('0 0 24 24');
    }
  });
});
