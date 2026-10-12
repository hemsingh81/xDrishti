import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { emptyFilters, parseFilters, toParams, type QualityFilters } from '../lib/filters';

type Patch = Partial<QualityFilters>;

/**
 * The table state (search, status, sort) plus the open instrument, kept in the URL. Typing replaces the history entry,
 * every other change adds one so Back steps through views.
 */
export function useQualityFilters() {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => parseFilters(params), [params]);
  const openId = params.get('id');

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

export type QualityFilterState = ReturnType<typeof useQualityFilters>;
