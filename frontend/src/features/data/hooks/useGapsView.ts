import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { isGapsView, type GapsView } from '../lib/gaps';

/**
 * The status filter of the Gaps tab in the URL: `?gaps=open|unfillable|filled|all` (open when left out), so that a link
 * (the bell's) opens the open gaps and Back steps through the views. The other parameters of the screen are left alone.
 */
export function useGapsView() {
  const [params, setParams] = useSearchParams();
  const raw = params.get('gaps');
  const view: GapsView = isGapsView(raw) ? raw : 'open';

  const update = useCallback(
    (next: GapsView, options: { replace?: boolean } = {}) => {
      setParams(
        (previous) => {
          const copy = new URLSearchParams(previous);
          copy.set('gaps', next);
          return copy;
        },
        { replace: options.replace ?? false },
      );
    },
    [setParams],
  );

  return useMemo(() => ({ view, update }), [view, update]);
}

export type GapsViewState = ReturnType<typeof useGapsView>;
