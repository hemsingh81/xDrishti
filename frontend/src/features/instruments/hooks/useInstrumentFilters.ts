import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { emptyFilters, parseFilters, toParams, type InstrumentFilters } from '../lib/filters';

type Patch = Partial<InstrumentFilters>;

/**
 * The page state (search, filters, sort, page, open instrument) kept in the URL. Changing a filter returns to page 1;
 * typing replaces the history entry, every other change adds one so Back steps through the views.
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
          const next = toParams({ ...current, page: 1, ...patch }, previous);
          return next;
        },
        { replace: options.replace ?? false },
      );
    },
    [setParams],
  );

  const clear = useCallback(() => {
    update({ ...emptyFilters, sort: filters.sort, pageSize: filters.pageSize });
  }, [update, filters.sort, filters.pageSize]);

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
