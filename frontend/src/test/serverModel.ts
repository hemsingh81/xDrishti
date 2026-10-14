import type { HoldingGroup, HoldingGroups, HoldingRow, HoldingsSummary } from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import { buildHolding, buildReturns, buildSummary } from './fixtures';

/**
 * The server's portfolio arithmetic written a second time, independently of the code under test, for the property tests of
 * the live recompute: `ReturnsCalculator`, `DayChangeCalculator`, `GroupCalculator` and `HoldingViews.Row` of
 * `XDrishti.Application.Portfolio`, working on whole lots (as the server does) and in integer paise (so no float noise). The
 * browser sees only what a row says; this model has the lots, the previous close's time and the sector as well.
 */

export interface ModelLot {
  qty: number;
  unitCostPaise: number;
  closed: boolean;
  closePricePaise: number;
}

export interface ModelHolding {
  id: string;
  /** Shown in the row; a made-up one when absent. */
  symbol?: string;
  accountId: string;
  bucketId: string;
  sector: string;
  instrumentId: string | null;
  /** The broker's quantity (what the day change uses); the open lots' quantity is what the value uses. */
  qty: number;
  status: 'open' | 'closed';
  lots: ModelLot[];
  /** 0: no usable price. */
  pricePaise: number;
  priceAt: number | null;
  source: 'sync' | 'quote' | 'live';
  previousClosePaise: number | null;
  /** When the previous close was written (only a quote writes it); null: not known. */
  previousCloseAt: number | null;
}

const money = (paise: number): number => paise / 100;

/** Four decimals, halves away from zero. */
const round4 = (value: number): number => {
  const rounded = Math.round(Math.abs(value) * 10_000 + 1e-7) / 10_000;
  return value < 0 ? -rounded : rounded;
};

const isOpen = (h: ModelHolding): boolean => h.status === 'open';
const openLots = (h: ModelHolding): ModelLot[] => h.lots.filter((l) => !l.closed);
const isPriced = (h: ModelHolding): boolean => h.pricePaise > 0 || h.lots.every((l) => l.closed);

/** `DayChangeCalculator.Counts`. */
function counts(h: ModelHolding): boolean {
  if (!isOpen(h) || h.qty <= 0 || h.pricePaise <= 0 || h.previousClosePaise === null || h.previousClosePaise <= 0)
    return false;
  if (h.source !== 'live') return true;
  return h.previousCloseAt !== null && h.priceAt !== null && istDayIso(h.previousCloseAt) === istDayIso(h.priceAt);
}

interface Sums {
  invested: number;
  value: number;
  /** Day change in paise, the previous value of the counted holdings, how many count. */
  amount: number;
  previous: number;
  included: number;
  open: number;
  priced: number;
}

/** `ReturnsCalculator.ForGroup` and `DayChangeCalculator.ForGroup` over holdings. */
function sums(holdings: readonly ModelHolding[]): Sums {
  const total: Sums = { invested: 0, value: 0, amount: 0, previous: 0, included: 0, open: 0, priced: 0 };
  for (const h of holdings) {
    if (isPriced(h)) {
      total.priced += isOpen(h) ? 1 : 0;
      for (const lot of openLots(h)) {
        total.invested += lot.qty * lot.unitCostPaise;
        total.value += lot.qty * h.pricePaise;
      }
    }
    if (!isOpen(h)) continue;
    total.open += 1;
    if (counts(h)) {
      total.included += 1;
      total.amount += h.qty * (h.pricePaise - (h.previousClosePaise ?? 0));
      total.previous += h.qty * (h.previousClosePaise ?? 0);
    }
  }
  return total;
}

/** `HoldingViews.Row`. */
export function rowOf(h: ModelHolding): HoldingRow {
  const priced = isPriced(h);
  const s = sums([h]);
  const invested = priced ? s.invested : openLots(h).reduce((sum, l) => sum + l.qty * l.unitCostPaise, 0);
  const value = priced ? s.value : null;
  const pnlRaw = value === null ? null : value - invested;
  const counted = counts(h);
  const at = h.priceAt === null ? null : new Date(h.priceAt).toISOString();
  const previous = h.previousClosePaise;
  return buildHolding({
    id: h.id,
    accountId: h.accountId,
    bucketId: h.bucketId,
    instrumentId: h.instrumentId,
    mapped: h.instrumentId !== null,
    symbol: h.symbol ?? `S${h.id.slice(-4)}`,
    name: h.symbol ?? `Holding ${h.id.slice(-4)}`,
    qty: h.qty,
    status: h.status,
    lastPrice: money(h.pricePaise),
    lastPriceAt: at,
    priceAt: at,
    priceSource: h.source,
    priced,
    previousClose: previous === null ? null : money(previous),
    returns: buildReturns({
      invested: round4(money(invested)),
      value: value === null ? null : round4(money(value)),
      pnl: pnlRaw === null ? null : round4(money(pnlRaw)),
      pnlPct: pnlRaw === null || invested <= 0 ? null : round4((pnlRaw / invested) * 100),
      xirr: 12.5,
      xirrState: 'ok',
      realisedPnl: 0,
    }),
    dayChange: counted ? round4(money(h.qty * (h.pricePaise - (previous ?? 0)))) : null,
    dayChangePct: counted ? round4(((h.pricePaise - (previous ?? 0)) / (previous ?? 1)) * 100) : null,
  });
}

