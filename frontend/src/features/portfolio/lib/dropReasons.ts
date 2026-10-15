import type { BrokerDroppedRow, BrokerKindStatus } from '@/shared/api';
import { formatDate, formatDayMonth, formatNumber } from '@/shared/lib/format';

/**
 * Why a trades or ledger read dropped a row (roadmap ISS-03), in plain words. The API sends a code and the place of the row,
 * never its content, so nothing here can show a name, symbol or amount. The order is the order of ties when counts are equal.
 */
const reasonTexts: Record<string, string> = {
  unreadable_row: 'unreadable row',
  missing_id: 'no trade id',
  bad_id: 'trade id too long',
  missing_side: 'side is not buy or sell',
  bad_quantity: 'quantity missing or not a whole positive number',
  bad_price: 'price unreadable',
  bad_time: 'time unreadable',
  bad_date: 'date unreadable',
  missing_amount: 'no amount',
  bad_amount: 'amount unreadable',
  zero_amount: 'zero amount (no money moved, e.g. an opening balance)',
  failed_checks: 'broke a storage rule',
  store_refused: 'the database refused it',
  other: 'other',
};

const order = Object.keys(reasonTexts);
const maxReasons = 3;
/** A ledger line where no money moved (an opening balance, or debit equal to credit): harmless, so a read that dropped only these is mentioned quietly. */
const harmless = 'zero_amount';

/** The words for a reason code; a code this version does not know reads "other" (the text is never taken from the API). */
export const dropReasonText = (code: string): string => reasonTexts[code] ?? 'other';

export interface DropsSummary {
  /** Rows the latest run dropped, in all. */
  total: number;
  /** "15 rows could not be read: 9 quantity missing or not a whole positive number, 4 no trade id, 2 time unreadable, +1 more". */
  text: string;
  /** Only lines where no money moved were dropped: shown muted, with no details. */
  quiet: boolean;
  /** Where the first rows were, one line each: "window 1 Sep–30 Sep, page 1, row 3: price unreadable". */
  locations: string[];
}

const rank = (code: string): number => {
  const at = order.indexOf(code);
  return at < 0 ? order.length - 1 : at;
};

/** "1 Sep–30 Sep" (with the year when the window crosses years); one date when both ends are the same day. */
function windowText(from: string, to: string): string {
  if (from === to) return formatDayMonth(from);
  return from.slice(0, 4) === to.slice(0, 4)
    ? `${formatDayMonth(from)}–${formatDayMonth(to)}`
    : `${formatDate(from)}–${formatDate(to)}`;
}

/** The place of one dropped row for a human: pages and rows count from 1 (the API counts from 0); the ledger has no page. */
export function locationText(row: BrokerDroppedRow, withPage: boolean): string {
  const page = withPage ? `, page ${formatNumber(row.page + 1)}` : '';
  return `window ${windowText(row.windowFrom, row.windowTo)}${page}, row ${formatNumber(row.rowIndex + 1)}: ${dropReasonText(row.reason)}`;
}

/** The dropped rows of a kind's latest run; null when it dropped none or the API does not say (a run from before ISS-03). */
export function summariseDrops(kind: 'trades' | 'ledger', status: BrokerKindStatus): DropsSummary | null {
  const byReason = status.droppedByReason ?? status.lastRun?.counts.droppedByReason ?? {};
  // Several codes may read the same ("other" for an unknown one): their counts add up.
  const merged = new Map<string, number>();
  for (const [code, n] of Object.entries(byReason)) {
    if (n <= 0) continue;
    const known = code in reasonTexts ? code : 'other';
    merged.set(known, (merged.get(known) ?? 0) + n);
  }
  const total = [...merged.values()].reduce((sum, n) => sum + n, 0);
  if (total <= 0) return null;
  const quiet = merged.size === 1 && merged.has(harmless);
  if (quiet) return { total, text: 'lines where no money moved ignored', quiet, locations: [] };

  const sorted = [...merged.entries()].sort(([a, x], [b, y]) => y - x || rank(a) - rank(b));
  const shown = sorted.slice(0, maxReasons).map(([code, n]) => `${formatNumber(n)} ${dropReasonText(code)}`);
  const more = sorted.length - maxReasons;
  if (more > 0) shown.push(`+${formatNumber(more)} more`);
  const rows = `${formatNumber(total)} ${total === 1 ? 'row' : 'rows'}`;
  const rowRows = status.droppedRows ?? status.lastRun?.counts.droppedRows ?? [];
  return {
    total,
    text: `${rows} could not be read: ${shown.join(', ')}`,
    quiet,
    locations: rowRows.map((row) => locationText(row, kind === 'trades')),
  };
}
