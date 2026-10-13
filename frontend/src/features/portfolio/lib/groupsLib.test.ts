import type { HoldingGroup } from '@/shared/api';
import { buildGroup } from '@/test/groupFixtures';
import { categoricalSeries } from '@/theme';
import { allocationDonut, exposureStack, otherId, stackedBars } from './allocation';
import { defaultSort } from './filters';
import { groupSortOf, nextGroupSort, stackByOf, unpricedText } from './groups';

const group = (n: number, value: number | null, parts: HoldingGroup['parts'] = null) =>
  buildGroup({ key: `k${String(n)}`, name: `Group ${String(n)}`, value, parts });
const part = (key: string, value: number) => ({ key, name: key.toUpperCase(), value });

describe('group sort', () => {
  it('keeps a sort the groups have and falls back to the largest value first', () => {
    expect(groupSortOf({ field: 'pnl', descending: false })).toEqual({ field: 'pnl', descending: false });
    expect(groupSortOf({ field: 'name', descending: true })).toEqual({ field: 'name', descending: true });
    expect(groupSortOf(defaultSort)).toEqual({ field: 'value', descending: true });
    expect(groupSortOf({ field: 'qty', descending: false })).toEqual({ field: 'value', descending: true });
  });

  it('flips the direction of the sorted field and starts a new one ascending', () => {
    expect(nextGroupSort({ field: 'value', descending: true }, 'value')).toEqual({ field: 'value', descending: false });
    expect(nextGroupSort({ field: 'value', descending: true }, 'pnl')).toEqual({ field: 'pnl', descending: false });
  });

  it('stacks accounts by sector and sectors by account, nothing else', () => {
    expect(stackByOf('sector')).toBe('account');
    expect(stackByOf('account')).toBe('sector');
    expect(stackByOf('bucket')).toBeUndefined();
    expect(stackByOf('basket')).toBeUndefined();
  });
});

describe('the flag for holdings without a price', () => {
  it('says nothing when all have one, "N without a price" for some and "N holdings without a price" for all', () => {
    expect(unpricedText(3, 0)).toBeNull();
    expect(unpricedText(3, 1)).toBe('1 without a price');
    expect(unpricedText(3, 3)).toBe('3 holdings without a price');
    expect(unpricedText(1, 1)).toBe('1 holding without a price');
  });
});

describe('the allocation donut', () => {
  it('orders the slices by value, whatever the order of the table', () => {
    const donut = allocationDonut([group(1, 10), group(2, 30), group(3, 20)]);
    expect(donut.slices.map((s) => [s.name, s.color])).toEqual([
      ['Group 2', 0],
      ['Group 3', 1],
      ['Group 1', 2],
    ]);
  });

  it('leaves out groups without a value', () => {
    const donut = allocationDonut([group(1, null), group(2, 0), group(3, 5)]);
    expect(donut.slices.map((s) => s.name)).toEqual(['Group 3']);
  });

  it('keeps the eight largest and merges the rest into "Other" with the neutral colour', () => {
    const groups = Array.from({ length: 11 }, (_, i) => group(i, 100 - i));
    const donut = allocationDonut(groups);
    expect(donut.slices).toHaveLength(categoricalSeries + 1);
    const other = donut.slices.at(-1);
    expect(other).toMatchObject({ id: otherId, name: 'Other', color: categoricalSeries, value: 92 + 91 + 90 });
  });

  it('does not make an "Other" of nothing', () => {
    expect(allocationDonut(Array.from({ length: 8 }, (_, i) => group(i, 10 + i))).slices.at(-1)?.name).not.toBe(
      'Other',
    );
  });
});

describe('the exposure stack', () => {
  it('is null when the groups carry no parts', () => {
    expect(exposureStack([group(1, 10)])).toBeNull();
    expect(exposureStack([])).toBeNull();
  });

  it('makes a bar per group and a stack per part, aligned, largest first', () => {
    const stacked = exposureStack([group(1, 30, [part('a', 10), part('b', 20)]), group(2, 50, [part('a', 50)])]);
    expect(stacked?.categories.map((c) => c.name)).toEqual(['Group 2', 'Group 1']);
    expect(stacked?.stacks.map((s) => [s.id, s.values, s.color])).toEqual([
      ['a', [50, 10], 0],
      ['b', [0, 20], 1],
    ]);
  });

  it('merges parts beyond the eight largest into "Other"', () => {
    const parts = Array.from({ length: 10 }, (_, i) => part(`p${String(i)}`, 100 - i));
    const stacked = exposureStack([group(1, 955, parts)]);
    expect(stacked?.stacks).toHaveLength(categoricalSeries + 1);
    expect(stacked?.stacks.at(-1)).toMatchObject({ id: otherId, name: 'Other', values: [92 + 91] });
  });

  it('merges the bars beyond the twelve largest into one "Other" bar, summing their parts', () => {
    const groups = Array.from({ length: stackedBars + 3 }, (_, i) => group(i, 100 - i, [part('a', 100 - i)]));
    const stacked = exposureStack(groups);
    expect(stacked?.categories).toHaveLength(stackedBars + 1);
    expect(stacked?.categories.at(-1)?.id).toBe(otherId);
    const rest = groups.slice(stackedBars).reduce((sum, g) => sum + (g.value ?? 0), 0);
    expect(stacked?.stacks[0]?.values.at(-1)).toBe(rest);
  });

  it('leaves out unpriced groups', () => {
    const stacked = exposureStack([group(1, null, [part('a', 1)]), group(2, 5, [part('a', 5)])]);
    expect(stacked?.categories.map((c) => c.name)).toEqual(['Group 2']);
  });
});
