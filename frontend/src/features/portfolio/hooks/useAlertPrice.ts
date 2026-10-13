import { useQuery } from '@tanstack/react-query';
import { heldInstrumentQuery, holdingLookupQuery, lastCloseQuery } from '../api/lookupQueries';
import type { AlertTarget } from '../lib/alertTarget';

export interface AlertPrice {
  /** The price the quick levels are computed from; null when none is known. */
  price: number | null;
  /** Where it comes from: the holding's last price, or the last stored daily close (`at` is that bar's time). */
  source: 'holding' | 'close' | null;
  at: string | null;
  /** A lookup is on its way. */
  loading: boolean;
}

const nobody = { id: '', accountId: '', symbol: '' };

/**
 * The last price of the instrument an alert is being set for: the holding's own (known from its row, else looked up in its
 * account; for a picked instrument, looked up among the holdings of that instrument), else the last stored daily close.
 * Nothing is known for an instrument that is not held and has no history; the API still refuses a level that already
 * holds when it knows a price.
 */
export function useAlertPrice(target: AlertTarget | null, instrumentId: string, symbol: string): AlertPrice {
  const known = target?.lastPrice ?? null;
  const lookupHolding = target?.holding !== undefined && target.lastPrice === undefined;
  const holding = useQuery({ ...holdingLookupQuery(target?.holding ?? nobody), enabled: lookupHolding });
  // A picked instrument (or a target without a holding): is it held?
  const lookupHeld = !lookupHolding && known === null && instrumentId !== '' && symbol !== '';
  const held = useQuery({ ...heldInstrumentQuery({ id: instrumentId, symbol }), enabled: lookupHeld });
  const own = known ?? (holding.data?.priced ? holding.data.lastPrice : null) ?? held.data?.lastPrice ?? null;
  const holdingSettled = (!lookupHolding || !holding.isPending) && (!lookupHeld || !held.isPending);
  const close = useQuery({
    ...lastCloseQuery(instrumentId),
    enabled: instrumentId !== '' && own === null && holdingSettled,
  });

  if (own !== null) return { price: own, source: 'holding', at: null, loading: false };
  if (close.data) return { price: close.data.price, source: 'close', at: close.data.at, loading: false };
  return { price: null, source: null, at: null, loading: !holdingSettled || close.isLoading };
}
