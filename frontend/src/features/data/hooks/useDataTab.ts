import { useCallback, useEffect, useMemo } from 'react';
import { useLocation, useSearchParams } from 'react-router';
import type { ActionsView } from '../lib/actions';
import type { GapsView } from '../lib/gaps';
import { defaultDataTab, resolveTab, type DataTab } from '../lib/tabs';

/** Where a link inside the screen lands: the tab, and the filter of the list it opens on. */
export interface TabTarget {
  tab: DataTab;
  gaps?: GapsView;
  actions?: ActionsView;
}

/**
 * The open tab in the URL (`?tab=calendar|actions|gaps|runs`; coverage when left out), so that a view can be shared and
 * Back steps through the tabs. Links made before the tabs existed (`?gaps=open`, `?section=actions&actions=suggested`,
 * `#corporate-actions`) resolve to their tab (see `resolveTab`). The filters of the tabs are left alone.
 */
export function useDataTab() {
  const [params, setParams] = useSearchParams();
  const { hash } = useLocation();
  const tab = resolveTab(params, hash);

  // An old link (`?gaps=open`) shows its tab at once; the tab is then written to the URL, so that clearing the filter
  // that led there (`?gaps=` gone) does not change the tab.
  const named = params.get('tab') !== null;
  useEffect(() => {
    if (named || tab === defaultDataTab) return;
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.set('tab', tab);
        return next;
      },
      { replace: true },
    );
  }, [named, tab, setParams]);

  const open = useCallback(
    ({ tab: next, gaps, actions }: TabTarget, options: { replace?: boolean } = {}) => {
      setParams(
        (previous) => {
          const copy = new URLSearchParams(previous);
          copy.delete('section');
          // Coverage is the default, but an old `?gaps=` or `?actions=` would still point elsewhere: then it is written.
          const keepsGaps = gaps !== undefined || copy.has('gaps');
          const keepsActions = actions === undefined ? copy.has('actions') : actions !== 'all';
          if (next === 'coverage' && !keepsGaps && !keepsActions && hash === '') copy.delete('tab');
          else copy.set('tab', next);
          if (gaps) copy.set('gaps', gaps);
          if (actions === 'all') copy.delete('actions');
          else if (actions) copy.set('actions', actions);
          return copy;
        },
        { replace: options.replace ?? false },
      );
    },
    [setParams, hash],
  );

  return useMemo(() => ({ tab, open }), [tab, open]);
}

export type DataTabState = ReturnType<typeof useDataTab>;
