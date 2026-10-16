// Generates icons/bpmn-icons.json from the vendored bpmn-font SVG font.
//
// Specification section 4.5 requires the shipped package to contain normalized path data and a
// licence attribution only: no webfont, no bpmn-io dependency, no network access at application
// build or run time. So this runs by hand, its output is committed, and scripts/verify-icons.mjs
// fails the build if the two ever disagree.
//
// SVG fonts are Y-up with the baseline at y=0; SVG rendering is Y-down. Every glyph is therefore
// reflected and scaled into a 0 0 24 24 box, with the transform baked into the path data so a
// consumer can drop the path into any SVG without knowing where it came from.
//
// Usage: node tools/generate-icons.mjs [--check]

import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptsRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = path.resolve(scriptsRoot, '..', '..');
const iconsDir = path.join(repoRoot, 'icons');

const sourceSvg = path.join(iconsDir, '.source', 'bpmn.svg');
const provenanceFile = path.join(iconsDir, '.source', 'SOURCE.json');
const manifestFile = path.join(iconsDir, 'bpmn-icons.json');

/** The glyph box every icon is normalized into. */
const VIEW_BOX_SIZE = 24;

/**
 * Semantic assignments: which glyph renders which BPMN construct.
 *
 * This table is the single source of truth for that mapping, consumed by both the canvas and the
 * stencil so their notation cannot drift (implementation plan section 2). It lives in the
 * generated manifest rather than in TypeScript or C# for the same reason the rule table does.
 *
 * Selectors are matched most-specific-first by the consumer; `null` means "any".
 */
const ASSIGNMENTS = [
  // --- Gateways (section 4.1) ---------------------------------------------
  { kind: 'gateway', type: 'exclusiveGateway', iconId: 'gateway-xor' },
  { kind: 'gateway', type: 'inclusiveGateway', iconId: 'gateway-or' },
  { kind: 'gateway', type: 'parallelGateway', iconId: 'gateway-parallel' },
  { kind: 'gateway', type: 'complexGateway', iconId: 'gateway-complex' },
  { kind: 'gateway', type: 'eventBasedGateway', iconId: 'gateway-eventbased' },

  // --- Task type symbols (section 4.4) ------------------------------------
  // The bare glyph family, not the "*-task" pictograms: those bundle the rounded rectangle and
  // would double the body the editor already draws. A generic Task has no symbol, and a Call
  // Activity is denoted by its thick border rather than a top-left glyph, so neither appears.
  { kind: 'taskType', type: 'userTask', iconId: 'user' },
  { kind: 'taskType', type: 'manualTask', iconId: 'manual' },
  { kind: 'taskType', type: 'serviceTask', iconId: 'service' },
  { kind: 'taskType', type: 'scriptTask', iconId: 'script' },
  { kind: 'taskType', type: 'businessRuleTask', iconId: 'business-rule' },
  { kind: 'taskType', type: 'sendTask', iconId: 'send' },
  { kind: 'taskType', type: 'receiveTask', iconId: 'receive' },

  // --- Bottom-centre Activity markers (section 4.3) -----------------------
  { kind: 'activityMarker', marker: 'loop', iconId: 'loop-marker' },
  { kind: 'activityMarker', marker: 'multiInstanceParallel', iconId: 'parallel-mi-marker' },
  { kind: 'activityMarker', marker: 'multiInstanceSequential', iconId: 'sequential-mi-marker' },
  { kind: 'activityMarker', marker: 'compensation', iconId: 'compensation-marker' },
  { kind: 'activityMarker', marker: 'collapsed', iconId: 'sub-process-marker' },
  { kind: 'activityMarker', marker: 'adHoc', iconId: 'ad-hoc-marker' },

  // --- Data markers (section 1.6) -----------------------------------------
  // A collection Data Object carries the same three-bar symbol as parallel multi-instance.
  { kind: 'dataMarker', marker: 'collection', iconId: 'parallel-mi-marker' },
];

