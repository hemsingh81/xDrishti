import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { isActionsView, type ActionsView } from '../lib/actions';

/**
 * The corporate actions list's status filter in the URL: `?actions=suggested|applied|ignored` (all when left out), so
 * that a link (the bell's) opens the suggestions and Back steps through the views. The list loads further rows as it is
 * scrolled, so there is no page; the other parameters of the screen are left alone.
 */
export function useActionsView() {
  const [params, setParams] = useSearchParams();
  const raw = params.get('actions');
  const view: ActionsView = isActionsView(raw) ? raw : 'all';

  const update = useCallback(
    (next: ActionsView, options: { replace?: boolean } = {}) => {
      setParams(
        (previous) => {
          const copy = new URLSearchParams(previous);
          if (next === 'all') copy.delete('actions');
          else copy.set('actions', next);
          return copy;
        },
        { replace: options.replace ?? false },
      );
    },
    [setParams],
  );

  return useMemo(() => ({ view, update }), [view, update]);
}

export type ActionsViewState = ReturnType<typeof useActionsView>;
