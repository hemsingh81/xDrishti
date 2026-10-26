/**
 * The chart library is imported by two modules only, each loaded lazily by its own wrapper: ADR 0020 §5 and ADR 0021 §5
 * (own chunks, not in the first page load). ESLint enforces the first; this reads the sources to keep both true.
 */
const sources = import.meta.glob<string>('/src/**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true });
const production = Object.entries(sources).filter(([path]) => !/\.test\.tsx?$|\/src\/test\//.test(path));

const importsLibrary = /(?:from|import\()\s*['"](?:echarts|zrender)(?:\/[^'"]*)?['"]/;

const lineCanvas = '/src/shared/ui/chart/EChartsCanvas.tsx';
const categoryCanvas = '/src/shared/ui/chart/CategoryChartCanvas.tsx';

/** The production modules that mention a canvas module by name, and whether they import it statically. */
const referrers = (canvas: string) => {
  const name = canvas.split('/').pop()?.replace('.tsx', '') ?? '';
  return production
    .filter(([path, text]) => ![lineCanvas, categoryCanvas].includes(path) && text.includes(name))
    .map(([path, text]) => [path, new RegExp(`from\\s+['"][^'"]*${name}['"]`).test(text)] as const);
};

describe('chart library boundary', () => {
  it('is imported by the two chart canvases and by nothing else', () => {
    const importers = production.filter(([, text]) => importsLibrary.test(text)).map(([path]) => path);
    expect(importers.sort()).toEqual([categoryCanvas, lineCanvas]);
  });

  it.each([lineCanvas, categoryCanvas])(
    'imports the library by feature (echarts/core, charts, components, renderers) in %s, never the whole package',
    (canvas) => {
      const text = sources[canvas] ?? '';
      const specifiers = [...text.matchAll(/from\s+'(echarts[^']*)'/g)].map((m) => m[1]);
      expect(specifiers.sort()).toEqual(['echarts/charts', 'echarts/components', 'echarts/core', 'echarts/renderers']);
    },
  );

  it('registers only the charts each canvas draws (the pie and the bar are not in the line chart, nor the reverse)', () => {
    const registered = (canvas: string) => /register\(\[([^\]]*)\]\)/.exec(sources[canvas] ?? '')?.[1]?.trim();
    expect(registered(lineCanvas)).toBe(
      'LineChart, GridComponent, AxisPointerComponent, DataZoomInsideComponent, CanvasRenderer',
    );
    expect(registered(categoryCanvas)).toBe('PieChart, BarChart, GridComponent, CanvasRenderer');
  });

  it('reaches each canvas only through a dynamic import in its own wrapper', () => {
    expect(referrers(lineCanvas)).toEqual([['/src/shared/ui/chart/TimeSeriesChart.tsx', false]]);
    expect(referrers(categoryCanvas)).toEqual([['/src/shared/ui/chart/CategoryChart.tsx', false]]);
  });
});

describe('price chart library boundary', () => {
  const priceCanvas = '/src/shared/ui/chart/PriceChartCanvas.tsx';
  const importsPriceLibrary = /(?:from|import\()\s*['"]lightweight-charts(?:\/[^'"]*)?['"]/;

  it('is imported by PriceChartCanvas and by nothing else', () => {
    expect(production.filter(([, text]) => importsPriceLibrary.test(text)).map(([path]) => path)).toEqual([
      priceCanvas,
    ]);
  });

  it('reaches the canvas only through a dynamic import in PriceChart', () => {
    const name = 'PriceChartCanvas';
    const found = production
      .filter(([path, text]) => path !== priceCanvas && text.includes(name))
      .map(([path, text]) => [path, new RegExp(`from\\s+['"][^'"]*${name}['"]`).test(text)] as const);
    expect(found).toEqual([['/src/shared/ui/chart/PriceChart.tsx', false]]);
  });
});
