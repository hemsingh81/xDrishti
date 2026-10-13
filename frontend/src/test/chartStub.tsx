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
