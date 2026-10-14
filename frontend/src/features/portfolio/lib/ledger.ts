import type { LedgerClassTotal } from '@/shared/api';
import { formatInrPrecise } from '@/shared/lib/format';
import { directionOf, type Direction } from './figures';

/** The classes of a ledger entry, in the order the totals are shown. */
export const ledgerClasses = ['funds_in', 'funds_out', 'dividend', 'charges', 'other'] as const;
export type LedgerClass = (typeof ledgerClasses)[number];

export const ledgerClassLabels: Record<LedgerClass, string> = {
  funds_in: 'Funds in',
  funds_out: 'Funds out',
  dividend: 'Dividends',
  charges: 'Charges',
  other: 'Other',
};

export const isLedgerClass = (value: string): value is LedgerClass => ledgerClasses.some((c) => c === value);

/** The word for a class; a class this screen does not know (a newer API) is shown as Other. */
export const classLabel = (value: string): string => ledgerClassLabels[isLedgerClass(value) ? value : 'other'];

/** What the list and the totals are asked for: the period (India dates, '' = no limit) and one class or all. */
export interface LedgerFilter {
  from: string;
  to: string;
  cls: LedgerClass | 'all';
}

export const noLedgerFilter: LedgerFilter = { from: '', to: '', cls: 'all' };

/** The totals of all five classes, in order; a class the API left out counts as no entries. */
export function totalsOf(totals: readonly LedgerClassTotal[]): LedgerClassTotal[] {
  return ledgerClasses.map((cls) => totals.find((t) => t.class === cls) ?? { class: cls, count: 0, amount: 0 });
}

/** A signed amount in rupees and paise: "+₹1,234.50", "−₹20.00", "₹0.00". The sign is part of the text. */
export function signedPrecise(value: number): string {
  const direction = directionOf(value, 2);
  const text = formatInrPrecise(Math.abs(value));
  if (direction === 'up') return `+${text}`;
  return direction === 'down' ? `−${text}` : formatInrPrecise(0);
}

export const amountDirection = (value: number): Direction => directionOf(value, 2);

/** The period is valid when both ends are empty or dates, and the start is not after the end. */
export const periodError = ({ from, to }: LedgerFilter): string | null =>
  from !== '' && to !== '' && from > to ? 'The start date is after the end date.' : null;
