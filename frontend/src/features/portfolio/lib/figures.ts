import type { HoldingRow, Returns } from '@/shared/api';
import { formatInr, formatPercent } from '@/shared/lib/format';

/** A proper minus sign, so a loss is never mistaken for a hyphen. */
const minus = '−';

export type Direction = 'up' | 'down' | 'flat';

/** Amounts are shown in whole rupees: anything that rounds to zero is flat, not a tiny gain or loss. */
export function directionOf(value: number, decimals = 0): Direction {
  const rounded = Math.abs(value) < 0.5 * 10 ** -decimals ? 0 : value;
  if (rounded > 0) return 'up';
  return rounded < 0 ? 'down' : 'flat';
}

export const arrowOf = (direction: Direction): string => ({ up: '▲', down: '▼', flat: '' })[direction];

/** "+₹1,23,456", "−₹4,000", "₹0": the sign is part of the text. */
export function signedInr(value: number): string {
  const direction = directionOf(value);
  const text = formatInr(Math.abs(value));
  if (direction === 'up') return `+${text}`;
  return direction === 'down' ? `${minus}${text}` : formatInr(0);
}

/** "+16.10%", "−3.20%", "0.00%". */
export function signedPercent(value: number): string {
  const direction = directionOf(value, 2);
  const text = `${formatPercent(Math.abs(value))}%`;
  if (direction === 'up') return `+${text}`;
  return direction === 'down' ? `${minus}${text}` : '0.00%';
}

export type XirrState = 'ok' | 'insufficient' | 'n_m' | 'no_data' | 'no_price';

/** Why there is no XIRR, in plain words: one explanation per state the API names. */
export const xirrReasons: Record<Exclude<XirrState, 'ok'>, string> = {
  insufficient:
    'XIRR needs the first purchase at least 30 days before today, and money going both in and out. Younger holdings, or cash flows that give no single rate, show no XIRR.',
  n_m: 'Not meaningful: the annualised rate is beyond ±1,000 %, which says more about a very short holding than about the investment.',
  no_data: 'There are no cash flows yet (no lots), so there is nothing to annualise.',
  no_price: 'No price yet, so there is no current value and no XIRR.',
};

/** The same, short enough for a tile's hint line. */
export const xirrShortReasons: Record<Exclude<XirrState, 'ok'>, string> = {
  insufficient: 'Needs 30+ days of history',
  n_m: 'Not meaningful (beyond ±1,000 %)',
  no_data: 'No cash flows yet',
  no_price: 'No price yet',
};

/** Why an unpriced holding or lot shows no value and no profit. */
export const noPriceReason = 'No price yet, so there is no current value, profit or loss to show.';

/** Why the totals have no day change: no open holding has a previous close yet (it arrives with the first quote). */
export const noDayChangeReason =
  'No holding has a previous close yet, so there is no day change. It arrives with the next quote: press Refresh prices.';

/** Why one holding shows no day change. */
export function noDayChangeReasonOf(holding: Pick<HoldingRow, 'status' | 'priced'>): string {
  if (holding.status === 'closed') return 'A closed holding has no day change.';
  if (!holding.priced) return noPriceReason;
  return 'No previous close yet, so there is no day change. It arrives with the next quote.';
}

export type XirrFigure = { value: number } | { reason: string; short: string; label: '—' | 'n/m' };

/** The XIRR of a figure set: a number when the state is ok, else the reason it is missing ("n/m" for the not-meaningful case). */
export function xirrOf(returns: Pick<Returns, 'xirr' | 'xirrState'>): XirrFigure {
  if (returns.xirrState === 'ok' && returns.xirr !== null) return { value: returns.xirr };
  const state = Object.keys(xirrReasons).find((key) => key === returns.xirrState) as
    Exclude<XirrState, 'ok'> | undefined;
  if (!state) return { reason: 'XIRR is not available.', short: 'Not available', label: '—' };
  return { reason: xirrReasons[state], short: xirrShortReasons[state], label: state === 'n_m' ? 'n/m' : '—' };
}
