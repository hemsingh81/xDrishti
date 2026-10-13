import { formatShare } from '@/shared/lib/format';
import type { CategoryData, CategorySlice, StackSeries } from './categoryTypes';

/** The data without what the legend has switched off. */
export function visibleData(data: CategoryData, hidden: ReadonlySet<string>): CategoryData {
  return data.kind === 'donut'
    ? { kind: 'donut', slices: data.slices.filter((s) => !hidden.has(s.id)) }
    : { ...data, stacks: data.stacks.filter((s) => !hidden.has(s.id)) };
}

/** What can be switched on and off in the legend: the slices of a donut, the stacks of a bar. */
export const legendEntries = (data: CategoryData): readonly (CategorySlice | StackSeries)[] =>
  data.kind === 'donut' ? data.slices : data.stacks;

export const sliceTotal = (slices: readonly CategorySlice[]): number => slices.reduce((sum, s) => sum + s.value, 0);

/** Total of a category row across the stacks. */
export const categoryTotal = (stacks: readonly StackSeries[], index: number): number =>
  stacks.reduce((sum, s) => sum + (s.values[index] ?? 0), 0);

/** A slice's share of the visible ones, in percent (0 when nothing is visible). */
export function shareOf(value: number, total: number): number {
  return total > 0 ? (value / total) * 100 : 0;
}

/** "32.1%". */
export const shareText = (value: number, total: number): string => `${formatShare(shareOf(value, total))}%`;

/** A long name cut for a label on the canvas (the legend and the table have it whole). */
export function shortName(name: string, max = 18): string {
  return name.length > max ? `${name.slice(0, max - 1)}…` : name;
}