/**
 * Event trigger glyphs (section 4.2).
 *
 * bpmn-font names these per context, and the names are not mechanically derivable: a catching
 * Message is `intermediate-event-catch-message` while its throwing counterpart is
 * `intermediate-event-throw-message`, and Start/End have their own prefixes. Expanding the matrix
 * explicitly is what lets verify-icons.mjs prove every legal combination resolves.
 */
const EVENT_CONTEXT_PREFIX = {
  start: 'start-event',
  startNonInterrupting: 'start-event-non-interrupting',
  intermediateCatch: 'intermediate-event-catch',
  intermediateCatchNonInterrupting: 'intermediate-event-catch-non-interrupting',
  intermediateThrow: 'intermediate-event-throw',
  end: 'end-event',
};

/** Triggers that are legal in each context, per the section 1.1 event matrix. */
const EVENT_TRIGGERS = {
  start: ['message', 'timer', 'condition', 'signal', 'multiple', 'parallel-multiple'],
  startNonInterrupting: ['message', 'timer', 'condition', 'signal', 'multiple', 'parallel-multiple'],
  intermediateCatch: ['message', 'timer', 'condition', 'signal', 'multiple', 'parallel-multiple', 'link', 'error', 'cancel', 'compensation', 'escalation'],
  intermediateCatchNonInterrupting: ['message', 'timer', 'condition', 'signal', 'multiple', 'parallel-multiple', 'escalation'],
  intermediateThrow: ['message', 'signal', 'multiple', 'link', 'compensation', 'escalation'],
  end: ['message', 'signal', 'multiple', 'error', 'cancel', 'compensation', 'escalation', 'terminate'],
};

/** Maps this library's trigger vocabulary onto bpmn-font's glyph-name spelling. */
const TRIGGER_NAMES = {
  message: 'message',
  timer: 'timer',
  conditional: 'condition',
  signal: 'signal',
  multiple: 'multiple',
  parallelMultiple: 'parallel-multiple',
  link: 'link',
  error: 'error',
  cancel: 'cancel',
  compensation: 'compensation',
  escalation: 'escalation',
  terminate: 'terminate',
};

// ---------------------------------------------------------------------------
// SVG font parsing
// ---------------------------------------------------------------------------

function parseFontFace(svg) {
  const match = /<font-face\b([^>]*)\/?>/.exec(svg);
  if (match === null) throw new Error('No <font-face> element in the vendored font.');

  const attribute = (name) => {
    const found = new RegExp(`${name}="([^"]*)"`).exec(match[1]);
    return found === null ? null : Number(found[1]);
  };

  const unitsPerEm = attribute('units-per-em');
  const ascent = attribute('ascent');
  const descent = attribute('descent');

  if (unitsPerEm === null || ascent === null || descent === null) {
    throw new Error('<font-face> is missing units-per-em, ascent or descent.');
  }

  return { unitsPerEm, ascent, descent };
}

function parseGlyphs(svg) {
  const glyphs = new Map();

  for (const match of svg.matchAll(/<glyph\b([^>]*?)\/?>/g)) {
    const attributes = match[1];
    const name = /glyph-name="([^"]*)"/.exec(attributes)?.[1];
    const data = /\sd="([^"]*)"/.exec(attributes)?.[1];
    const unicode = /unicode="([^"]*)"/.exec(attributes)?.[1] ?? null;

    if (name === undefined || data === undefined) continue;
    glyphs.set(name, { name, data, unicode });
  }

  return glyphs;
}

// ---------------------------------------------------------------------------
// Path transformation
// ---------------------------------------------------------------------------

/** Rounds to a fixed precision so regeneration is byte-identical across platforms. */
const round = (value) => {
  const rounded = Number(value.toFixed(3));
  // Avoid emitting "-0", which differs textually from "0" while being numerically equal.
  return Object.is(rounded, -0) ? 0 : rounded;
};

