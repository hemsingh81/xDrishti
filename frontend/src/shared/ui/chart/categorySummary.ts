import { categoryTotal, shareText, sliceTotal } from './categoryData';
import type { CategoryData } from './categoryTypes';

interface SummaryInput {
  label: string;
  data: CategoryData;
  valueFormatter: (value: number) => string;
}

/**
 * The chart in words, for assistive technology: what it shows and every slice (or bar) with its figures. The table is the
 * full alternative.
 */
export function categorySummary({ label, data, valueFormatter }: SummaryInput): string {
  if (data.kind === 'donut') {
    if (data.slices.length === 0) return `${label}: no data.`;
    const total = sliceTotal(data.slices);
    const parts = data.slices.map((s) => `${s.name} ${shareText(s.value, total)} (${valueFormatter(s.value)})`);
    const count = data.slices.length === 1 ? '1 slice' : `${String(data.slices.length)} slices`;
    return `${label}, ${count}, total ${valueFormatter(total)}: ${parts.join('; ')}.`;
  }
  if (data.categories.length === 0 || data.stacks.length === 0) return `${label}: no data.`;
  const rows = data.categories.map((category, index) => {
    const split = data.stacks
      .map((s) => ({ name: s.name, value: s.values[index] ?? 0 }))
      .filter((p) => p.value > 0)
      .map((p) => `${p.name} ${valueFormatter(p.value)}`);
    return `${category.name} ${valueFormatter(categoryTotal(data.stacks, index))}${split.length > 0 ? ` (${split.join(', ')})` : ''}`;
  });
  const count = data.categories.length === 1 ? '1 bar' : `${String(data.categories.length)} bars`;
  return `${label}, ${count}: ${rows.join('; ')}.`;
}
