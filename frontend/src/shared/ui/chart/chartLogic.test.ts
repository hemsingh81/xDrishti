import { createAppTheme } from '@/theme/theme';
import { palettes } from '@/theme/tokens';
import { alignedValues, axisDateLabel, chartDates } from './chartData';
import { buildChartOption } from './chartOption';
import { chartSummary } from './chartSummary';
import { chartTokens } from './chartTheme';
import type { ChartSeries } from './types';

const series: ChartSeries[] = [
  {
    id: 'value',
    name: 'Value',
    color: 'accent',
    data: [
      ['2026-10-05', 100],
      ['2026-10-06', 120],
      ['2026-10-07', 90],
    ],
  },
  {
    id: 'bench',
    name: 'Index <b>&</b>',
    color: 'warn',
    dash: 'dotted',
    data: [
      ['2026-10-06', 110],
      ['2026-10-07', null],
    ],
  },
];
const money = (v: number) => `₹${String(v)}`;
const day = (d: string) => `day ${d}`;

const optionFor = (mode: 'light' | 'dark', animate = true) =>
  buildChartOption({
    series,
    dates: chartDates(series),
    tokens: chartTokens(createAppTheme(mode)),
    axisFormatter: money,
    axisDateLabel: axisDateLabel(chartDates(series)),
    animate,
  });

describe('chart data helpers', () => {
  it('joins the dates of all series, once, in order', () => {
    expect(chartDates(series)).toEqual(['2026-10-05', '2026-10-06', '2026-10-07']);
  });

  it('labels the axis with day and month up to about a year, month and year beyond', () => {
    expect(axisDateLabel(['2026-01-05', '2026-10-13'])('2026-10-13')).toBe('13 Oct');
    expect(axisDateLabel(['2024-01-05', '2026-10-13'])('2026-10-13')).toBe('Oct 2026');
  });

  it('lines a series up with the shared dates, null where it has no value', () => {
    expect(alignedValues(series[1]!, chartDates(series))).toEqual([null, 110, null]);
  });
});

describe('chart summary', () => {
  it('says the range and, per line, first and last value with their dates, lowest and highest', () => {
    const text = chartSummary({ label: 'Portfolio value history', series, valueFormatter: money, dateFormatter: day });
    expect(text).toContain('Portfolio value history, 3 dates from day 2026-10-05 to day 2026-10-07.');
    expect(text).toContain('Value: ₹100 on day 2026-10-05, ₹90 on day 2026-10-07, lowest ₹90, highest ₹120.');
    // A trailing gap does not count as the last value.
    expect(text).toContain(
      'Index <b>&</b>: ₹110 on day 2026-10-06, ₹110 on day 2026-10-06, lowest ₹110, highest ₹110.',
    );
  });

  it('says so when there is nothing to describe', () => {
    expect(chartSummary({ label: 'Chart', series: [], valueFormatter: money, dateFormatter: day })).toBe(
      'Chart: no data.',
    );
  });
});

describe.each(['light', 'dark'] as const)('chart option (%s)', (mode) => {
  it('colours the lines, axes and pointer from the theme tokens of the mode', () => {
    const p = palettes[mode];
    const option = optionFor(mode) as {
      series: { lineStyle: { color: string; type: string } }[];
      xAxis: {
        axisLabel: { color: string };
        axisLine: { lineStyle: { color: string } };
        axisPointer: { lineStyle: { color: string } };
      };
      yAxis: { splitLine: { lineStyle: { color: string } } };
    };
    expect(option.series.map((s) => s.lineStyle.color)).toEqual([p.accent, p.warn]);
    expect(option.series.map((s) => s.lineStyle.type)).toEqual(['solid', 'dotted']);
    expect(option.xAxis.axisLine.lineStyle.color).toBe(p.border);
    expect(option.xAxis.axisPointer.lineStyle.color).toBe(p.textSecondary);
    expect(option.xAxis.axisLabel.color).toBe(p.textSecondary);
    expect(option.yAxis.splitLine.lineStyle.color).toBe(p.border);
  });
});

describe('chart option', () => {
  it('switches the animation off for a visitor who asks for less motion', () => {
    expect(optionFor('light', true)).toMatchObject({ animation: true });
    expect(optionFor('light', false)).toMatchObject({ animation: false });
  });

  it('zooms inside, with Ctrl for the wheel so the page still scrolls', () => {
    expect(optionFor('light')).toMatchObject({ dataZoom: [{ type: 'inside', zoomOnMouseWheel: 'ctrl' }] });
  });

  it('draws the pointer line that follows the cursor and snaps to a date, without its own tooltip or label', () => {
    const option = optionFor('light') as Record<string, unknown> & {
      xAxis: { axisPointer: Record<string, unknown> };
    };
    expect(option.xAxis.axisPointer).toMatchObject({ show: true, snap: true, label: { show: false } });
    expect(option).not.toHaveProperty('tooltip');
  });

  it('aligns every line to the shared dates, leaving a gap where a line has no value', () => {
    const lines = (optionFor('light') as { series: { id: string; data: (number | null)[] }[] }).series;
    expect(lines.find((s) => s.id === 'bench')?.data).toEqual([null, 110, null]);
  });
});
