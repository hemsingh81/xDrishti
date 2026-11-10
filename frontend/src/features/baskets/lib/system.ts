import type { Basket } from '@/shared/api';
import type { BasketSource, BasketVersionSummary } from '@/shared/api/systemBasketTypes';
import { formatDate, formatIstDate } from '@/shared/lib/format';

/** A portfolio bucket: computed from holdings. Every other system basket (index, list, derived) is stored (BK-01). */
export const isPortfolio = (basket: Basket): boolean => basket.kind === 'portfolio';
export const isStored = (basket: Basket): boolean => basket.system && !isPortfolio(basket);

export const storedKinds = ['index', 'derived', 'list'] as const;
export type StoredKind = (typeof storedKinds)[number];
/** The kind filter of the screen: all, one kind of system basket, or the owner's own (`?kind=`). */
export type KindFilter = 'all' | StoredKind | 'yours';

const kinds: readonly string[] = ['all', ...storedKinds, 'yours'];
export const parseKind = (value: string | null): KindFilter =>
  value !== null && kinds.includes(value) ? (value as KindFilter) : 'all';

export const inSampleWarning = 'Ranked on past bars; do not test a strategy on the same window it was ranked on';
export const systemMoveTip = 'System baskets cannot be changed';

const day = (value: string | null | undefined) => (value ? formatIstDate(value) : 'never');

/** AC 2 and 3: the two dates of an index / list basket, or the as-of and effective dates of a derived basket's version. */
export function asOfLine(basket: Basket, version: BasketVersionSummary | null): string | null {
  if (basket.kind === 'derived') {
    return version
      ? `Members as of ${formatDate(version.asOf)} (bars to that close) · effective from ${formatDate(version.effectiveFrom)}`
      : null;
  }
  const lastOk = basket.source?.lastOkAt;
  if (!lastOk) return null;
  const since = basket.membersKnownSince ? ` · known since ${formatDate(basket.membersKnownSince)}` : '';
  return `Members as of ${formatIstDate(lastOk)}${since}; earlier dates use these members (ISS-22)`;
}

export type SourceTone = 'ok' | 'warning' | 'error' | 'neutral';

export interface SourceStatusText {
  tone: SourceTone;
  text: string;
}

/** The newest fetch that was rejected or failed: why the source is in that state. */
export const failureReason = (source: BasketSource | undefined): string | null =>
  source?.fetches.find((f) => f.outcome === 'rejected' || f.outcome === 'failed')?.error ?? null;

/** AC 4 and 5: what the status line says for each state of a basket's source. */
export function sourceStatus(basket: Basket, reason: string | null): SourceStatusText | null {
  const source = basket.source;
  if (!source) return null;
  switch (source.state) {
    case 'ok':
      return { tone: 'ok', text: `Up to date: checked ${day(source.lastCheckedAt ?? source.lastOkAt)}` };
    case 'stale':
      return {
        tone: 'warning',
        text: `Stale: last checked ${day(source.lastCheckedAt)}, last updated ${day(source.lastOkAt)}`,
      };
    case 'rejected':
    case 'failed':
      return {
        tone: 'error',
        text: `${source.state === 'failed' ? 'Failed' : 'Rejected'}: ${reason ?? 'no reason was recorded'}. The last good members are shown`,
      };
    default:
      return { tone: 'neutral', text: 'Not fetched yet: fetching is off' };
  }
}

/** A parameter hash in its short form. */
export const shortHash = (hash: string): string => hash.slice(0, 8);
