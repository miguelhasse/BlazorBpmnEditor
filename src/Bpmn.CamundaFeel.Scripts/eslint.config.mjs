// @ts-check
import js from '@eslint/js';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: ['dist/**', 'node_modules/**', 'src/BuildConstants.ts'],
  },
  js.configs.recommended,
  ...tseslint.configs.strictTypeChecked,
  ...tseslint.configs.stylisticTypeChecked,
  {
    languageOptions: {
      parserOptions: {
        // tsconfig.json covers src and test; only the root config files sit outside it.
        projectService: {
          allowDefaultProject: ['*.config.mjs', '*.config.ts', 'scripts/*.mjs', 'tools/*.mjs'],
          defaultProject: 'tsconfig.json',
        },
        tsconfigRootDir: import.meta.dirname,
      },
      globals: { ...globals.browser },
    },
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_' },
      ],
      '@typescript-eslint/consistent-type-imports': ['error', { fixStyle: 'separate-type-imports' }],
      '@typescript-eslint/switch-exhaustiveness-check': [
        'error',
        { considerDefaultExhaustiveForUnions: false },
      ],
      '@typescript-eslint/no-unnecessary-condition': 'error',
      '@typescript-eslint/no-non-null-assertion': 'error',

      // JointJS+ is commercially licensed and explicitly out of scope
      // (implementation plan §3 / §7 Q3). Enforce it mechanically, not by review.
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@joint/plus', '@joint/plus/*', '**/joint.plus*', '@joint/*-plus'],
              message:
                'JointJS+ is out of scope — no commercial licence is available. Compose open-source @joint/core primitives instead.',
            },
          ],
        },
      ],
    },
  },
  {
    // The public interop surface is a versioned contract: never let its shape be inferred.
    files: ['src/interop/**/*.ts', 'src/index.ts'],
    rules: {
      '@typescript-eslint/explicit-module-boundary-types': 'error',
    },
  },
  {
    files: ['test/**/*.ts'],
    languageOptions: {
      globals: { ...globals.node },
    },
    rules: {
      '@typescript-eslint/no-non-null-assertion': 'off',
    },
  },
  {
    files: ['*.config.mjs', 'scripts/**/*.mjs', 'tools/**/*.mjs'],
    languageOptions: {
      globals: { ...globals.node },
    },
    extends: [tseslint.configs.disableTypeChecked],
  },
);
