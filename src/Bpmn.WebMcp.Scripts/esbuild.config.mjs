import * as esbuild from 'esbuild';
import process from 'node:process';

const rawBuildMode = process.argv.find(arg => arg.startsWith('--build-mode='))?.split('=')[1];
const isDebug = rawBuildMode !== 'Release';

await esbuild.build({
  entryPoints: ['src/webmcp.ts'],
  bundle: true,
  minify: !isDebug,
  sourcemap: isDebug,
  target: 'es2022',
  format: 'esm',
  outfile: 'dist/webmcp.js',
  logLevel: 'info',
});
