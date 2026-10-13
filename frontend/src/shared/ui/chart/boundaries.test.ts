/**
 * The chart library is imported by one module only, and that module is loaded lazily: ADR 0020 §5 (own chunk, not in
 * the first page load). ESLint enforces the first; this reads the sources to keep both true.
 */
const sources = import.meta.glob<string>('/src/**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true });
const production = Object.entries(sources).filter(([path]) => !/\.test\.tsx?$|\/src\/test\//.test(path));

const importsLibrary = /(?:from|import\()\s*['"](?:echarts|zrender)(?:\/[^'"]*)?['"]/;

describe('chart library boundary', () => {
  it('is imported by shared/ui/chart/EChartsCanvas.tsx and by nothing else', () => {
    const importers = production.filter(([, text]) => importsLibrary.test(text)).map(([path]) => path);
    expect(importers).toEqual(['/src/shared/ui/chart/EChartsCanvas.tsx']);
  });

  it('imports the library by feature (echarts/core, charts, components, renderers), never the whole package', () => {
    const text = sources['/src/shared/ui/chart/EChartsCanvas.tsx'] ?? '';
    const specifiers = [...text.matchAll(/from\s+'(echarts[^']*)'/g)].map((m) => m[1]);
    expect(specifiers.sort()).toEqual(['echarts/charts', 'echarts/components', 'echarts/core', 'echarts/renderers']);
  });

  it('is reached only through a dynamic import in the wrapper', () => {
    const referrers = production
      .filter(([path, text]) => path !== '/src/shared/ui/chart/EChartsCanvas.tsx' && text.includes('EChartsCanvas'))
      .map(([path, text]) => [path, /from\s+['"][^'"]*EChartsCanvas['"]/.test(text)] as const);
    expect(referrers).toEqual([['/src/shared/ui/chart/TimeSeriesChart.tsx', false]]);
  });
});
