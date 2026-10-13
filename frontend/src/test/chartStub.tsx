import type { CategoryChartCanvasProps } from '@/shared/ui/chart/CategoryChartCanvas';
import type { EChartsCanvasProps } from '@/shared/ui/chart/EChartsCanvas';

/**
 * Stand-in for the lazy ECharts module (`shared/ui/chart/EChartsCanvas`, replaced for every test in setup.ts): jsdom has
 * no canvas. It records the props each render received, so tests assert what would be drawn, not how.
 */
export const chartStub = {
  calls: [] as EChartsCanvasProps[],
  /** Make the "chunk" fail to render, as a failed library load would. */
  fail: false,
  reset() {
    chartStub.calls = [];
    chartStub.fail = false;
  },
  get last(): EChartsCanvasProps | undefined {
    return chartStub.calls[chartStub.calls.length - 1];
  },
};

/** The series the last render drew: id, name, line colour and style, and values. */
export function drawnSeries() {
  const series = chartStub.last?.option.series;
  return (Array.isArray(series) ? series : []) as {
    id: string;
    name: string;
    data: (number | null)[];
    lineStyle: { color: string; type: string };
  }[];
}

export default function EChartsCanvasStub(props: EChartsCanvasProps) {
  if (chartStub.fail) throw new Error('The chart library could not be loaded.');
  chartStub.calls.push(props);
  return <div data-testid="chart-canvas" />;
}

/** The same for the donut and stacked-bar canvas (`shared/ui/chart/CategoryChartCanvas`): what each render was asked to draw. */
export const categoryStub = {
  calls: [] as CategoryChartCanvasProps[],
  fail: false,
  reset() {
    categoryStub.calls = [];
    categoryStub.fail = false;
  },
  get last(): CategoryChartCanvasProps | undefined {
    return categoryStub.calls[categoryStub.calls.length - 1];
  },
};

/** The first series the last category render drew (the donut's one pie, or the first stack of a bar). */
export function drawnCategorySeries() {
  const series = categoryStub.last?.option.series;
  return (Array.isArray(series) ? series : []) as {
    id?: string;
    name?: string;
    type: string;
    data: (
      { id: string; name: string; value: number; itemStyle: { color: string }; label?: { formatter: string } } | number
    )[];
    itemStyle?: { color: string };
  }[];
}

export function CategoryChartCanvasStub(props: CategoryChartCanvasProps) {
  if (categoryStub.fail) throw new Error('The chart library could not be loaded.');
  categoryStub.calls.push(props);
  return <div data-testid="category-canvas" />;
}
