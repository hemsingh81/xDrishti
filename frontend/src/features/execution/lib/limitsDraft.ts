import type { Schemas } from '@/shared/api';

type Row = Schemas['LimitsRowView'];
type Ceilings = Schemas['LimitsCeilingsView'];

export interface LimitsDraft {
  maxOrdersPerDay: string;
  maxNotionalPerOrder: string;
  maxNotionalPerDay: string;
  maxLossPerDay: string;
  maxOpenPositions: string;
  allowList: string;
  windowStart: string;
  windowEnd: string;
  products: string[];
  exitAllOnLossHalt: boolean;
}

export const numericFields = [
  ['maxOrdersPerDay', 'Orders a day'],
  ['maxNotionalPerOrder', 'Notional per order (Rs)'],
  ['maxNotionalPerDay', 'Notional a day (Rs)'],
  ['maxLossPerDay', 'Worst-case loss a day (Rs)'],
  ['maxOpenPositions', 'Open positions'],
] as const;

const text = (v: number | null | undefined) => (v === null || v === undefined ? '' : String(v));
const num = (s: string): number | null => (s.trim() === '' || !Number.isFinite(Number(s)) ? null : Number(s));
export const tokens = (s: string): string[] => [
  ...new Set(
    s
      .split(/[\s,]+/)
      .filter(Boolean)
      .map((t) => t.toUpperCase()),
  ),
];

export function draftOf(row: Row | null): LimitsDraft {
  return {
    maxOrdersPerDay: text(row?.maxOrdersPerDay),
    maxNotionalPerOrder: text(row?.maxNotionalPerOrder),
    maxNotionalPerDay: text(row?.maxNotionalPerDay),
    maxLossPerDay: text(row?.maxLossPerDay),
    maxOpenPositions: text(row?.maxOpenPositions),
    allowList: (row?.allowList ?? []).join(', '),
    windowStart: row?.windowStart ?? '',
    windowEnd: row?.windowEnd ?? '',
    products: row?.products ?? [],
    exitAllOnLossHalt: row?.exitAllOnLossHalt ?? false,
  };
}

/** The fields whose new value gives more authority than the sheet in force: a cap raised or newly set, a wider window, more names or products. */
export function raisedFields(draft: LimitsDraft, current: Row | null): string[] {
  const out: string[] = [];
  for (const [key] of numericFields) {
    const next = num(draft[key]);
    const cur = current?.[key] ?? null;
    if (next !== null && (cur === null || next > cur)) out.push(key);
  }
  const names = new Set(current?.allowList ?? []);
  if (tokens(draft.allowList).some((t) => !names.has(t))) out.push('allowList');
  if (draft.windowStart !== '' && (!current?.windowStart || draft.windowStart < current.windowStart))
    out.push('windowStart');
  if (draft.windowEnd !== '' && (!current?.windowEnd || draft.windowEnd > current.windowEnd)) out.push('windowEnd');
  if (draft.products.some((p) => !(current?.products ?? []).includes(p))) out.push('products');
  if ((current?.exitAllOnLossHalt ?? false) && !draft.exitAllOnLossHalt) out.push('exitAllOnLossHalt');
  return out;
}

/** A field above its ceiling is refused here, so the form never sends it (the server would refuse it too). */
export function aboveCeiling(draft: LimitsDraft, ceilings: Ceilings): string[] {
  const out: string[] = [];
  if ((num(draft.maxOrdersPerDay) ?? 0) > ceilings.maxOrdersPerDay) out.push('maxOrdersPerDay');
  if ((num(draft.maxOpenPositions) ?? 0) > ceilings.maxOpenPositions) out.push('maxOpenPositions');
  if (tokens(draft.allowList).length > ceilings.maxAllowList) out.push('allowList');
  if (draft.windowStart !== '' && draft.windowStart < ceilings.windowEarliest) out.push('windowStart');
  if (draft.windowEnd !== '' && draft.windowEnd > ceilings.windowLatest) out.push('windowEnd');
  return out;
}

export function bodyOf(draft: LimitsDraft, reason: string, password: string): Schemas['LimitsChangeBody'] {
  return {
    maxOrdersPerDay: num(draft.maxOrdersPerDay),
    maxNotionalPerOrder: num(draft.maxNotionalPerOrder),
    maxNotionalPerDay: num(draft.maxNotionalPerDay),
    maxLossPerDay: num(draft.maxLossPerDay),
    maxOpenPositions: num(draft.maxOpenPositions),
    allowList: tokens(draft.allowList),
    windowStart: draft.windowStart || null,
    windowEnd: draft.windowEnd || null,
    products: draft.products,
    exitAllOnLossHalt: draft.exitAllOnLossHalt,
    reason,
    password: password || null,
  };
}
