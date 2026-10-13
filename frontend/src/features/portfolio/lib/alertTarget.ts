import type { ActionItem, HoldingRow } from '@/shared/api';
import type { HoldingRef } from '../api/lookupQueries';

/** The instrument an alert is set for, with what is known about its price. */
export interface AlertTarget {
  instrumentId: string;
  symbol: string;
  name: string | null;
  /** The holding it is set from: its last price is looked up for the hint when `lastPrice` is not known. */
  holding?: HoldingRef;
  /** The last price when it is at hand (the holding's row); null/undefined = look it up. */
  lastPrice?: number | null;
}

/** The target of an Action-center item; null for an item that is not about an instrument (no alert makes sense). */
export function alertTargetOfItem(item: ActionItem): AlertTarget | null {
  if (!item.instrumentId || !item.symbol) return null;
  return {
    instrumentId: item.instrumentId,
    symbol: item.symbol,
    name: null,
    holding: item.holdingId ? { id: item.holdingId, accountId: item.accountId, symbol: item.symbol } : undefined,
  };
}

/** The target of a holding row; null when the holding is not in the master list (alerts are for listed shares and ETFs). */
export function alertTargetOfHolding(holding: HoldingRow): AlertTarget | null {
  if (!holding.instrumentId) return null;
  return {
    instrumentId: holding.instrumentId,
    symbol: holding.symbol,
    name: holding.name,
    holding: { id: holding.id, accountId: holding.accountId, symbol: holding.symbol },
    lastPrice: holding.priced ? holding.lastPrice : null,
  };
}
