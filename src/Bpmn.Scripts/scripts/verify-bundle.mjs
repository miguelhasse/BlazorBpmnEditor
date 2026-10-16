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

/** Exported names the Blazor side calls by string — a bundle without them is broken. */
const REQUIRED_EXPORTS = ['initialize', 'dispose', 'loadDocument', 'setViewport', 'getInitResult'];

/** JointJS+ is commercially licensed and must never reach the shipped artifact. */
const FORBIDDEN_SUBSTRINGS = ['@joint/plus', 'joint.plus'];

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
