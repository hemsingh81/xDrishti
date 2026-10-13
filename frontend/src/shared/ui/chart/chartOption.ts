import { alignedValues } from './chartData';
import type { ChartTokens } from './chartTheme';
import type { ChartOption, ChartSeries } from './types';

interface OptionInput {
  /** The lines to draw (the legend has already taken out the hidden ones). */
  series: readonly ChartSeries[];
  /** Every date, in order. */
  dates: readonly string[];
  tokens: ChartTokens;
  axisFormatter: (value: number) => string;
  axisDateLabel: (date: string) => string;
  /** False when the visitor asks for reduced motion. */
  animate: boolean;
}

/**
 * The ECharts option for a set of lines, from theme tokens only. A pure function (no library import), so the colours,
 * the motion switch and the axis are testable without a canvas.
 */
export function buildChartOption(input: OptionInput): ChartOption {
  const { series, dates, tokens, axisFormatter, axisDateLabel, animate } = input;
  const values = new Map(series.map((s) => [s.id, alignedValues(s, dates)]));

  return {
    animation: animate,
    backgroundColor: 'transparent',
    textStyle: { fontFamily: tokens.fontFamily },
    grid: { left: 8, right: 16, top: 16, bottom: 8, containLabel: true },
    xAxis: {
      type: 'category',
      data: dates,
      boundaryGap: false,
      axisLine: { lineStyle: { color: tokens.border } },
      axisTick: { show: false },
      axisLabel: { color: tokens.textSecondary, hideOverlap: true, formatter: (date: string) => axisDateLabel(date) },
      // The line that follows the pointer; the React tooltip (ChartTooltip) shows the figures.
      axisPointer: {
        show: true,
        type: 'line',
        snap: true,
        triggerOn: 'mousemove|click',
        label: { show: false },
        lineStyle: { color: tokens.textSecondary, type: 'dashed' },
      },
    },
    yAxis: {
      type: 'value',
      // scale: the axis fits the data instead of starting at 0, so a month of small moves is not drawn flat.
      scale: true,
      axisLabel: { color: tokens.textSecondary, formatter: (value: number) => axisFormatter(value) },
      splitLine: { lineStyle: { color: tokens.grid } },
    },
    // filterMode 'none': a zoomed line keeps its points outside the window, so it does not redraw from cut data.
    // Wheel zoom needs Ctrl (⌘ and pinch send it too), so scrolling the page over the chart still scrolls the page.
    dataZoom: [{ type: 'inside', filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseWheel: false }],
    series: series.map((s) => ({
      id: s.id,
      name: s.name,
      type: 'line',
      data: values.get(s.id),
      showSymbol: dates.length < 2,
      symbolSize: 6,
      connectNulls: false,
      lineStyle: { width: 2, color: tokens.colors[s.color], type: s.dash ?? 'solid' },
      itemStyle: { color: tokens.colors[s.color] },
      emphasis: { disabled: true },
    })),
  };
}
