import { shareText, shortName, sliceTotal } from './categoryData';
import type { ChartTokens } from './chartTheme';
import type { CategoryData } from './categoryTypes';
import type { ChartOption } from './types';

interface OptionInput {
  /** What to draw (the legend has already taken out what is switched off). */
  data: CategoryData;
  tokens: ChartTokens;
  axisFormatter: (value: number) => string;
  /** Names and shares on the slices; off on a phone, where the legend carries them. */
  showLabels: boolean;
  /** False when the visitor asks for reduced motion. */
  animate: boolean;
}

const colourOf = (tokens: ChartTokens, slot: number): string =>
  tokens.categories[Math.min(slot, tokens.categories.length - 1)] ?? tokens.textSecondary;

function donut(data: Extract<CategoryData, { kind: 'donut' }>, input: OptionInput): ChartOption {
  const { tokens, showLabels, animate } = input;
  const total = sliceTotal(data.slices);
  return {
    animation: animate,
    backgroundColor: 'transparent',
    textStyle: { fontFamily: tokens.fontFamily },
    series: [
      {
        type: 'pie',
        radius: showLabels ? ['44%', '66%'] : ['52%', '84%'],
        center: ['50%', '50%'],
        avoidLabelOverlap: true,
        // The card colour between slices keeps neighbours apart without relying on the hue.
        itemStyle: { borderColor: tokens.surface, borderWidth: 2 },
        label: { show: showLabels, color: tokens.text, fontFamily: tokens.fontFamily, lineHeight: 15 },
        labelLine: { show: showLabels, lineStyle: { color: tokens.textSecondary } },
        emphasis: { scale: animate, label: { show: showLabels } },
        data: data.slices.map((s) => ({
          id: s.id,
          name: s.name,
          value: s.value,
          itemStyle: { color: colourOf(tokens, s.color) },
          label: { formatter: `${shortName(s.name)}\n${shareText(s.value, total)}` },
        })),
      },
    ],
  };
}

function stackedBar(data: Extract<CategoryData, { kind: 'stacked' }>, input: OptionInput): ChartOption {
  const { tokens, axisFormatter, animate } = input;
  return {
    animation: animate,
    backgroundColor: 'transparent',
    textStyle: { fontFamily: tokens.fontFamily },
    grid: { left: 8, right: 24, top: 8, bottom: 8, containLabel: true },
    // Horizontal bars: sector and account names are long. `inverse` puts the first (largest) category on top.
    yAxis: {
      type: 'category',
      inverse: true,
      data: data.categories.map((c) => c.name),
      axisLine: { lineStyle: { color: tokens.border } },
      axisTick: { show: false },
      axisLabel: { color: tokens.text, width: 120, overflow: 'truncate', interval: 0 },
    },
    xAxis: {
      type: 'value',
      axisLabel: { color: tokens.textSecondary, hideOverlap: true, formatter: (value: number) => axisFormatter(value) },
      splitLine: { lineStyle: { color: tokens.grid } },
    },
    series: data.stacks.map((s) => ({
      id: s.id,
      name: s.name,
      type: 'bar',
      stack: 'total',
      data: s.values,
      barMaxWidth: 28,
      itemStyle: { color: colourOf(tokens, s.color), borderColor: tokens.surface, borderWidth: 1 },
      emphasis: { disabled: true },
    })),
  };
}

/**
 * The ECharts option of a donut or a stacked bar, from theme tokens only. A pure function (no library import), so the
 * colours, the labels and the motion switch are testable without a canvas.
 */
export function buildCategoryOption(input: OptionInput): ChartOption {
  return input.data.kind === 'donut' ? donut(input.data, input) : stackedBar(input.data, input);
}
