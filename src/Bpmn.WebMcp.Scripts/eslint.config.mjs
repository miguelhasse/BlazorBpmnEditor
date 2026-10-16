// @ts-check
import js from '@eslint/js';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: ['dist/**', 'node_modules/**'],
  },
  js.configs.recommended,
  ...tseslint.configs.strictTypeChecked,
  ...tseslint.configs.stylisticTypeChecked,
  {
    languageOptions: {
      parserOptions: {
        // tsconfig.json covers src and test; only the root config files sit outside it.
        projectService: {
          allowDefaultProject: ['*.config.mjs', '*.config.ts'],
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
      '@typescript-eslint/no-unnecessary-condition': 'error',
      '@typescript-eslint/no-non-null-assertion': 'error',
    },
  },
  {
    // The module's exports are what the .NET side calls: never let their shape be inferred.
    files: ['src/**/*.ts'],
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
    files: ['*.config.mjs', '*.config.ts'],
    languageOptions: {
      globals: { ...globals.node },
    },
    extends: [tseslint.configs.disableTypeChecked],
  },
);
