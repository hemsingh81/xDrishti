import type { BrokerHistoryStatus, BrokerKindStatus } from '@/shared/api';
import { formatDate, formatNumber } from '@/shared/lib/format';
import type { RunTone } from './syncText';

export type BrokerKind = 'trades' | 'ledger';

const names: Record<BrokerKind, string> = { trades: 'Trades', ledger: 'Ledger' };

export interface KindSummary {
  /** `idle`: nothing has been read yet (no run to colour). */
  tone: RunTone | 'idle';
  /** "Trades read up to 11 Oct 2026", "Reading trades…", "Trades read failed". */
  title: string;
  /** What follows the time: the new count, the progress, or the reason in plain words. */
  detail: string;
  /** When the run ended (or began, while it runs); null when none. */
  at: string | null;
}

const count = (n: number, one: string, many: string): string => `${formatNumber(n)} ${n === 1 ? one : many}`;

/** The progress of a running read: "3 of 12 windows done", or "starting" before the first window is known. */
function progress(status: BrokerKindStatus): string {
  const done = status.windowsDone || (status.lastRun?.counts.windowsDone ?? 0);
  const total = status.windowsTotal || (status.lastRun?.counts.windowsTotal ?? 0);
  if (total <= 0) return 'starting';
  return `${formatNumber(done)} of ${formatNumber(total)} windows done`;
}

/** One kind (trades or ledger) as a line: how far it is read, what the latest run found (new, completed), and why it failed. */
export function summariseKind(kind: BrokerKind, status: BrokerKindStatus): KindSummary {
  const name = names[kind];
  const run = status.lastRun;
  const readTo = status.watermark === null ? null : formatDate(status.watermark);
  if (run?.status === 'running') {
    return { tone: 'info', title: `Reading ${name.toLowerCase()}…`, detail: progress(status), at: run.startedAt };
  }
  if (!run) {
    return readTo
      ? { tone: 'success', title: `${name} read up to ${readTo}`, detail: '', at: null }
      : { tone: 'idle', title: `${name} not read yet`, detail: '', at: null };
  }
  const at = run.finishedAt ?? run.startedAt;
  const upTo = readTo ? ` Read up to ${readTo}.` : '';
  if (run.status === 'failed') {
    return { tone: 'error', title: `${name} read failed`, detail: `${run.error ?? 'Nothing was changed.'}${upTo}`, at };
  }
  if (run.status === 'skipped') {
    return {
      tone: 'warning',
      title: `${name} read skipped`,
      detail: `${run.error ?? 'The scheduled read did not run.'}${upTo}`,
      at,
    };
  }
  const stored = run.counts.stored ?? 0;
  const noData = run.counts.emptyByNoData ?? 0;
  // Dhan's "no data" answer can also mean a wrong request (ADR 0025). One per window is what a quiet day looks like;
  // more than that means windows were halved and still empty, which is worth saying.
  const noDataNote =
    noData > (run.counts.windowsDone ?? 0)
      ? ` · Dhan answered "no data" for ${count(noData, 'date range', 'date ranges')}`
      : '';
  // Trades stored by an earlier read that this read completed (ISIN, charges, ticker): not new, but not "nothing" either.
  const enriched = run.counts.enriched ?? 0;
  const enrichedNote = enriched > 0 ? ` · ${count(enriched, 'trade', 'trades')} completed` : '';
  return {
    tone: 'success',
    title: `${name} read up to ${readTo ?? formatDate(run.to)}`,
    detail: `${stored > 0 ? `${formatNumber(stored)} new` : 'nothing new'}${enrichedNote}${noDataNote}`,
    at,
  };
}

/** "2 holdings rebuilt, 1 needs review"; null when the last trades run touched none. */
export function rebuildText(status: BrokerHistoryStatus): string | null {
  const { holdingsRebuilt: rebuilt, holdingsNeedingReview: review } = status;
  if (rebuilt <= 0 && review <= 0) return null;
  return `${count(rebuilt, 'holding', 'holdings')} rebuilt, ${formatNumber(review)} ${review === 1 ? 'needs' : 'need'} review`;
}

/**
 * The tickers the last successful trades run left alone for review (a lot dated by hand); the API lists the first 50 only.
 * They come from the same run as the counts, so a skipped or failed night does not hide them.
 */
export const needsReviewSymbols = (status: BrokerHistoryStatus): string[] => status.needsReviewSymbols;

/** Whether the last successful trades run left this holding's lots alone because one of them is dated by hand. */
export const needsReview = (status: BrokerHistoryStatus, symbol: string): boolean =>
  needsReviewSymbols(status).includes(symbol);
