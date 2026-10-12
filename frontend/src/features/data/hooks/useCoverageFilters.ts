import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { emptyFilters, parseFilters, toParams, type CoverageFilters } from '../lib/filters';
import { defaultTimeframe, isTimeframe, type Timeframe } from '../lib/timeframes';

type Patch = Partial<CoverageFilters>;

/**
 * The list state (search, status, has gaps, sort) plus the open instrument, its timeframe and whether prices are adjusted, kept in the URL.
 * Typing replaces the history entry, every other change adds one so Back steps through views. There is no page: the list
 * loads further rows as it is scrolled.
 */
export function useCoverageFilters() {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => parseFilters(params), [params]);
  const openId = params.get('id');
  const tf = params.get('tf');
  const timeframe: Timeframe = isTimeframe(tf) ? tf : defaultTimeframe;
  const adjusted = params.get('adjusted') === 'true';

  const update = useCallback(
    (patch: Patch, options: { replace?: boolean } = {}) => {
      setParams((previous) => toParams({ ...parseFilters(previous), ...patch }, previous), {
        replace: options.replace ?? false,
      });
    },
    [setParams],
  );

  const clear = useCallback(() => {
    update({ ...emptyFilters, sort: filters.sort });
  }, [update, filters.sort]);

  const open = useCallback(
    (id: string | null) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          if (id) next.set('id', id);
          else {
            next.delete('id');
            next.delete('tf');
            next.delete('adjusted');
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

  const setAdjusted = useCallback(
    (value: boolean) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          if (value) next.set('adjusted', 'true');
          else next.delete('adjusted');
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  return { filters, update, clear, openId, open, timeframe, setTimeframe, adjusted, setAdjusted };
}