/**
 * Bakes a reflection and uniform scale into path data.
 *
 * The font is Y-up about the baseline; SVG is Y-down. Absolute coordinates therefore become
 * `(x*s, (ascent - y)*s)` and relative deltas `(dx*s, -dy*s)`. The reflection also inverts an
 * arc's sweep direction and negates its x-axis rotation — quietly skipping that would mirror
 * every curved glyph.
 */
function transformPath(data, scale, ascent) {
  const tokens = data.match(/[A-Za-z]|-?\d*\.?\d+(?:e[-+]?\d+)?/gi) ?? [];
  const out = [];

  let index = 0;
  let command = '';

  const next = () => Number(tokens[index++]);
  const emit = (...values) => out.push(...values.map(round));

  while (index < tokens.length) {
    const token = tokens[index];

    if (/^[A-Za-z]$/.test(token)) {
      command = token;
      index += 1;
      out.push(command);
    } else if (command === '') {
      throw new Error(`Path data starts with a number rather than a command: ${data.slice(0, 40)}`);
    } else {
      // A repeated parameter group implies the previous command (and moveto implies lineto).
      out.push(command === 'M' ? 'L' : command === 'm' ? 'l' : command);
    }

    switch (command) {
      case 'M':
        emit(next() * scale, (ascent - next()) * scale);
        break;
      case 'm':
      case 'l':
      case 't':
        emit(next() * scale, -next() * scale);
        break;
      case 'L':
      case 'T':
        emit(next() * scale, (ascent - next()) * scale);
        break;
      case 'h':
        emit(next() * scale);
        break;
      case 'H':
        emit(next() * scale);
        break;
      case 'v':
        emit(-next() * scale);
        break;
      case 'V':
        emit((ascent - next()) * scale);
        break;
      case 'c':
        emit(next() * scale, -next() * scale, next() * scale, -next() * scale, next() * scale, -next() * scale);
        break;
      case 'C':
        emit(
          next() * scale, (ascent - next()) * scale,
          next() * scale, (ascent - next()) * scale,
          next() * scale, (ascent - next()) * scale,
        );
        break;
      case 's':
      case 'q':
        emit(next() * scale, -next() * scale, next() * scale, -next() * scale);
        break;
      case 'S':
      case 'Q':
        emit(next() * scale, (ascent - next()) * scale, next() * scale, (ascent - next()) * scale);
        break;
      case 'a':
      case 'A': {
        const rx = next() * scale;
        const ry = next() * scale;
        const rotation = -next();
        const largeArc = next();
        // The reflection reverses the direction of travel around the ellipse.
        const sweep = next() === 1 ? 0 : 1;
        const x = next();
        const y = next();
        emit(rx, ry, rotation);
        out.push(largeArc, sweep);
        emit(command === 'a' ? x * scale : x * scale, command === 'a' ? -y * scale : (ascent - y) * scale);
        break;
      }
      case 'z':
      case 'Z':
        break;
      default:
        throw new Error(`Unsupported path command '${command}' in the vendored font.`);
    }
  }

  // Join numbers with spaces but keep commands tight, producing stable, readable output.
  return out
    .map((value, position) => {
      const previous = out[position - 1];
      const needsSpace = position > 0 && typeof value === 'number' && typeof previous === 'number';
      return (needsSpace ? ' ' : '') + String(value);
    })
    .join('')
    .replace(/([A-Za-z])/g, ' $1')
    .trim();
}

// ---------------------------------------------------------------------------
// Inner-symbol extraction
// ---------------------------------------------------------------------------

/**
 * Splits transformed path data into subpaths, each with its bounding box.
 *
 * Control points are folded into the box rather than solving the curve, which over-estimates
 * slightly. That is the safe direction here: classification only needs to tell a full-size ring
 * from a small central symbol, and an over-estimate never shrinks a ring below the threshold.
 */
