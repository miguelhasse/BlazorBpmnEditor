// Gates the generated icon manifest.
//
// Three failures this catches, all of which would otherwise surface as a silently wrong diagram:
// a manifest that no longer matches the vendored font, an assignment pointing at an icon that
// does not exist, and an icon whose path data is empty or malformed.

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptsRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const manifestFile = path.resolve(scriptsRoot, '..', '..', 'icons', 'bpmn-icons.json');

const failures = [];
const manifest = JSON.parse(await readFile(manifestFile, 'utf8'));

const byId = new Map(manifest.icons.map((icon) => [icon.iconId, icon]));

if (manifest.icons.length === 0) failures.push('The manifest contains no icons.');
if (manifest.assignments.length === 0) failures.push('The manifest contains no assignments.');

for (const icon of manifest.icons) {
  if (typeof icon.pathData !== 'string' || icon.pathData.trim() === '') {
    failures.push(`Icon '${icon.iconId}' has no path data.`);
    continue;
  }

  // Path data must start with a moveto; extraction re-anchors every kept subpath, so anything
  // else means a subpath lost its opening command and would render from the wrong origin.
  if (!/^M/.test(icon.pathData)) {
    failures.push(`Icon '${icon.iconId}' does not begin with an absolute moveto.`);
  }

  if (!/^0 0 \d+ \d+$/.test(icon.viewBox)) {
    failures.push(`Icon '${icon.iconId}' has an unexpected viewBox '${icon.viewBox}'.`);
  }
}

for (const assignment of manifest.assignments) {
  if (!byId.has(assignment.iconId)) {
    failures.push(`Assignment ${JSON.stringify(assignment)} references an unknown icon.`);
  }
}

// Every icon must be reachable: an orphan means an assignment was removed and the glyph left
// behind, which is how a manifest silently grows stale.
const referenced = new Set(manifest.assignments.map((assignment) => assignment.iconId));
for (const icon of manifest.icons) {
  if (!referenced.has(icon.iconId)) failures.push(`Icon '${icon.iconId}' is not referenced by any assignment.`);
}

if (!manifest.source?.license) failures.push('The manifest is missing its licence attribution.');

if (failures.length > 0) {
  console.error('\nIcon manifest verification failed:');
  for (const failure of failures) console.error(`  - ${failure}`);
  console.error('');
  process.exit(1);
}

console.log(
  `icons ok  ${manifest.icons.length} icons, ${manifest.assignments.length} assignments, `
  + `${manifest.source.license} (${manifest.source.package}@${manifest.source.version})`,
);
