import { describe, expect, it } from 'vitest';

import { TableFontMetrics, type LabelFontStyle } from '../../src/labels/font-metrics.js';
import { wrapText } from '../../src/labels/word-wrap.js';

/**
 * Specification section 4.5: wrapping must be deterministic, break at word boundaries, split
 * overlong tokens at grapheme clusters, honour explicit newlines, and never truncate.
 *
 * The table metrics provider makes every character exactly 6px wide at the 10px font size used
 * here, so expected line breaks are arithmetic rather than dependent on a font rasteriser.
 */
const metrics = new TableFontMetrics(0.6);

const style: LabelFontStyle = {
  fontFamily: 'test',
  fontSize: 10,
  fontWeight: 'normal',
  lineHeight: 1.2,
};

/** One character is 6px, so a width of 60 fits exactly ten characters. */
const TEN_CHARACTERS = 60;

describe('wrapText', () => {
  it('keeps text on one line when it fits', () => {
    expect(wrapText('short', TEN_CHARACTERS, style, metrics)).toEqual(['short']);
  });

  it('breaks at word boundaries rather than mid-word', () => {
    expect(wrapText('check the stock level', TEN_CHARACTERS, style, metrics)).toEqual([
      'check the',
      'stock',
      'level',
    ]);
  });

  it('treats an explicit newline as a mandatory break', () => {
    expect(wrapText('one\ntwo', 600, style, metrics)).toEqual(['one', 'two']);
  });

  it('preserves a blank line between paragraphs', () => {
    expect(wrapText('one\n\ntwo', 600, style, metrics)).toEqual(['one', '', 'two']);
  });

  it('splits a single token that is wider than the line', () => {
    // Nothing is dropped: the pieces reassemble to the original token.
    const lines = wrapText('abcdefghijklmnopqrstuvwxy', TEN_CHARACTERS, style, metrics);

    expect(lines).toEqual(['abcdefghij', 'klmnopqrst', 'uvwxy']);
    expect(lines.join('')).toBe('abcdefghijklmnopqrstuvwxy');
  });

  it('never truncates: every character survives wrapping', () => {
    const text = 'the quick brown fox jumps over the lazy dog';
    const lines = wrapText(text, TEN_CHARACTERS, style, metrics);

    expect(lines.join(' ')).toBe(text);
  });

  it('keeps an astral character intact when splitting an overlong token', () => {
    // Emoji are surrogate pairs; a UTF-16 split would produce two replacement characters.
    const lines = wrapText('AAAAAAAAAA\u{1F600}', TEN_CHARACTERS, style, metrics);

    expect(lines).toEqual(['AAAAAAAAAA', '\u{1F600}']);
  });

  it('emits a single line for an empty string rather than nothing', () => {
    expect(wrapText('', TEN_CHARACTERS, style, metrics)).toEqual(['']);
  });

  it('does not loop forever on a zero-width box', () => {
    expect(wrapText('anything', 0, style, metrics)).toEqual(['anything']);
  });

  it('is deterministic: the same input always wraps identically', () => {
    const first = wrapText('check the stock level', TEN_CHARACTERS, style, metrics);
    const second = wrapText('check the stock level', TEN_CHARACTERS, style, metrics);

    expect(first).toEqual(second);
  });
});
