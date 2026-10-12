import { useCallback } from 'react';
import { useSearchParams } from 'react-router';

const isoDay = /^\d{4}-\d{2}-\d{2}$/;

/** The page state kept in the URL: the selected basket (`?basket=<id>`) and the look-back date (`&asOf=yyyy-MM-dd`). */
export function useBasketParams() {
  const [params, setParams] = useSearchParams();
  const basketId = params.get('basket');
  const asOfParam = params.get('asOf');
  const asOf = asOfParam !== null && isoDay.test(asOfParam) ? asOfParam : null;

  const select = useCallback(
    (id: string | null, options: { replace?: boolean } = {}) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          if (id) next.set('basket', id);
          else next.delete('basket');
          return next;
        },
        { replace: options.replace ?? false },
      );
    },
    [setParams],
  );

  const setAsOf = useCallback(
    (value: string | null) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          if (value) next.set('asOf', value);
          else next.delete('asOf');
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  return { basketId, asOf, select, setAsOf };
}
