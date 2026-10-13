import type { HoldingGroup } from '@/shared/api';
import type { CategoryData, CategorySlice, StackCategory, StackSeries } from '@/shared/ui/chart';
import { categoricalSeries } from '@/theme';

/** Id of the merged "Other" slice, bar or stack (a group key never starts with `~` except the reserved `~none`). */
export const otherId = '~other';
const otherName = 'Other';
/** Groups beyond these are merged into "Other": a donut of more than eight slices cannot be read, nor told apart by colour. */
export const donutSlices = categoricalSeries;
/** Bars of the stacked chart before the rest is merged into one "Other" bar. */
export const stackedBars = 12;

const byValueDesc = (a: { value: number; name: string }, b: { value: number; name: string }) =>
  b.value - a.value || a.name.localeCompare(b.name);

/** Groups that have a value to draw: the unpriced-only ones and the empty ones are left out. */
const valued = (groups: readonly HoldingGroup[]) =>
  groups.flatMap((g) => (g.value !== null && g.value > 0 ? [{ group: g, value: g.value, name: g.name }] : []));

/** The share of each group in the value: the eight largest, and the rest as "Other". */
export function allocationDonut(groups: readonly HoldingGroup[]): Extract<CategoryData, { kind: 'donut' }> {
  const sorted = valued(groups).sort(byValueDesc);
  const slices: CategorySlice[] = sorted
    .slice(0, donutSlices)
    .map((g, index) => ({ id: g.group.key, name: g.name, value: g.value, color: index }));
  const rest = sorted.slice(donutSlices);
  if (rest.length > 0) {
    slices.push({ id: otherId, name: otherName, value: rest.reduce((sum, g) => sum + g.value, 0), color: donutSlices });
  }
  return { kind: 'donut', slices };
}

/** Adds `value` to the part with this key (the groups' parts, summed). */
function addPart(into: Map<string, { name: string; value: number }>, key: string, name: string, value: number) {
  const part = into.get(key);
  if (part) part.value += value;
  else into.set(key, { name, value });
}

/**
 * The stacked bar of the second dimension: one bar per group (the largest twelve, then "Other"), split into the parts
 * (the eight largest over all the groups, then "Other"). Null when the groups carry no parts (no `stackBy` asked for).
 */
export function exposureStack(groups: readonly HoldingGroup[]): Extract<CategoryData, { kind: 'stacked' }> | null {
  const rows = valued(groups)
    .filter((g) => g.group.parts !== null)
    .sort(byValueDesc);
  if (rows.length === 0) return null;

  const kept = rows.slice(0, stackedBars);
  const merged = rows.slice(stackedBars);
  const bars = [
    ...kept.map((r) => ({ id: r.group.key, name: r.name, parts: r.group.parts ?? [] })),
    ...(merged.length > 0 ? [{ id: otherId, name: otherName, parts: merged.flatMap((r) => r.group.parts ?? []) }] : []),
  ];

  const totals = new Map<string, { name: string; value: number }>();
  for (const bar of bars) for (const p of bar.parts) if (p.value > 0) addPart(totals, p.key, p.name, p.value);
  const ranked = [...totals.entries()].map(([key, part]) => ({ key, ...part })).sort(byValueDesc);
  const slotOf = new Map(ranked.slice(0, categoricalSeries).map((p, index) => [p.key, index]));

  const stacks: StackSeries[] = [];
  const stackIds = [...slotOf.keys()];
  const hasOther = ranked.length > categoricalSeries;
  const valuesOf = (match: (key: string) => boolean) =>
    bars.map((bar) => bar.parts.filter((p) => p.value > 0 && match(p.key)).reduce((sum, p) => sum + p.value, 0));
  for (const key of stackIds) {
    const part = ranked.find((p) => p.key === key);
    stacks.push({ id: key, name: part?.name ?? key, color: slotOf.get(key) ?? 0, values: valuesOf((k) => k === key) });
  }
  if (hasOther) {
    stacks.push({ id: otherId, name: otherName, color: categoricalSeries, values: valuesOf((k) => !slotOf.has(k)) });
  }
  const categories: StackCategory[] = bars.map((b) => ({ id: b.id, name: b.name }));
  return { kind: 'stacked', categories, stacks };
}
