import { describe, expect, it } from 'vitest';

import { findBinding, matches, type KeyStroke } from '../../src/input/shortcuts.js';
import type { ShortcutBindingContract } from '../../src/interop/contracts.js';

/**
 * Shortcut matching.
 *
 * The two rules that matter are both easy to get wrong and invisible once wrong: modifiers must
 * match exactly rather than as a prefix, and a printable key must compare case-insensitively
 * because Shift changes what the browser reports.
 */
const binding = (overrides: Partial<ShortcutBindingContract> = {}): ShortcutBindingContract => ({
  command: 'zoomIn',
  key: '+',
  ctrl: false,
  shift: false,
  alt: false,
  meta: false,
  description: 'Zoom in',
  ...overrides,
});

const stroke = (overrides: Partial<KeyStroke> = {}): KeyStroke => ({
  key: '+',
  ctrlKey: false,
  shiftKey: false,
  altKey: false,
  metaKey: false,
  ...overrides,
});

describe('matches', () => {
  it('matches an unmodified key', () => {
    expect(matches(stroke(), binding())).toBe(true);
  });

  it('requires every modifier to agree', () => {
    expect(matches(stroke({ ctrlKey: true }), binding())).toBe(false);
    expect(matches(stroke(), binding({ ctrl: true }))).toBe(false);
  });

  it('does not treat a binding as a prefix of a longer combination', () => {
    // Ctrl+0 and Ctrl+Shift+0 are different bindings. If the first matched the second's
    // keystroke, one of the two would be permanently unreachable.
    const ctrlZero = binding({ key: '0', ctrl: true });

    expect(matches(stroke({ key: '0', ctrlKey: true }), ctrlZero)).toBe(true);
    expect(matches(stroke({ key: '0', ctrlKey: true, shiftKey: true }), ctrlZero)).toBe(false);
  });

  it('compares a printable key case-insensitively', () => {
    // Shift+a reports key 'A'; a host that wrote { key: 'a', shift: true } means that.
    const shiftA = binding({ key: 'a', shift: true });

    expect(matches(stroke({ key: 'A', shiftKey: true }), shiftA)).toBe(true);
  });

  it('compares a named key exactly', () => {
    // 'ArrowLeft' and 'arrowleft' are not the same key name, and lowercasing named keys would
    // let a typo silently work.
    expect(matches(stroke({ key: 'ArrowLeft' }), binding({ key: 'ArrowLeft' }))).toBe(true);
    expect(matches(stroke({ key: 'arrowleft' }), binding({ key: 'ArrowLeft' }))).toBe(false);
  });

  it('distinguishes ctrl from meta', () => {
    // A host targeting macOS binds Cmd; treating the two as interchangeable would fire both.
    expect(matches(stroke({ metaKey: true }), binding({ ctrl: true }))).toBe(false);
  });
});

describe('findBinding', () => {
  it('returns null when nothing matches', () => {
    expect(findBinding(stroke({ key: 'q' }), [binding()])).toBeNull();
  });

  it('returns the first match, so a duplicated binding does not break initialization', () => {
    const first = binding({ command: 'zoomIn' });
    const second = binding({ command: 'zoomOut' });

    expect(findBinding(stroke(), [first, second])?.command).toBe('zoomIn');
  });

  it('picks the more specific binding when both are configured', () => {
    const plain = binding({ key: '0', command: 'resetZoom' });
    const shifted = binding({ key: '0', shift: true, command: 'fitToContent' });

    expect(findBinding(stroke({ key: '0', shiftKey: true }), [plain, shifted])?.command)
      .toBe('fitToContent');
  });
});
