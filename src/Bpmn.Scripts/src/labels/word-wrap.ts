import type { FontMetricsProvider, LabelFontStyle } from './font-metrics.js';

/**
 * Deterministic word wrapping (specification section 4.5).
 *
 * Rules, in priority order:
 *  1. Explicit newlines are mandatory breaks and are never collapsed.
 *  2. Prefer Unicode word boundaries, so CJK and scripts without spaces wrap correctly.
 *  3. A single token wider than the line is split at grapheme-cluster boundaries, so an
 *     emoji or a combining sequence is never cut in half.
 *  4. Text is never truncated. Clipping a fixed label box is a presentation choice made
 *     later; the full string always survives here.
 */

const wordSegmenter = createSegmenter('word');
const graphemeSegmenter = createSegmenter('grapheme');

export function wrapText(
  text: string,
  maxWidth: number,
  style: LabelFontStyle,
  metrics: FontMetricsProvider,
): readonly string[] {
  if (text === '') return [''];

  const lines: string[] = [];
  for (const paragraph of text.split('\n')) {
    if (paragraph === '') {
      lines.push('');
      continue;
    }
    lines.push(...wrapParagraph(paragraph, maxWidth, style, metrics));
  }
  return lines;
}

function wrapParagraph(
  paragraph: string,
  maxWidth: number,
  style: LabelFontStyle,
  metrics: FontMetricsProvider,
): readonly string[] {
  // A non-positive box cannot wrap meaningfully; emit one line rather than loop forever.
  if (maxWidth <= 0) return [paragraph];

  const lines: string[] = [];
  let current = '';

  for (const chunk of segmentWords(paragraph)) {
    const candidate = current + chunk;
    if (metrics.measure(candidate.trimEnd(), style) <= maxWidth) {
      current = candidate;
      continue;
    }

    // The chunk does not fit after existing content: break the line first.
    if (current.trimEnd() !== '') {
      lines.push(current.trimEnd());
      current = chunk.trimStart();
      if (metrics.measure(current, style) <= maxWidth) continue;
    } else {
      current = chunk;
    }

    // Still too wide on its own: it is one long token, so split by graphemes.
    if (metrics.measure(current.trimEnd(), style) > maxWidth) {
      const pieces = splitOversizedToken(current, maxWidth, style, metrics);
      lines.push(...pieces.slice(0, -1));
      current = pieces.at(-1) ?? '';
    }
  }

  if (current.trimEnd() !== '' || lines.length === 0) lines.push(current.trimEnd());
  return lines;
}

function splitOversizedToken(
  token: string,
  maxWidth: number,
  style: LabelFontStyle,
  metrics: FontMetricsProvider,
): readonly string[] {
  const pieces: string[] = [];
  let current = '';

  for (const grapheme of segmentGraphemes(token)) {
    const candidate = current + grapheme;
    if (current !== '' && metrics.measure(candidate, style) > maxWidth) {
      pieces.push(current);
      current = grapheme;
    } else {
      current = candidate;
    }
  }

  pieces.push(current);
  return pieces;
}

/**
 * Splits into word-plus-trailing-whitespace chunks, so a break always happens at a word
 * boundary and the space that justified it is dropped from the end of the line.
 */
function segmentWords(text: string): readonly string[] {
  if (wordSegmenter !== null) {
    const chunks: string[] = [];
    for (const { segment } of wordSegmenter.segment(text)) {
      const last = chunks.at(-1);
      // Whitespace belongs to the word it follows: that keeps "word " as one unit and
      // lets trimEnd drop it exactly when the line breaks there.
      if (last !== undefined && /^\s+$/u.test(segment)) {
        chunks[chunks.length - 1] = last + segment;
      } else {
        chunks.push(segment);
      }
    }
    return chunks;
  }

  return text.split(/(?<=\s)/u);
}

function segmentGraphemes(text: string): readonly string[] {
  if (graphemeSegmenter !== null) {
    return [...graphemeSegmenter.segment(text)].map((entry) => entry.segment);
  }
  // Iterating a string yields code points, which keeps surrogate pairs intact even
  // where Intl.Segmenter is unavailable. Combining marks may separate from their base
  // in that fallback; every supported runtime ships Intl.Segmenter, so this is a floor,
  // not the normal path.
  const codePoints: string[] = [];
  for (const codePoint of text) codePoints.push(codePoint);
  return codePoints;
}

function createSegmenter(granularity: 'word' | 'grapheme'): Intl.Segmenter | null {
  if (typeof Intl === 'undefined' || typeof Intl.Segmenter !== 'function') return null;
  return new Intl.Segmenter(undefined, { granularity });
}