/** `ValueOf`: null when there are holdings and none has a usable price. */
function scopeValue(holdings: readonly ModelHolding[]): number | null {
  if (holdings.length > 0 && holdings.every((h) => !isPriced(h))) return null;
  return round4(money(sums(holdings).value));
}

const weight = (value: number | null, of: number | null): number | null =>
  value !== null && of !== null && of > 0 ? round4((value / of) * 100) : null;

function figures(holdings: readonly ModelHolding[]) {
  const s = sums(holdings);
  const nothingPriced = holdings.length > 0 && holdings.every((h) => !isPriced(h));
  const value = round4(money(s.value));
  const invested = round4(money(s.invested));
  return {
    holdings: holdings.length,
    unpriced: holdings.filter((h) => isOpen(h) && !isPriced(h)).length,
    invested,
    value: nothingPriced ? null : value,
    pnl: nothingPriced ? null : round4(money(s.value - s.invested)),
    pnlPct: nothingPriced || s.invested <= 0 ? null : round4(((s.value - s.invested) / s.invested) * 100),
    dayChange: s.included === 0 ? null : round4(money(s.amount)),
    dayChangePct: s.included === 0 || s.previous <= 0 ? null : round4((s.amount / s.previous) * 100),
    dayChangeExcluded: s.open - s.included,
  };
}

/** The summary of the holdings (the fields price moves change) merged into a full summary. */
export function summaryOf(holdings: readonly ModelHolding[], base: HoldingsSummary = buildSummary()): HoldingsSummary {
  const f = figures(holdings);
  const s = sums(holdings);
  const times = holdings
    .filter((h) => isOpen(h) && isPriced(h) && h.priceAt !== null)
    .map((h) => h.priceAt!)
    .sort((a, b) => a - b);
  const iso = (ms: number | undefined) => (ms === undefined ? null : new Date(ms).toISOString());
  return {
    ...base,
    returns: {
      ...base.returns,
      invested: f.invested,
      value: round4(money(s.value)),
      pnl: round4(money(s.value - s.invested)),
      pnlPct: s.invested > 0 ? round4(((s.value - s.invested) / s.invested) * 100) : null,
    },
    openCount: holdings.filter(isOpen).length,
    closedCount: holdings.filter((h) => !isOpen(h)).length,
    unpricedCount: f.unpriced,
    dayChange: f.dayChange,
    dayChangePct: f.dayChangePct,
    dayChangeExcluded: f.dayChangeExcluded,
    pricesAsOf: { oldest: iso(times[0]), newest: iso(times.at(-1)) },
  };
}

export type ModelDimension = 'account' | 'bucket' | 'sector';

const keyOf = (h: ModelHolding, by: ModelDimension): string =>
  by === 'account' ? h.accountId : by === 'bucket' ? h.bucketId : h.sector;

/** The groups of the holdings by a dimension, in order of first appearance, with the total row (`GroupCalculator`). */
export function groupsOf(holdings: readonly ModelHolding[], by: ModelDimension): HoldingGroups {
  const keys = [...new Set(holdings.map((h) => keyOf(h, by)))];
  const scope = scopeValue(holdings);
  const groups: HoldingGroup[] = keys.map((key) => {
    const members = holdings.filter((h) => keyOf(h, by) === key);
    const f = figures(members);
    return {
      key,
      name: key,
      ...f,
      realised: 0,
      xirr: 12.5,
      xirrState: 'ok',
      weightPct: weight(f.value, scope),
      parts: null,
    };
  });
  const total = figures(holdings);
  return {
    by,
    additive: true,
    stackBy: null,
    groups,
    total: { ...total, realised: 0, xirr: 12.5, xirrState: 'ok', weightPct: weight(total.value, total.value) },
  };
}

/** A hub price written to the holdings of an instrument, with the server's precedence: only a newer price time (or the same time and another price) replaces. */
export function writePrice(
  holdings: readonly ModelHolding[],
  instrumentId: string,
  pricePaise: number,
  at: number,
  source: ModelHolding['source'],
): ModelHolding[] {
  return holdings.map((h) => {
    if (h.instrumentId !== instrumentId || !isOpen(h)) return h;
    const known = h.priceAt ?? Number.NEGATIVE_INFINITY;
    if (at < known || (at === known && pricePaise === h.pricePaise)) return h;
    // A quote writes the previous close with its price (the same value here), a live tick or a sync does not.
    return {
      ...h,
      pricePaise,
      priceAt: at,
      source,
      previousCloseAt: source === 'quote' ? at : h.previousCloseAt,
    };
  });
}
