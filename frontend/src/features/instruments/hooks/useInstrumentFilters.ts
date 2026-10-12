import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { emptyFilters, parseFilters, toParams, type InstrumentFilters } from '../lib/filters';

type Patch = Partial<InstrumentFilters>;

/**
 * The page state (search, filters, sort, open instrument) kept in the URL. Typing replaces the history entry, every other change adds one so Back steps through the views.
 */
export function useInstrumentFilters() {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => parseFilters(params), [params]);
  const openId = params.get('id');

  const update = useCallback(
    (patch: Patch, options: { replace?: boolean } = {}) => {
      setParams(
        (previous) => {
          const current = parseFilters(previous);
          return toParams({ ...current, ...patch }, previous);
        },
        { replace: options.replace ?? false },
      );
    },
    [setParams],
  );

  const clear = useCallback(() => {
    update({ ...emptyFilters, tab: filters.tab, sort: filters.sort });
  }, [update, filters.tab, filters.sort]);

  const open = useCallback(
    (id: string | null) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          if (id) next.set('id', id);
          else next.delete('id');
          return next;
        },
        { replace: id === null },
      );
    },
    [setParams],
  );

  return { filters, update, clear, openId, open };
}
