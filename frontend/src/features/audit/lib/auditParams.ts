import { istDayIso } from '@/shared/lib/format';

/** The range presets. Dates are India days; "24 hours" is yesterday and today, the smallest range the API takes. */
export const presets = ['24h', '7d', '30d', '90d', 'custom'] as const;
export type Preset = (typeof presets)[number];

export const presetLabels: Readonly<Record<Preset, string>> = {
  '24h': '24 hours',
  '7d': '7 days',
  '30d': '30 days',
  '90d': '90 days',
  custom: 'Custom',
};

const presetDays: Readonly<Record<Exclude<Preset, 'custom'>, number>> = { '24h': 2, '7d': 7, '30d': 30, '90d': 90 };

/** What the URL holds; every field is optional text (a comma list for source, category, outcome, actor and kind). */
export interface AuditFilter {
  preset: Preset;
  from: string | null;
  to: string | null;
  source: string | null;
  category: string | null;
  kind: string | null;
  outcome: string | null;
  actor: string | null;
  account: string | null;
  q: string | null;
}

/** The days before today, as India dates. */
function daysBefore(today: string, days: number): string {
  const date = new Date(`${today}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() - days);
  return date.toISOString().slice(0, 10);
}

/** The API query for a filter: the dates of the preset (or the custom ones) and every filter that is set. */
export function toQuery(filter: AuditFilter, now: Date = new Date()): Record<string, string> {
  const today = istDayIso(now);
  const query: Record<string, string> = {};
  if (filter.preset === 'custom') {
    if (filter.from) query.from = filter.from;
    if (filter.to) query.to = filter.to;
  } else {
    query.from = daysBefore(today, presetDays[filter.preset] - 1);
    query.to = today;
  }
  for (const key of ['source', 'category', 'kind', 'outcome', 'actor', 'account', 'q'] as const) {
    const value = filter[key];
    if (value) query[key] = value;
  }
  return query;
}

/** The values of a comma list in the URL. */
export function listOf(value: string | null): string[] {
  return value ? value.split(',').filter(Boolean) : [];
}

export function toggled(value: string | null, item: string): string | null {
  const items = listOf(value);
  const next = items.includes(item) ? items.filter((i) => i !== item) : [...items, item];
  return next.length > 0 ? next.join(',') : null;
}

/** True when any filter beyond the range is set. */
export function hasFilters(filter: AuditFilter): boolean {
  return Boolean(
    filter.source ?? filter.category ?? filter.kind ?? filter.outcome ?? filter.actor ?? filter.account ?? filter.q,
  );
}

/** Words for the codes the API sends; an unknown code is shown as it is. */
export function words(code: string): string {
  const text = code.replaceAll('_', ' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** Who did it, as words: an automatic rule or a service is never shown as the owner, and an unknown sign-in address is not shown. */
export function actorText(row: { actorKind: string; actor: string | null }): string {
  if (row.actorKind === 'other_address') return 'Another address';
  return row.actor ?? words(row.actorKind);
}
