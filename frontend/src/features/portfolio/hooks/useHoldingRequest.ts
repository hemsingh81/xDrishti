import { useCallback } from 'react';
import { useSearchParams } from 'react-router';

/** What a link asked of the open holding: its Trades section, and the rebuild preview on top of it. */
export interface HoldingRequest {
  /** `?rebuild=preview`: open the rebuild preview ("Rebuild lots") straight away. */
  rebuild: boolean;
  /** The request was carried out: take it out of the URL (replace, so Back does not repeat it). */
  onHandled: () => void;
}

/**
 * `?holding=…&section=trades` (the bell's "Open trades") opens the holding on its Trades section; adding `&rebuild=preview`
 * ("Review") opens the rebuild preview there too. Null when the URL asks for neither. The Trades section removes both
 * parameters once it has scrolled to its title, so the holding can be closed and the link used again.
 */
export function useHoldingRequest(): HoldingRequest | null {
  const [params, setParams] = useSearchParams();
  const section = params.get('section') === 'trades';
  const rebuild = params.get('rebuild') === 'preview';
  const onHandled = useCallback(() => {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete('section');
        next.delete('rebuild');
        return next;
      },
      { replace: true },
    );
  }, [setParams]);
  return section || rebuild ? { rebuild, onHandled } : null;
}
