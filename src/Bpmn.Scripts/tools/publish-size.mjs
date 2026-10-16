// Sizes of a published Blazor WebAssembly app, as a first visit downloads it: raw, gzip and Brotli, by kind and by culture.
//
//   dotnet publish playground/Bpmn.Demo -c Release -o artifacts/publish-demo
//   node tools/publish-size.mjs artifacts/publish-demo/wwwroot [--top=15]
//
// The runtime (dotnet.native.wasm), ICU data and the satellite assemblies of the 13 UI languages are what a consumer controls with
// InvariantGlobalization, BlazorWebAssemblyLoadAllGlobalizationData and SatelliteResourceLanguages; this shows what they cost.
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { brotliCompressSync, brotliDecompressSync, gunzipSync, gzipSync } from 'node:zlib';

const args = process.argv.slice(2);
const root = args.find((a) => !a.startsWith('--'));
const top = Number(args.find((a) => a.startsWith('--top='))?.split('=')[1] ?? 15);
if (!root) {
  console.error('usage: node tools/publish-size.mjs <publish wwwroot> [--top=15]');
  process.exit(2);
}

async function* walk(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(full);
    else yield full;
  }
}

const kindOf = (file) => {
  const name = path.basename(file);
  if (/\.resources\.dll|\.resources\.[a-z0-9]+\.(dll|wasm)/i.test(name) || /[\\/](cs|de|es|fr|it|ja|ko|pl|pt-BR|ru|tr|zh-Hans|zh-Hant)[\\/]/.test(file)) return 'satellite assemblies';
  if (/icudt.*\.dat/i.test(name) || /icu.*\.(dat|blat)/i.test(name)) return 'ICU data';
  if (/dotnet\.native.*\.wasm$/i.test(name)) return 'runtime (wasm)';
  if (/\.(dll|wasm)$/i.test(name)) return 'assemblies';
  if (/\.m?js$/i.test(name)) return 'js';
  if (/\.css$/i.test(name)) return 'css';
  return 'other';
};

const rows = [];
for await (const file of walk(root)) {
  if (/\.(gz|br|map)$/i.test(file)) continue;
  const raw = await readFile(file);
  rows.push({
    file: path.relative(root, file).replaceAll('\\', '/'),
    kind: kindOf(file),
    raw: raw.length,
    gzip: gzipSync(raw, { level: 9 }).length,
    brotli: brotliCompressSync(raw).length,
  });
}

const kb = (n) => (n / 1024).toFixed(0).padStart(8);
const byKind = new Map();
for (const row of rows) {
  const t = byKind.get(row.kind) ?? { files: 0, raw: 0, gzip: 0, brotli: 0 };
  t.files += 1; t.raw += row.raw; t.gzip += row.gzip; t.brotli += row.brotli;
  byKind.set(row.kind, t);
}

console.log(`${'kind'.padEnd(22)}${'files'.padStart(7)}${'raw KB'.padStart(9)}${'gzip KB'.padStart(9)}${'brotli KB'.padStart(11)}`);
let total = { files: 0, raw: 0, gzip: 0, brotli: 0 };
for (const [kind, t] of [...byKind].sort((a, b) => b[1].brotli - a[1].brotli)) {
  console.log(`${kind.padEnd(22)}${String(t.files).padStart(7)}${kb(t.raw)}${kb(t.gzip)}${kb(t.brotli).padStart(10)}`);
  for (const key of Object.keys(total)) total[key] += t[key];
}
console.log(`${'total'.padEnd(22)}${String(total.files).padStart(7)}${kb(total.raw)}${kb(total.gzip)}${kb(total.brotli).padStart(10)}\n`);

console.log(`largest ${top} files (by Brotli):`);
for (const row of rows.sort((a, b) => b.brotli - a.brotli).slice(0, top)) {
  console.log(`${kb(row.brotli)} KB br  ${kb(row.raw)} KB raw  ${row.file}`);
}
// Every precompressed file must decode to the file it sits beside. A Release publish once shipped 258-byte .br files of the wrong
// source next to the editor's script, and the browser refused the module; this is the check that would have caught it.
const bad = [];
let checked = 0;
for await (const file of walk(root)) {
  const decoder = file.endsWith('.br') ? brotliDecompressSync : file.endsWith('.gz') ? gunzipSync : null;
  if (decoder === null) continue;
  const raw = file.slice(0, -3);
  let original;
  try {
    original = await readFile(raw);
  } catch {
    continue;
  }
  checked += 1;
  let decoded = null;
  try {
    decoded = decoder(await readFile(file));
  } catch {
    // an undecodable file is a mismatch too
  }
  if (decoded === null || !decoded.equals(original)) bad.push(path.relative(root, file).split(path.sep).join('/'));
}
console.log(`
precompressed files checked against their originals: ${checked}, mismatched: ${bad.length}`);
for (const file of bad) console.error(`  MISMATCH ${file}`);
if (bad.length > 0) process.exitCode = 1;

const precompressed = [];
for await (const file of walk(root)) if (/\.(gz|br)$/i.test(file)) precompressed.push(file);
console.log(`\nprecompressed files present: ${precompressed.length} ${precompressed.length ? '(host serves them)' : '(none: enable compression on the host)'}`);
