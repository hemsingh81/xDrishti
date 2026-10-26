// ESLint flat config. Rules here are the enforced part of frontend/CLAUDE.md.
import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import tseslint from 'typescript-eslint';

// The chart library is imported by exactly two modules (shared/ui/chart/EChartsCanvas.tsx for the line chart and
// shared/ui/chart/CategoryChartCanvas.tsx for the donut and the bars), which their wrappers load with a dynamic import:
// anything else importing it would pull it into another chunk (ADR 0020 §5, ADR 0021 §5).
const featureIndexOnly = {
  group: ['@/features/*/*'],
  message: 'Import a feature only through its public index: @/features/<name>.',
};
const sharedIsLeaf = {
  group: ['@/features/*', '@/app/*'],
  message: 'shared/ and theme/ must not depend on features or app.',
};
const chartLibrary = {
  group: ['echarts', 'echarts/*', 'zrender', 'zrender/*', 'lightweight-charts', 'lightweight-charts/*'],
  message:
    'Use <TimeSeriesChart>, <CategoryChart> or <PriceChart> from @/shared/ui/chart; only shared/ui/chart/EChartsCanvas.tsx, CategoryChartCanvas.tsx and PriceChartCanvas.tsx import a chart library.',
};

export default defineConfig([
  globalIgnores(['dist', 'coverage', 'test-results', 'playwright-report', 'src/shared/api/schema.d.ts']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.strictTypeChecked,
      tseslint.configs.stylisticTypeChecked,
      reactHooks.configs.flat['recommended-latest'],
      reactRefresh.configs.vite,
      jsxA11y.flatConfigs.recommended,
    ],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    rules: {
      '@typescript-eslint/consistent-type-imports': ['error', { fixStyle: 'inline-type-imports' }],
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/restrict-template-expressions': ['error', { allowNumber: true }],
      eqeqeq: ['error', 'always'],
      'no-console': ['error', { allow: ['warn', 'error'] }],
      'max-lines': ['warn', { max: 250, skipBlankLines: true, skipComments: true }],
      // Module boundaries: a feature is used only through its public index; shared code never imports features.
      'no-restricted-imports': [
        'error',
        {
          // Inside a feature use relative imports; from outside, only the feature's public index.
          patterns: [featureIndexOnly, chartLibrary],
        },
      ],
    },
  },
  {
    files: ['src/shared/**/*.{ts,tsx}', 'src/theme/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [featureIndexOnly, sharedIsLeaf, chartLibrary],
        },
      ],
    },
  },
  {
    // The three modules allowed to import a chart library.
    files: [
      'src/shared/ui/chart/EChartsCanvas.tsx',
      'src/shared/ui/chart/CategoryChartCanvas.tsx',
      'src/shared/ui/chart/PriceChartCanvas.tsx',
    ],
    rules: {
      'no-restricted-imports': ['error', { patterns: [featureIndexOnly, sharedIsLeaf] }],
    },
  },
  {
    files: ['**/*.test.{ts,tsx}', 'src/test/**'],
    rules: {
      'max-lines': 'off',
      '@typescript-eslint/no-non-null-assertion': 'off',
      // Test helpers export builders and components side by side; there is no fast refresh in tests.
      'react-refresh/only-export-components': 'off',
    },
  },
  {
    files: ['*.config.{js,ts}'],
    extends: [tseslint.configs.disableTypeChecked],
    languageOptions: { globals: globals.node },
  },
  prettier,
]);
