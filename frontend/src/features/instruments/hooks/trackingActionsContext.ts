import { createContext, useContext } from 'react';

export interface TrackingActions {
  /** A track or untrack call is running. */
  busy: boolean;
  /** Tracks the instruments; resolves true when it worked (a toast tells the owner either way). */
  track: (ids: readonly string[], label: string) => Promise<boolean>;
  /** Asks first, then untracks; resolves true when it was done. */
  untrack: (ids: readonly string[], label: string) => Promise<boolean>;
}

const unavailable = (): Promise<boolean> => Promise.resolve(false);

export const TrackingActionsContext = createContext<TrackingActions>({
  busy: false,
  track: unavailable,
  untrack: unavailable,
});

/** Track / untrack from anywhere under the Instruments page (row buttons, bulk bar, drawer). */
export function useTrackingActions(): TrackingActions {
  return useContext(TrackingActionsContext);
}
