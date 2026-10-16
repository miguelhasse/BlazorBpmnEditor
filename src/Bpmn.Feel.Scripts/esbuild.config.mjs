import * as esbuild from 'esbuild';
import { readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

const rawBuildMode = process.argv.find(arg => arg.startsWith('--build-mode='))?.split('=')[1];
const isDebug = rawBuildMode !== 'Release';

const result = await esbuild.build({
  entryPoints: ['src/index.ts'],
  bundle: true,
  minify: !isDebug,
  sourcemap: isDebug,
  target: 'es2022',
  format: 'esm',
  outfile: 'dist/feel-editor.js',
  loader: { '.css': 'text' },
  legalComments: 'inline',
  metafile: true,
  logLevel: 'info',
});

const packages = new Set(Object.keys(result.metafile.inputs)
  .map(input => /node_modules\/((?:@[^/]+\/)?[^/]+)/u.exec(input)?.[1])
  .filter(Boolean));
// semver-compat bundles semver internally; include its license too.
packages.add('semver');
const notices = ['Third-party software bundled in Hasseware.AspNetCore.Components.Bpmn.Feel', ''];
for (const name of [...packages].sort()) {
  const directory = path.join('node_modules', name);
  const pkg = JSON.parse(await readFile(path.join(directory, 'package.json'), 'utf8'));
  const licenses = (await readdir(directory)).filter(file => /^licen[cs]e(?:[.-]|$)/iu.test(file));
  if (!licenses.length && name !== '@bpmn-io/semver-compat') throw new Error(`Missing license for bundled package ${name}`);
  notices.push(`${name} ${pkg.version} (${pkg.license})`, String(pkg.repository?.url ?? ''), '');
  if (name === '@bpmn-io/semver-compat') notices.push(await readFile('licenses/semver-compat-MIT.txt', 'utf8'), '');
  for (const file of licenses) notices.push(await readFile(path.join(directory, file), 'utf8'), '');
}
await writeFile('../Bpmn.Feel/THIRD-PARTY-NOTICES.txt', notices.join('\n'), 'utf8');
