import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { SelectionAnchorReporter } from '../../src/canvas/selection-anchor.js';
import type { AnchorContract } from '../../src/interop/contracts.js';

function anchorAt(x: number, y: number): AnchorContract {
  const box = { x, y, width: 100, height: 80 };
  return { bounds: box, client: box };
}

describe('SelectionAnchorReporter', () => {
  let target: EventTarget;
  let window_: EventTarget;
  let selection: readonly string[];
  let current: AnchorContract | null;
  let reports: { id: string; anchor: AnchorContract | null }[];
  let reporter: SelectionAnchorReporter;

  beforeEach(() => {
    vi.useFakeTimers();
    target = new EventTarget();
    window_ = new EventTarget();
    selection = ['Task_1'];
    current = anchorAt(10, 20);
    reports = [];
    reporter = new SelectionAnchorReporter(
      target,
      {
        selection: () => selection,
        anchorOf: () => current,
        report: (id, anchor) => { reports.push({ id, anchor }); },
      },
      150,
      window_ as Window,
    ).attach();
    reporter.selectionReported(current);
  });

  afterEach(() => {
    reporter.destroy();
    vi.useRealTimers();
  });

  it('hides the strip once when the selected element starts moving', () => {
    reporter.moved();
    reporter.moved();
    reporter.moved();

    expect(reports).toEqual([{ id: 'Task_1', anchor: null }]);
  });

  it('re-anchors at the new spot once the geometry is still', () => {
    reporter.moved();
    current = anchorAt(300, 40);
    vi.advanceTimersByTime(150);

    expect(reports.at(-1)).toEqual({ id: 'Task_1', anchor: anchorAt(300, 40) });
  });

  it('waits for the pointer to be released before showing it again', () => {
    target.dispatchEvent(new Event('pointerdown'));
    reporter.moved();
    current = anchorAt(300, 40);
    vi.advanceTimersByTime(1000);

    expect(reports).toHaveLength(1);

    window_.dispatchEvent(new Event('pointerup'));
    vi.advanceTimersByTime(150);

    expect(reports.at(-1)?.anchor).toEqual(anchorAt(300, 40));
  });

  it('re-anchors after a redraw that moved the element, without hiding it first', () => {
    current = anchorAt(50, 60);
    reporter.scheduleShow();
    vi.advanceTimersByTime(150);

    expect(reports).toEqual([{ id: 'Task_1', anchor: anchorAt(50, 60) }]);
  });

  it('sends nothing when the anchor has not changed', () => {
    reporter.scheduleShow();
    vi.advanceTimersByTime(150);

    expect(reports).toEqual([]);
  });

  it('gives way to a real selection change', () => {
    reporter.moved();
    reporter.selectionReported(anchorAt(500, 500));
    vi.advanceTimersByTime(1000);

    expect(reports).toHaveLength(1);
  });

  it('leaves a multi-selection alone', () => {
    selection = ['Task_1', 'Task_2'];
    reporter.moved();
    vi.advanceTimersByTime(1000);

    expect(reports).toEqual([]);
  });
});