function splitSubpaths(data) {
  const tokens = data.match(/[A-Za-z]|-?\d*\.?\d+(?:e[-+]?\d+)?/gi) ?? [];
  const subpaths = [];

  let current = null;
  let command = '';
  let x = 0;
  let y = 0;
  let startX = 0;
  let startY = 0;
  let index = 0;

  const next = () => Number(tokens[index++]);

  const begin = () => {
    current = { parts: [], points: [], minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity };
    subpaths.push(current);
  };

  const include = (px, py) => {
    current.minX = Math.min(current.minX, px);
    current.minY = Math.min(current.minY, py);
    current.maxX = Math.max(current.maxX, px);
    current.maxY = Math.max(current.maxY, py);
    current.points.push([px, py]);
  };

  while (index < tokens.length) {
    const token = tokens[index];
    let implicit = true;

    if (/^[A-Za-z]$/.test(token)) {
      command = token;
      index += 1;
      implicit = false;
    }

    if ((command === 'M' || command === 'm') && !implicit) begin();
    if (current === null) begin();

    const emit = (...values) => {
      if (!implicit) current.parts.push(command);
      current.parts.push(...values.map(String));
    };

    switch (command) {
      case 'M':
      case 'L': {
        const nx = next();
        const ny = next();
        emit(nx, ny);
        x = nx;
        y = ny;
        if (command === 'M' && !implicit) {
          startX = x;
          startY = y;
          current.startX = x;
          current.startY = y;
        }
        include(x, y);
        break;
      }
      case 'm':
      case 'l': {
        const dx = next();
        const dy = next();
        emit(dx, dy);
        x += dx;
        y += dy;
        if (command === 'm' && !implicit) {
          startX = x;
          startY = y;
          // Remember where this subpath actually begins. Glyph subpaths after the first open
          // with a *relative* moveto chained off the previous subpath's end point, so a kept
          // subpath has to be re-anchored absolutely or dropping the ring displaces the symbol.
          current.startX = x;
          current.startY = y;
        }
        include(x, y);
        break;
      }
      case 'h': {
        const d = next();
        emit(d);
        x += d;
        include(x, y);
        break;
      }
      case 'H': {
        const v = next();
        emit(v);
        x = v;
        include(x, y);
        break;
      }
      case 'v': {
        const d = next();
        emit(d);
        y += d;
        include(x, y);
        break;
      }
      case 'V': {
        const v = next();
        emit(v);
        y = v;
        include(x, y);
        break;
      }
      case 'c': {
        const values = [next(), next(), next(), next(), next(), next()];
        emit(...values);
        include(x + values[0], y + values[1]);
        include(x + values[2], y + values[3]);
        x += values[4];
        y += values[5];
        include(x, y);
        break;
      }
      case 'C': {
        const values = [next(), next(), next(), next(), next(), next()];
        emit(...values);
        include(values[0], values[1]);
        include(values[2], values[3]);
        x = values[4];
        y = values[5];
        include(x, y);
        break;
      }
      case 'a': {
        const values = [next(), next(), next(), next(), next(), next(), next()];
        emit(...values);
        // An arc bulges by at most its radii beyond the endpoint.
        include(x + values[5], y + values[6]);
        include(x + values[5] - values[0], y + values[6] - values[1]);
        include(x + values[5] + values[0], y + values[6] + values[1]);
        x += values[5];
        y += values[6];
        break;
      }
      case 'z':
      case 'Z':
        current.parts.push('z');
        x = startX;
        y = startY;
        break;
      default:
        throw new Error(`Unsupported command '${command}' while splitting subpaths.`);
    }
  }

  return subpaths.map((subpath) => {
    // Re-anchor the opening moveto absolutely: the parts still hold whatever the font wrote,
    // which for every subpath but the first is relative to the previous one.
    const parts = [...subpath.parts];
    if (parts[0] === 'M' || parts[0] === 'm') {
      parts.splice(0, 3, 'M', String(round(subpath.startX)), String(round(subpath.startY)));
    }
    return { ...subpath, text: parts.join(' ') };
  });
}

