import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { emptyFilters, parseFilters, toParams, type PortfolioFilters } from '../lib/filters';

type Patch = Partial<PortfolioFilters>;

/**
 * The page state (account, bucket, status, search, sort, open holding) kept in the URL. Typing replaces the history entry, every other change adds one so Back steps through the views.
 */
export function usePortfolioFilters() {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => parseFilters(params), [params]);
  const openId = params.get('holding');

  const update = useCallback(
    (patch: Patch, options: { replace?: boolean } = {}) => {
      setParams((previous) => toParams({ ...parseFilters(previous), ...patch }, previous), {
        replace: options.replace ?? false,
      });
    },
    [setParams],
  );

  const clear = useCallback(() => {
    update({ ...emptyFilters, account: filters.account, sort: filters.sort });
  }, [update, filters.account, filters.sort]);

  const open = useCallback(
    (id: string | null) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          if (id) next.set('holding', id);
          else next.delete('holding');
          return next;
        },
        { replace: id === null },
      );
    },
    [setParams],
  );

  return { filters, update, clear, openId, open };
}
