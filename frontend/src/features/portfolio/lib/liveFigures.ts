import type { HoldingRow, IncomeView } from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import type { LiveTick, LiveTicks } from './liveTicks';

/**
 * The server's figures, worked out again in the browser for a price that arrived over the hub (ADR 0024 §7). Each function
 * is the TypeScript twin of one in `XDrishti.Application.Portfolio`: `ReturnsCalculator` (value = quantity of the open lots
 * x last price, profit = value - invested, the percentage of invested), `DayChangeCalculator` (quantity x (last - previous
 * close), left out without a usable previous close, and for a live price when the previous close is not of the tick's India
 * day), and `GroupCalculator` (sums over the holdings of a group, the weight as a share of the scope's value). Money is
 * rounded to four decimals as the server rounds it. What cannot be worked out from a row (XIRR, the net XIRR, the benchmark,
 * the history) stays as the last read gave it, and the read every five minutes makes everything exact again. The dividends and
 * charges of an `income` never change with a price; only its two total return percentages do (`incomeAt`, the twin of
 * `IncomeCalculator.Percent`).
 */

const scale = 10_000;

/** Four decimals, halves away from zero (the server's `MidpointRounding.AwayFromZero`). */
export const round4 = (value: number): number => {
  const rounded = Math.round(Math.abs(value) * scale + 1e-7) / scale;
  // A small negative that rounds to nothing is 0, not -0 (the server's decimal has no negative zero).
  return value < 0 && rounded !== 0 ? -rounded : rounded;
};

/**
 * The income of a scope at a live profit (ADR 0026): the dividends, charges, dates and net XIRR are the read's; the total
 * return % is (realised + profit + dividends) over invested and the net one is the same less the charges, rounded once, null
 * without invested capital or without a usable price (`pnl` null: nothing of the scope is priced). The scope's own `realised`
 * and `invested` are used. An income that is not there (nothing recorded) stays as it is.
 */
export function incomeAt(
  income: IncomeView | null | undefined,
  realised: number,
  invested: number,
  pnl: number | null,
): IncomeView | null | undefined {
  if (!income) return income;
  const percent = (extra: number): number | null =>
    invested > 0 && pnl !== null ? round4(((realised + pnl + extra) / invested) * 100) : null;
  return {
    ...income,
    totalReturnPct: percent(income.dividends),
    netTotalReturnPct: percent(income.dividends - income.charges),
  };
}

export const isOpenRow = (row: HoldingRow): boolean => row.status === 'open';

/**
 * The quantity of the open lots, which is what the server values (the sum of the lots, not the broker's quantity). The row
 * does not carry it, but its value is that quantity x its price rounded to four decimals, so the quantity is value / price,
 * a whole number. A row without a value (no price yet) has no better answer than the holding's quantity.
 */
export function openQtyOf(row: HoldingRow): number {
  const { value } = row.returns;
  return value !== null && row.lastPrice > 0 ? Math.round(value / row.lastPrice) : row.qty;
}

/**
 * The India day the row's previous close was taken, as far as the row says. A quote writes the close together with its
 * price, so a quote-priced row's close is of its price time's day; a live-priced row that counts a day change has a close of
 * its price time's day (that is the rule the server applied). Anything else is unknown (null): the live price then leaves the
 * holding out of the day change, as the server does until a quote of the day wrote the close. That can leave out one the
 * server counts (a quote of the day that arrived under a newer live price), until the next read.
 */
export function previousCloseDay(row: HoldingRow): string | null {
  if (row.previousClose === null || row.priceAt === null) return null;
  const counted = row.priceSource === 'quote' || (row.priceSource === 'live' && row.dayChange !== null);
  return counted ? istDayIso(new Date(row.priceAt)) : null;
}