/**
 * Removes a pictogram's outer ring or diamond, leaving the inner trigger or type symbol.
 *
 * bpmn-font draws complete pictograms: `gateway-xor` is a diamond *and* an X, and
 * `start-event-message` is a circle *and* an envelope. Specification sections 4.1 and 4.2 require
 * the editor to own that outer geometry, because ring thickness and dash pattern encode event
 * context and must stay under the renderer's control. The outer geometry is therefore stripped
 * here, once, at generation time, with the result committed and reviewed on a contact sheet.
 *
 * Two independent rules, because a dashed ring is not one big subpath but many small ones:
 *  - a subpath spanning almost the whole glyph is the outer ring or diamond;
 *  - a subpath that never reaches the middle is one dash of that ring.
 *
 * Removing everything is a generation failure, not a silently empty icon.
 */
function extractInnerSymbol(iconId, data) {
  const subpaths = splitSubpaths(data);
  if (subpaths.length === 1) {
    throw new Error(`'${iconId}' is a single subpath; its outer geometry cannot be separated.`);
  }

  const extent = {
    minX: Math.min(...subpaths.map((s) => s.minX)),
    minY: Math.min(...subpaths.map((s) => s.minY)),
    maxX: Math.max(...subpaths.map((s) => s.maxX)),
    maxY: Math.max(...subpaths.map((s) => s.maxY)),
  };

  const width = extent.maxX - extent.minX;
  const height = extent.maxY - extent.minY;
  const centreX = (extent.minX + extent.maxX) / 2;
  const centreY = (extent.minY + extent.maxY) / 2;

  // Classify radially rather than by bounding box. A ring is an annulus — an outer contour and
  // a concentric inner one — and a dashed ring is a dozen small arcs. All of them share the
  // property that every one of their points sits out near the rim, while a trigger symbol always
  // reaches in toward the middle. Bounding boxes cannot tell those apart: the annulus's inner
  // contour is not much smaller than its outer one, and a dash's box is tiny.
  const radius = Math.max(width, height) / 2;

  // Two properties together identify outer geometry, because neither alone is enough.
  //
  // Radius: measured across the whole font, the tightest ring contour sits at 0.564 of the glyph
  // radius and the widest symbol at 0.485 (the Event-Based Gateway's outer circle, with a
  // Terminate Event's filled disc just behind it at 0.441). The boundary goes between them.
  //
  // Circularity: a ring is a contour of near-constant radius. This is what stops a Terminate
  // Event's disc and an Event-Based Gateway's circles — symbols that happen to be circular —
  // from being decided on radius alone, and what identifies each short arc of a dashed ring.
  //
  // The two populations are only ~0.08 apart, so the guard below is not a formality: this
  // classification is calibrated to one pinned font, and a version bump has to be re-measured.
  const RIM = 0.52;
  const CIRCULAR = 0.75;
  /** A subpath this close to the radius boundary has been guessed at, not classified. */
  const AMBIGUOUS = 0.03;

  const profiles = subpaths.map((subpath) => {
    const radii = subpath.points.map(([px, py]) => Math.hypot(px - centreX, py - centreY));
    const nearest = Math.min(...radii) / radius;
    const furthest = Math.max(...radii) / radius;
    return { subpath, nearest, circularity: furthest === 0 ? 1 : nearest / furthest };
  });

  const ambiguous = profiles.find(
    (profile) => profile.circularity >= CIRCULAR && Math.abs(profile.nearest - RIM) < AMBIGUOUS,
  );
  if (ambiguous !== undefined) {
    throw new Error(
      `'${iconId}' has a circular subpath at ${ambiguous.nearest.toFixed(3)} of the glyph radius, too `
      + `close to the ${RIM} boundary to classify as ring or symbol. The vendored font has changed `
      + `shape: re-measure with "node tools/generate-icons.mjs --inspect ${iconId}" before trusting it.`,
    );
  }

  const kept = profiles
    .filter((profile) => !(profile.nearest >= RIM && profile.circularity >= CIRCULAR))
    .map((profile) => profile.subpath);

  if (kept.length === 0) {
    throw new Error(`Extracting the inner symbol of '${iconId}' removed every subpath.`);
  }

  return kept.map((subpath) => subpath.text).join(' ');
}

