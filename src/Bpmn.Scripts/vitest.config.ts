import { defineConfig } from 'vitest/config';

export default defineConfig({
  server: {
    fs: { allow: ['.', '../Bpmn/Resources'] },
  },
  test: {
    // Default to Node: every unit under test is a pure function or a JointJS model
    // operation that does not need a DOM. Suites that genuinely need one opt in with
    // a `// @vitest-environment jsdom` docblock so the slow environment stays rare.
    environment: 'node',
    include: ['test/**/*.test.ts'],
    globals: false,
    // @joint/core ships ESM source with circular imports between Vectorizer and the DOM
    // helpers. Vite's per-module SSR transform evaluates those in an order that leaves
    // V.prototype half-initialised, so dia.Paper fails to construct. Pre-bundling the package
    // with esbuild reproduces exactly what the production build does and resolves the cycle.
    deps: {
      optimizer: {
        ssr: {
          enabled: true,
          include: ['@joint/core'],
        },
      },
    },
    coverage: {
      provider: 'v8',
      include: ['src/**/*.ts'],
      exclude: ['src/BuildConstants.ts'],
    },
  },
});
