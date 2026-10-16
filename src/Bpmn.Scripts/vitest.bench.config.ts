import { defineConfig } from 'vitest/config';

import base from './vitest.config.js';

/** Runs the model-level micro-benchmarks in test/bench (`npm run bench`); kept out of `npm test`. */
export default defineConfig({
  ...base,
  test: {
    ...base.test,
    include: ['test/bench/**/*.perf.ts'],
    testTimeout: 120_000,
  },
});
