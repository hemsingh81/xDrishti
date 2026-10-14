import type { HoldingRow } from '@/shared/api';
import { isEarlierDay } from './prices';

/**
 * Why a holding has no live price while the screen is live: no instrument (so nothing to follow), or no tick yet today (it
 * has not traded, is beyond the cap, or is not on the feed). Null when its price is a tick of today.
 */
export function notLiveReason(holding: HoldingRow, now: number): string | null {
  if (holding.status !== 'open') return null;
  if (holding.instrumentId === null) {
    return 'Not in the master list, so it has no live feed: the price is from the last sync.';
  }
  if (holding.priceSource === 'live' && holding.priceAt !== null && !isEarlierDay(holding.priceAt, now)) return null;
  const from = holding.priceSource === 'live' ? 'tick' : holding.priceSource;
  return `No live tick today: the price is from the last ${from}.`;
}
