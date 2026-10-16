// Post-build gate for the npm bundle.
//
// `dotnet build`'s own VerifyBpmnScriptsBundle target can only check that files exist,
// which a zero-export bundle passes. Content checks belong here, where the artifact is.
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptsRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const pkg = JSON.parse(await readFile(path.join(scriptsRoot, 'package.json'), 'utf8'));

const jsBundle = path.join(scriptsRoot, pkg.main);
const cssBundle = path.join(scriptsRoot, pkg.cssBundle);
const manifest = path.join(scriptsRoot, 'dist', 'bundle-manifest.json');

/** Minimum plausible size for a bundle that actually contains JointJS + our modules. */
const MIN_JS_BYTES = 50_000;
const MIN_CSS_BYTES = 200;

/**
 * Size budgets: a little above what the bundle weighs today (664,044 B JS minified, 12,908 B CSS, Release). They are a tripwire
 * for an accidental import (a whole library, a bundled data file), not a target; raise them deliberately, with a reason.
 * A Debug bundle is not minified (1,439,146 B today), so it has a budget of its own: the same tripwire at the size it actually has.
 */
const MAX_JS_BYTES_RELEASE = 700_000;
const MAX_JS_BYTES_DEBUG = 1_600_000;
const MAX_CSS_BYTES = 16_000;

/** Exported names the Blazor side calls by string — a bundle without them is broken. */
const REQUIRED_EXPORTS = ['initialize', 'dispose', 'loadDocument', 'deleteElements', 'setViewport', 'getInitResult'];

/** JointJS+ is commercially licensed and must never reach the shipped artifact. */
const FORBIDDEN_SUBSTRINGS = ['@joint/plus', 'joint.plus'];

let buildMode = 'Release';
try {
  buildMode = JSON.parse(await readFile(manifest, 'utf8')).buildMode === 'Debug' ? 'Debug' : 'Release';
} catch {
  // A missing or unreadable manifest is reported below; judge the bundle by the stricter budget meanwhile.
}

const MAX_JS_BYTES = buildMode === 'Debug' ? MAX_JS_BYTES_DEBUG : MAX_JS_BYTES_RELEASE;

const failures = [];

async function sizeOf(file) {
  try {
    return (await stat(file)).size;
  } catch {
    return -1;
  }
}

const jsSize = await sizeOf(jsBundle);
if (jsSize < 0) {
  failures.push(`JS bundle missing: ${jsBundle}`);
} else if (jsSize < MIN_JS_BYTES) {
  failures.push(
    `JS bundle is only ${jsSize} bytes (expected at least ${MIN_JS_BYTES}). ` +
      'The entry point probably exports nothing — check src/index.ts.',
  );
} else if (jsSize > MAX_JS_BYTES) {
  failures.push(
    `JS bundle is ${jsSize} bytes, over the ${MAX_JS_BYTES} budget. ` +
      'Find what grew (esbuild --metafile) or raise MAX_JS_BYTES in scripts/verify-bundle.mjs with a reason.',
  );
} else {
  const contents = await readFile(jsBundle, 'utf8');
  for (const name of REQUIRED_EXPORTS) {
    if (!contents.includes(name)) {
      failures.push(`JS bundle does not contain the required export '${name}'.`);
    }
  }
  for (const forbidden of FORBIDDEN_SUBSTRINGS) {
    if (contents.includes(forbidden)) {
      failures.push(`JS bundle references '${forbidden}' — JointJS+ is out of scope.`);
    }
  }
}

const cssSize = await sizeOf(cssBundle);
if (cssSize < 0) {
  failures.push(`CSS bundle missing: ${cssBundle}`);
} else if (cssSize < MIN_CSS_BYTES) {
  failures.push(`CSS bundle is only ${cssSize} bytes (expected at least ${MIN_CSS_BYTES}).`);
} else if (cssSize > MAX_CSS_BYTES) {
  failures.push(`CSS bundle is ${cssSize} bytes, over the ${MAX_CSS_BYTES} budget.`);
}

if ((await sizeOf(manifest)) < 0) {
  failures.push(`Bundle manifest missing: ${manifest}`);
}

if (failures.length > 0) {
  console.error('\nBundle verification failed:');
  for (const failure of failures) console.error(`  - ${failure}`);
  console.error('');
  process.exit(1);
}

console.log(
  `bundle ok  js=${jsSize.toLocaleString()}B  css=${cssSize.toLocaleString()}B  exports=${REQUIRED_EXPORTS.join(',')}`,
);