/** Pictogram families whose outer geometry the editor draws itself. */
const EXTRACT_INNER_SYMBOL = new Set(['gateway', 'eventTrigger']);

// ---------------------------------------------------------------------------
// Manifest assembly
// ---------------------------------------------------------------------------

function accessibilityLabel(iconId) {
  return iconId
    .split('-')
    .map((word, position) => (position === 0 ? word.charAt(0).toUpperCase() + word.slice(1) : word))
    .join(' ');
}

function buildEventAssignments(glyphs, missing) {
  const assignments = [];

  for (const [context, prefix] of Object.entries(EVENT_CONTEXT_PREFIX)) {
    for (const [trigger, fontName] of Object.entries(TRIGGER_NAMES)) {
      if (!EVENT_TRIGGERS[context].includes(fontName)) continue;

      const iconId = `${prefix}-${fontName}`;
      if (!glyphs.has(iconId)) {
        missing.push(`${context}/${trigger} -> ${iconId}`);
        continue;
      }

      assignments.push({ kind: 'eventTrigger', context, trigger, iconId });
    }
  }

  return assignments;
}

async function build() {
  const svg = await readFile(sourceSvg, 'utf8');
  const provenance = JSON.parse(await readFile(provenanceFile, 'utf8'));

  const { unitsPerEm, ascent, descent } = parseFontFace(svg);
  const glyphs = parseGlyphs(svg);
  if (glyphs.size === 0) throw new Error('The vendored font contains no glyphs.');

  const scale = VIEW_BOX_SIZE / unitsPerEm;

  const missing = [];
  const eventAssignments = buildEventAssignments(glyphs, missing);

  for (const assignment of ASSIGNMENTS) {
    if (!glyphs.has(assignment.iconId)) missing.push(`${assignment.kind} -> ${assignment.iconId}`);
  }

  if (missing.length > 0) {
    // Source drift must fail generation rather than silently change notation (section 4.5).
    throw new Error(`The vendored font is missing required glyphs:\n  ${missing.join('\n  ')}`);
  }

  const assignments = [...ASSIGNMENTS, ...eventAssignments];
  const referenced = new Set(assignments.map((assignment) => assignment.iconId));

  const extracted = new Set(
    assignments
      .filter((assignment) => EXTRACT_INNER_SYMBOL.has(assignment.kind))
      .map((assignment) => assignment.iconId),
  );

  const icons = [...referenced]
    .sort()
    .map((iconId) => {
      const glyph = glyphs.get(iconId);
      const isInnerSymbol = extracted.has(iconId);
      return {
        iconId,
        upstreamName: glyph.name,
        // True when the outer ring or diamond was stripped, so a consumer knows the editor is
        // still responsible for drawing it.
        isInnerSymbol,
        codePoint: glyph.unicode === null ? null : `U+${glyph.unicode.codePointAt(0).toString(16).toUpperCase()}`,
        pathData: isInnerSymbol
          ? extractInnerSymbol(iconId, transformPath(glyph.data, scale, ascent))
          : transformPath(glyph.data, scale, ascent),
        viewBox: `0 0 ${VIEW_BOX_SIZE} ${VIEW_BOX_SIZE}`,
        // The baseline in the normalized box, kept so a consumer can align a glyph with text.
        baseline: round(ascent * scale),
        alignment: 'middle',
        accessibilityLabel: accessibilityLabel(iconId),
      };
    });

  const manifest = {
    $comment:
      'Generated by src/Bpmn.Scripts/tools/generate-icons.mjs from the vendored bpmn-font in '
      + 'icons/.source. Do not edit by hand: run "npm run icons:generate" in src/Bpmn.Scripts.',
    manifestVersion: '1.0.0',
    source: {
      repository: 'https://github.com/bpmn-io/bpmn-font',
      package: provenance.package,
      version: provenance.version,
      sourceFile: provenance.sourceFile,
      sourceSha256: provenance.sourceSha256,
      license: provenance.license,
      licenseHolder: provenance.licenseHolder,
      attribution:
        'BPMN icons derived from bpmn-font (c) Camunda Services GmbH, SIL Open Font License 1.1. '
        + 'The full licence text is vendored at icons/.source/LICENSE.',
    },
    fontMetrics: { unitsPerEm, ascent, descent },
    icons,
    assignments,
  };

  // A digest over the meaningful content, so a consumer can prove which manifest it loaded.
  manifest.contentHash = createHash('sha256')
    .update(JSON.stringify({ icons: manifest.icons, assignments: manifest.assignments }))
    .digest('hex');

  return `${JSON.stringify(manifest, null, 2)}\n`;
}

