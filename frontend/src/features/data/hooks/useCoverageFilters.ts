import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { emptyFilters, parseFilters, toParams, type CoverageFilters } from '../lib/filters';
import { defaultTimeframe, isTimeframe, type Timeframe } from '../lib/timeframes';

type Patch = Partial<CoverageFilters>;

/**
 * The table state (search, status, sort, page) plus the open instrument and its timeframe, kept in the URL. Changing a
 * filter returns to page 1; typing replaces the history entry, every other change adds one so Back steps through views.
 */
export function useCoverageFilters() {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => parseFilters(params), [params]);
  const openId = params.get('id');
  const tf = params.get('tf');
  const timeframe: Timeframe = isTimeframe(tf) ? tf : defaultTimeframe;

  const update = useCallback(
    (patch: Patch, options: { replace?: boolean } = {}) => {
      setParams((previous) => toParams({ ...parseFilters(previous), page: 1, ...patch }, previous), {
        replace: options.replace ?? false,
      });
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
          else {
            next.delete('id');
            next.delete('tf');
          }
          return next;
        },
        { replace: id === null },
      );
    },
    [setParams],
  );

  const setTimeframe = useCallback(
    (value: Timeframe) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          if (value === defaultTimeframe) next.delete('tf');
          else next.set('tf', value);
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  return { filters, update, clear, openId, open, timeframe, setTimeframe };
}
