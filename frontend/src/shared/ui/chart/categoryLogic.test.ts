import { createAppTheme } from '@/theme/theme';
import { categorical } from '@/theme/tokens';
import { categorySummary } from './categorySummary';
import { buildCategoryOption } from './categoryOption';
import { shareOf, shareText, shortName, visibleData } from './categoryData';
import { chartTokens } from './chartTheme';
import type { CategoryData } from './categoryTypes';

const money = (v: number) => `₹${String(v)}`;

describe('category data helpers', () => {
  it('computes a share, and 0 when there is no total', () => {
    expect(shareOf(25, 200)).toBe(12.5);
    expect(shareOf(5, 0)).toBe(0);
    expect(shareText(1, 3)).toBe('33.3%');
  });

  it('cuts a long name for a label on the canvas only', () => {
    expect(shortName('Oil Gas & Consumable Fuels')).toBe('Oil Gas & Consuma…');
    expect(shortName('IT')).toBe('IT');
  });

  it('takes what the legend switched off out of the data', () => {
    const data: CategoryData = {
      kind: 'donut',
      slices: [
        { id: 'a', name: 'A', value: 1, color: 0 },
        { id: 'b', name: 'B', value: 2, color: 1 },
      ],
    };
    expect(visibleData(data, new Set(['a']))).toEqual({ kind: 'donut', slices: [data.slices[1]] });
  });
});

describe('category summary', () => {
  it('says there is no data for an empty chart', () => {
    expect(categorySummary({ label: 'Mix', data: { kind: 'donut', slices: [] }, valueFormatter: money })).toBe(
      'Mix: no data.',
    );
    expect(
      categorySummary({
        label: 'Split',
        data: { kind: 'stacked', categories: [], stacks: [] },
        valueFormatter: money,
      }),
    ).toBe('Split: no data.');
  });

  it('uses the singular for one slice', () => {
    const text = categorySummary({
      label: 'Mix',
      data: { kind: 'donut', slices: [{ id: 'a', name: 'A', value: 5, color: 0 }] },
      valueFormatter: money,
    });
    expect(text).toBe('Mix, 1 slice, total ₹5: A 100.0% (₹5).');
  });
});

describe('category option', () => {
  const tokens = (mode: 'light' | 'dark') => chartTokens(createAppTheme(mode));
  const donut: CategoryData = {
    kind: 'donut',
    slices: [{ id: 'a', name: '<b>Banking & Finance</b>', value: 3, color: 0 }],
  };

  it('exposes the categorical palette of the mode through the chart tokens', () => {
    expect(tokens('dark').categories).toEqual(categorical.dark);
    expect(tokens('light').categories).toEqual(categorical.light);
  });

  it('separates slices with the card colour, so neighbours are told apart without the hue', () => {
    const option = buildCategoryOption({
      data: donut,
      tokens: tokens('dark'),
      axisFormatter: money,
      showLabels: true,
      animate: true,
    });
    const [pie] = option.series as { itemStyle: { borderColor: string } }[];
    expect(pie?.itemStyle.borderColor).toBe(createAppTheme('dark').palette.background.paper);
  });

  it('keeps names as plain strings (the canvas does not read them as markup)', () => {
    const option = buildCategoryOption({
      data: donut,
      tokens: tokens('light'),
      axisFormatter: money,
      showLabels: true,
      animate: false,
    });
    expect(option).toMatchObject({ animation: false });
    const [pie] = option.series as { data: { name: string }[] }[];
    expect(pie?.data[0]?.name).toBe('<b>Banking & Finance</b>');
  });

  it('formats the value axis of a stacked bar with the caller formatter', () => {
    const option = buildCategoryOption({
      data: {
        kind: 'stacked',
        categories: [{ id: 'a', name: 'A' }],
        stacks: [{ id: 's', name: 'S', color: 2, values: [4] }],
      },
      tokens: tokens('light'),
      axisFormatter: (v) => `<${String(v)}>`,
      showLabels: true,
      animate: true,
    });
    const axis = option.xAxis as { axisLabel: { formatter: (v: number) => string } };
    expect(axis.axisLabel.formatter(7)).toBe('<7>');
  });
});
