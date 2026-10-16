import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // The module only touches globalThis.document / navigator, which the tests stub by hand.
    environment: 'node',
    include: ['test/**/*.test.ts'],
    globals: false,
  },
});
