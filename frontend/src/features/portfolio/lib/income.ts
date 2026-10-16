import type { DividendView, HoldingGroup, IncomeView } from '@/shared/api';
import { formatDate, formatInrPrecise } from '@/shared/lib/format';
import { xirrOf, type XirrFigure } from './figures';

/** What alpha and the benchmark XIRR are: the index has no dividends and the XIRR they are set against has no charges (ADR 0026). */
export const priceReturnBasis = 'price return, before dividends and charges';

/** "from 14 Mar 2024", or, when nothing in that part was read or entered, the plain words for it. */
export const fromText = (date: string | null, none: string): string =>
  date === null ? none : `from ${formatDate(date)}`;

/** The net XIRR of an income as the XIRR figure of a tile: a number, or the reason there is none. */
export const netXirrOf = (income: IncomeView): XirrFigure =>
  xirrOf({ xirr: income.netXirr, xirrState: income.netXirrState });

/** The part of the dividends no holding owns, when there is any worth saying (an account or the portfolio, above zero). */
export const unallocatedOf = (income: IncomeView): number | null =>
  income.unallocated !== null && income.unallocated > 0 ? income.unallocated : null;

/**
 * Under the Dividends tile: the day the owner's first dividend entry is from, so a total never reads as "all the dividends
 * there were". What was paid before it is not counted.
 */
export const dividendsHint = (income: IncomeView): string =>
  income.dividendsFrom === null ? 'none entered yet' : `entered ${fromText(income.dividendsFrom, '')}`;

/**
 * Under the Costs tile: the two sources of charges, each with its own first day, and only the ones that exist - the trade
 * charges stored per trade, and the account costs of the ledger (read from the first day of the ledger).
 */
export const costsHint = (income: IncomeView): string => {
  const parts = [
    income.tradeChargesFrom === null ? null : `trade charges ${fromText(income.tradeChargesFrom, '')}`,
    income.ledgerChargesFrom === null ? null : `account costs ${fromText(income.ledgerChargesFrom, '')}`,
  ].filter((part) => part !== null);
  return parts.length === 0 ? 'none read yet' : parts.join(' · ');
};

/** "of which account costs ₹120", or null when there are none to mention (below account level, or zero). */
export const ledgerChargesNote = (income: IncomeView, format: (value: number) => string): string | null =>
  income.ledgerCharges !== null && income.ledgerCharges > 0
    ? `of which account costs ${format(income.ledgerCharges)}`
    : null;

/**
 * Under the net tiles: the later start of the sources that exist (the first dividend entered and the charges), so the
 * figure is never counted from a day earlier than any of its sources.
 */
export const coverageHint = (income: IncomeView): string =>
  income.coverageFrom === null ? 'nothing recorded yet' : `counted ${fromText(income.coverageFrom, '')}`;

/** The row of a group that holds what belongs to no group (ADR 0026): not a holding group, it cannot be opened. */
export const isUnallocatedGroup = (group: Pick<HoldingGroup, 'key'>): boolean => group.key === '~unallocated';

/** A dividend as a sentence for a button or a toast: "the dividend of ₹1,200.00 on 5 Dec 2025". */
export const dividendName = (dividend: Pick<DividendView, 'amount' | 'payDate'>): string =>
  `dividend of ${formatInrPrecise(dividend.amount)} on ${formatDate(dividend.payDate)}`;
