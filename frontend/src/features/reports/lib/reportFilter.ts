import { isRealDay } from '@/shared/lib/dates';

export const views = [
  'overview',
  'what-works',
  'baskets',
  'pivot',
  'strategies',
  'side',
  'calendar',
  'maemfe',
  'distribution',
  'instruments',
  'sectors',
] as const;
export type ReportView = (typeof views)[number];

/** The timeframe codes of the engine and the two sides: fixed lists (P8-08 decision 7). */
export const timeframes = ['1m', '5m', '10m', '15m', '30m', '1h', '1D', '1W', '1M'] as const;
export const sides = ['BUY', 'SELL'] as const;

/** The filters of a report, as the address carries them. `null` = not set. Anything malformed was dropped on reading. */
export interface ReportFilter {
  from: string | null;
  to: string | null;
  /** Basket ids. */
  basket: readonly string[];
  side: string | null;
  style: string | null;
  exit: string | null;
  strategy: string | null;
  timeframe: string | null;
  /** Upper-case symbols. */
  symbol: readonly string[];
  /** Run keys (64 hex). */
  runs: readonly string[];
  basis: 'gross' | 'net';
}

/** The wire names of the filters, each a string the server reads. */
export type ReportQuery = Partial<
  Record<
    'runs' | 'basis' | 'strategy' | 'timeframe' | 'side' | 'style' | 'exit' | 'basket' | 'symbol' | 'from' | 'to',
    string
  >
>;

/** What every report panel receives: the request filters and the minimum sample (`null` until `what-works` has answered). */
export interface ReportPanelProps {
  query: ReportQuery;
  min: number | null;
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const runKey = /^[0-9a-f]{64}$/i;
const symbolText = /^[A-Za-z0-9&_.-]{1,32}$/;

const day = (value: string | null) => (value !== null && isRealDay(value) ? value : null);
// Free text (a strategy id, a style, an exit): short, one line, no control characters; the server judges the rest.
const label = (value: string | null) =>
  // eslint-disable-next-line no-control-regex
  value !== null && value.length <= 120 && !/[\u0000-\u001f]/.test(value) ? value : null;
const list = (value: string | null, ok: (item: string) => boolean): string[] =>
  (value ?? '')
    .split(',')
    .map((item) => item.trim())
    .filter((item) => item !== '' && ok(item));

/** Reads the filters from the text of each address parameter. */
export function readFilter(text: (name: string) => string | null): ReportFilter {
  const pick = <T extends string>(name: string, allowed: readonly T[]): T | null =>
    allowed.find((value) => value === text(name)) ?? null;
  return {
    from: day(text('from')),
    to: day(text('to')),
    basket: list(text('basket'), (id) => uuid.test(id)),
    side: pick('side', sides),
    style: label(text('style')),
    exit: label(text('exit')),
    strategy: label(text('strategy')),
    timeframe: pick('timeframe', timeframes),
    symbol: list(text('symbol'), (item) => symbolText.test(item)).map((item) => item.toUpperCase()),
    runs: list(text('runs'), (key) => runKey.test(key)),
    basis: text('basis') === 'net' ? 'net' : 'gross',
  };
}

/** The request parameters of a filter (`gross` is the server's default and is not sent). */
export function toQuery(filter: ReportFilter, basis: 'gross' | 'net' = filter.basis): ReportQuery {
  const query: ReportQuery = {};
  if (filter.runs.length > 0) query.runs = filter.runs.join(',');
  if (basis === 'net') query.basis = 'net';
  if (filter.strategy) query.strategy = filter.strategy;
  if (filter.timeframe) query.timeframe = filter.timeframe;
  if (filter.side) query.side = filter.side;
  if (filter.style) query.style = filter.style;
  if (filter.exit) query.exit = filter.exit;
  if (filter.basket.length > 0) query.basket = filter.basket.join(',');
  if (filter.symbol.length > 0) query.symbol = filter.symbol.join(',');
  if (filter.from) query.from = filter.from;
  if (filter.to) query.to = filter.to;
  return query;
}

/** A stable text of a query: the cache key. */
export const queryString = (query: ReportQuery): string =>
  Object.entries(query)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([name, value]) => `${name}=${value}`)
    .join('&');

/** The address patch that sets (or, with `null`, removes) a filter; lists are written comma separated. */
export type FilterPatch = Readonly<Record<string, string | null>>;
export const joined = (items: readonly string[]): string | null => (items.length > 0 ? items.join(',') : null);

export interface FilterChip {
  id: string;
  text: string;
  /** What removes it. */
  clear: FilterPatch;
}

/** One chip per active filter (the basket names come from the caller). */
export function activeChips(filter: ReportFilter, basketName: (id: string) => string): FilterChip[] {
  const chips: FilterChip[] = [];
  if (filter.from || filter.to)
    chips.push({
      id: 'range',
      text: `Dates ${filter.from ?? 'start'} to ${filter.to ?? 'end'}`,
      clear: { from: null, to: null },
    });
  if (filter.basket.length > 0)
    chips.push({ id: 'basket', text: `Baskets ${filter.basket.map(basketName).join(', ')}`, clear: { basket: null } });
  if (filter.strategy) chips.push({ id: 'strategy', text: `Strategy ${filter.strategy}`, clear: { strategy: null } });
  if (filter.timeframe)
    chips.push({ id: 'timeframe', text: `Timeframe ${filter.timeframe}`, clear: { timeframe: null } });
  if (filter.side) chips.push({ id: 'side', text: `Side ${filter.side}`, clear: { side: null } });
  if (filter.style) chips.push({ id: 'style', text: `Style ${filter.style}`, clear: { style: null } });
  if (filter.exit) chips.push({ id: 'exit', text: `Exit ${filter.exit}`, clear: { exit: null } });
  if (filter.symbol.length > 0)
    chips.push({ id: 'symbol', text: `Symbols ${filter.symbol.join(', ')}`, clear: { symbol: null } });
  if (filter.runs.length > 0)
    chips.push({ id: 'runs', text: `${String(filter.runs.length)} chosen runs`, clear: { runs: null } });
  return chips;
}
