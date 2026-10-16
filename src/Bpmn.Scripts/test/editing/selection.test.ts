import { describe, expect, it } from 'vitest';

import { Selection } from '../../src/editing/selection.js';

/**
 * The selection set.
 *
 * The behaviour worth pinning is what a plain press does to an existing multi-selection: collapse
 * it and dragging a group becomes impossible, because pressing down on a member to start the drag
 * would throw the rest away first.
 */
describe('Selection', () => {
  it('starts empty', () => {
    expect(Selection.empty.isEmpty).toBe(true);
    expect(Selection.empty.ids).toEqual([]);
  });

  it('replaces the selection on a plain press', () => {
    const selection = Selection.of(['a', 'b']).apply('c', 'replace');

    expect(selection.ids).toEqual(['c']);
  });

  it('keeps a multi-selection when pressing one of its members', () => {
    // Otherwise pressing down on a selected shape to drag the group would discard the group.
    const selection = Selection.of(['a', 'b', 'c']);

    expect(selection.apply('b', 'replace').ids).toEqual(['a', 'b', 'c']);
  });

  it('adds and removes with toggle', () => {
    const added = Selection.of(['a']).apply('b', 'toggle');
    expect(added.ids).toEqual(['a', 'b']);
    expect(added.apply('a', 'toggle').ids).toEqual(['b']);
  });

  it('reports ids in a stable order regardless of click order', () => {
    // The payload crosses interop; an order that depended on clicks would make every selection
    // look changed to anything comparing them.
    const forwards = Selection.of(['a', 'b', 'c']);
    const backwards = Selection.of(['c', 'b', 'a']);

    expect(forwards.ids).toEqual(backwards.ids);
  });

  it('drops ids that are no longer present', () => {
    const selection = Selection.of(['a', 'b', 'c']);

    expect(selection.retaining((id) => id !== 'b').ids).toEqual(['a', 'c']);
  });

  it('returns itself when retaining changes nothing', () => {
    // Identity is what lets the caller skip a re-render and an interop message.
    const selection = Selection.of(['a', 'b']);

    expect(selection.retaining(() => true)).toBe(selection);
  });

  it('compares by content, not by order or identity', () => {
    expect(Selection.of(['a', 'b']).equals(Selection.of(['b', 'a']))).toBe(true);
    expect(Selection.of(['a']).equals(Selection.of(['a', 'b']))).toBe(false);
    expect(Selection.of([]).equals(Selection.empty)).toBe(true);
  });

  it('clears to the shared empty instance', () => {
    expect(Selection.of(['a']).clear().isEmpty).toBe(true);
    expect(Selection.empty.clear()).toBe(Selection.empty);
  });
});