// --inspect <glyph> reports each subpath's radial profile, which is how the ring-detection
// thresholds below were chosen rather than guessed.
const inspectIndex = process.argv.indexOf('--inspect');
if (inspectIndex >= 0) {
  const target = process.argv[inspectIndex + 1];
  const svg = await readFile(sourceSvg, 'utf8');
  const { unitsPerEm, ascent } = parseFontFace(svg);
  const glyph = parseGlyphs(svg).get(target);
  if (glyph === undefined) throw new Error(`No glyph named '${target}'.`);

  const subpaths = splitSubpaths(transformPath(glyph.data, VIEW_BOX_SIZE / unitsPerEm, ascent));
  const extent = {
    minX: Math.min(...subpaths.map((s) => s.minX)), minY: Math.min(...subpaths.map((s) => s.minY)),
    maxX: Math.max(...subpaths.map((s) => s.maxX)), maxY: Math.max(...subpaths.map((s) => s.maxY)),
  };
  const cx = (extent.minX + extent.maxX) / 2;
  const cy = (extent.minY + extent.maxY) / 2;
  const radius = Math.max(extent.maxX - extent.minX, extent.maxY - extent.minY) / 2;

  console.log(`${target}: ${subpaths.length} subpaths, radius ${radius.toFixed(2)}`);
  subpaths.forEach((subpath, i) => {
    const radii = subpath.points.map(([x, y]) => Math.hypot(x - cx, y - cy));
    const min = Math.min(...radii) / radius;
    const max = Math.max(...radii) / radius;
    console.log(
      `  #${i} minR=${min.toFixed(3)} maxR=${max.toFixed(3)} ratio=${(min / max).toFixed(3)} points=${radii.length}`,
    );
  });
  process.exit(0);
}

const generated = await build();
const isCheck = process.argv.includes('--check');

if (isCheck) {
  const existing = await readFile(manifestFile, 'utf8').catch(() => null);
  if (existing !== generated) {
    console.error(
      'icons/bpmn-icons.json is out of date with the vendored font.\n'
      + 'Run "npm run icons:generate" in src/Bpmn.Scripts and commit the result.',
    );
    process.exit(1);
  }
  console.log('icons manifest up to date');
} else {
  await writeFile(manifestFile, generated, 'utf8');
  const parsed = JSON.parse(generated);
  console.log(
    `wrote icons/bpmn-icons.json — ${parsed.icons.length} icons, `
    + `${parsed.assignments.length} assignments, content ${parsed.contentHash.slice(0, 12)}`,
  );
}
