import { build } from 'esbuild';
import process from 'node:process';
import { rm } from 'node:fs/promises';

await rm('dist', { recursive: true, force: true });
await build({
  entryPoints: ['src/editor.ts'],
  outfile: 'dist/feelsharp-editor.js',
  bundle: true,
  format: 'esm',
  target: 'es2022',
  sourcemap: !process.argv.includes('--build-mode=Release'),
  minify: process.argv.includes('--build-mode=Release'),
  logLevel: 'info',
});
