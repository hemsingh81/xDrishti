import type { ServiceRun, ServiceView } from '@/shared/api';
import { formatIstDayMonth, formatIstTime, istDayIso } from '@/shared/lib/format';

/** The id of the service that quotes the holdings. */
export const quotesServiceId = 'holdings-quotes';

/** Used when the service's schedule does not say how often it runs. */
export const defaultQuoteMinutes = 5;

/** A quote is "fresh" while it is at most this many intervals old. */
const freshIntervals = 2;

const minuteMs = 60_000;

export type PricesKind = 'none' | 'fresh' | 'delayed' | 'asOf' | 'off';

export interface PricesDescription {
  kind: PricesKind;
  /** The line; for `off` the reason sentence ("Quotes off — …"), with the age of the prices in `asOf`. */
  text: string;
  /** For `off`: when the prices are from, e.g. "Prices as of 10:05 IST". */
  asOf?: string;
}

export interface PricesInput {
  /** The newest price time of the open holdings (ISO); null when none has one. */
  newest: string | null;
  /** The moment the line is for (ms). */
  now: number;
  marketOpen: boolean;
  /** The quote service as the list shows it; null while unknown. */
  quotes: ServiceView | null;
}

/** How often the service quotes (minutes): its interval schedule, else the default. */
export function quoteMinutes(service: ServiceView | null): number {
  const every = service?.schedule.type === 'interval' ? service.schedule.everyMinutes : null;
  return every && every > 0 ? every : defaultQuoteMinutes;
}

/** Why quotes are not coming in, or null when the service is on and its last run did not fail. */
export function quotesOffReason(service: ServiceView | null): string | null {
  if (!service) return null;
  if (!service.enabled) return 'the service is paused';
  const run = service.lastRun;
  if (service.status === 'running' || !run) return null;
  const detail = run.message ? `: ${run.message}` : '';
  if (run.status === 'failed') return `the last run failed${detail}`;
  if (run.status === 'skipped') return `the last run was skipped${detail}`;
  return null;
}

/**
 * Was the newest price a quote? The summary does not say where a price came from, so it is told from the quote service's
 * last successful run: a quote is stamped with the moment it was fetched, inside that run. Anything else is a sync.
 */
export function isQuotedPrice(newest: string, run: ServiceRun | null): boolean {
  if (run?.status !== 'succeeded' || !run.finishedAt) return false;
  const at = Date.parse(newest);
  return at >= Date.parse(run.startedAt) - minuteMs && at <= Date.parse(run.finishedAt) + minuteMs;
}

/** The India day (yyyy-MM-dd) of a moment. */
const dayOf = (value: string | number): string => istDayIso(typeof value === 'string' ? new Date(value) : value);

/** Is a price time on an earlier India day than `now`? */
export const isEarlierDay = (priceAt: string, now: number): boolean => dayOf(priceAt) < dayOf(now);

/**
 * The line under the totals (acceptance criterion 17): how fresh the prices are and where they come from. Times are in
 * India time. Quotes that are off (paused, or the last run failed or was skipped) win over everything else, because that
 * is what the owner has to act on.
 */
export function describePrices({ newest, now, marketOpen, quotes }: PricesInput): PricesDescription {
  const reason = quotesOffReason(quotes);
  const quoted = newest !== null && isQuotedPrice(newest, quotes?.lastRun ?? null);
  const sameDay = newest !== null && !isEarlierDay(newest, now);
  const when = newest === null ? null : sameDay ? `${formatIstTime(newest)} IST` : formatIstDayMonth(newest);

  if (reason !== null) {
    return {
      kind: 'off',
      text: `Quotes off — ${reason}`,
      asOf: when === null ? undefined : `Prices as of ${when}`,
    };
  }
  if (newest === null) return { kind: 'none', text: 'No prices yet' };

  if (marketOpen && sameDay) {
    const minutes = quoteMinutes(quotes);
    const fresh = now - Date.parse(newest) <= freshIntervals * minutes * minuteMs;
    return fresh
      ? { kind: 'fresh', text: `Prices as of ${String(when)} · quotes every ${String(minutes)} min` }
      : { kind: 'delayed', text: `Prices delayed — last ${quoted ? 'quote' : 'price'} ${String(when)}` };
  }
  const source = quoted ? 'quote' : 'sync';
  return {
    kind: 'asOf',
    text: sameDay
      ? `As of ${formatIstDayMonth(newest)} ${formatIstTime(newest)} IST · from the last ${source}`
      : `As of ${formatIstDayMonth(newest)} · from the last ${source}`,
  };
}