/** The row at a pushed price: price, price time and source, value, profit, and day change. The row itself when the price is not newer. */
export function applyTick(row: HoldingRow, tick: LiveTick | undefined): HoldingRow {
  if (!tick || !isOpenRow(row) || !(tick.price > 0)) return row;
  const known = row.priceAt === null ? Number.NEGATIVE_INFINITY : Date.parse(row.priceAt);
  if (tick.at < known || (tick.at === known && tick.price === row.lastPrice)) return row;

  const { invested } = row.returns;
  const value = openQtyOf(row) * tick.price;
  const profit = value - invested;
  const previous = row.previousClose;
  const counts =
    row.qty > 0 &&
    previous !== null &&
    previous > 0 &&
    (tick.source !== 'live' || previousCloseDay(row) === istDayIso(new Date(tick.at)));
  return {
    ...row,
    lastPrice: tick.price,
    lastPriceAt: tick.atIso,
    priceAt: tick.atIso,
    priceSource: tick.source,
    priced: true,
    returns: {
      ...row.returns,
      value: round4(value),
      pnl: round4(profit),
      pnlPct: invested > 0 ? round4((profit / invested) * 100) : null,
    },
    income: incomeAt(row.income, row.returns.realisedPnl, invested, round4(profit)),
    dayChange: counts ? round4(row.qty * (tick.price - previous)) : null,
    dayChangePct: counts ? round4(((tick.price - previous) / previous) * 100) : null,
  };
}

/**
 * The rows at the pushed prices. A row whose price did not change keeps its identity (the grid's rows are memoised), and so
 * does the whole array when no row changed.
 */
export function applyTicks(rows: readonly HoldingRow[], ticks: LiveTicks): readonly HoldingRow[] {
  if (ticks.size === 0) return rows;
  const next = rows.map((row) => (row.instrumentId === null ? row : applyTick(row, ticks.get(row.instrumentId))));
  return next.some((row, index) => row !== rows[index]) ? next : rows;
}

/** The figures a group, the total row and the tiles show, of the holdings given (closed ones are ignored). */
export interface LiveFigures {
  /** Open holdings, and those of them with a usable price. */
  open: number;
  priced: number;
  invested: number;
  /** Of the priced holdings; 0 when there are none (the summary shows 0, a group of only unpriced holdings shows none). */
  value: number;
  pnl: number;
  pnlPct: number | null;
  dayChange: number | null;
  dayChangePct: number | null;
  dayChangeExcluded: number;
  /** The oldest and the newest price time of the open priced holdings (ISO). */
  oldestPriceAt: string | null;
  newestPriceAt: string | null;
}

export function figuresOf(rows: readonly HoldingRow[]): LiveFigures {
  const open = rows.filter(isOpenRow);
  const priced = open.filter((row) => row.priced);
  const invested = priced.reduce((sum, row) => sum + row.returns.invested, 0);
  const value = priced.reduce((sum, row) => sum + (row.returns.value ?? 0), 0);

  let amount = 0;
  let previousValue = 0;
  let included = 0;
  for (const row of open) {
    if (row.dayChange === null || row.previousClose === null) continue;
    included += 1;
    amount += row.dayChange;
    previousValue += row.qty * row.previousClose;
  }

  const byTime = priced
    .flatMap((row) => (row.priceAt === null ? [] : [row.priceAt]))
    .sort((a, b) => Date.parse(a) - Date.parse(b));
  return {
    open: open.length,
    priced: priced.length,
    invested: round4(invested),
    value: round4(value),
    pnl: round4(value - invested),
    pnlPct: invested > 0 ? round4(((value - invested) / invested) * 100) : null,
    dayChange: included === 0 ? null : round4(amount),
    dayChangePct: included === 0 || !(previousValue > 0) ? null : round4((amount / previousValue) * 100),
    dayChangeExcluded: open.length - included,
    oldestPriceAt: byTime[0] ?? null,
    newestPriceAt: byTime.at(-1) ?? null,
  };
}

/** A holding's share of a scope's value, in percent; null when either is unknown or the scope has no value. */
export const weightOf = (value: number | null, scopeValue: number | null): number | null =>
  value !== null && scopeValue !== null && scopeValue > 0 ? round4((value / scopeValue) * 100) : null;
