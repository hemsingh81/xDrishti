export interface DatePreset {
  id: string;
  label: string;
  /** Days back from the end of the data; `null` = all (no date filter). */
  days: number | null;
}

export const datePresets: readonly DatePreset[] = [
  { id: '1M', label: '1M', days: 30 },
  { id: '3M', label: '3M', days: 91 },
  { id: '6M', label: '6M', days: 182 },
  { id: '1Y', label: '1Y', days: 365 },
  { id: 'All', label: 'All', days: null },
];

const DAY_MS = 86_400_000;

/** `from` of a preset: the calendar day `days` before `dataEnd` (yyyy-MM-dd), by UTC date arithmetic. `All` has none. */
export function presetFrom(dataEnd: string, days: number | null): string | null {
  if (days === null) return null;
  return new Date(Date.parse(`${dataEnd}T00:00:00Z`) - days * DAY_MS).toISOString().slice(0, 10);
}

/** The newest `windowEnd` among the runs: the end of the data (presets are measured from it, never from today). */
export function dataEndOf(windowEnds: readonly string[]): string | null {
  return windowEnds.reduce<string | null>((newest, end) => (newest === null || end > newest ? end : newest), null);
}
