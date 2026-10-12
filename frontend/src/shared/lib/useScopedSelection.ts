import { useCallback, useState } from 'react';

const none: ReadonlySet<string> = new Set();

/**
 * The selected row ids of a list. They are kept across pages and sort orders; `scope` identifies the list they were
 * picked from (tab, filters, basket…), and a different scope starts with nothing selected, so hidden rows are never
 * acted on.
 */
export function useScopedSelection(scope: string) {
  const [state, setState] = useState<{ scope: string; ids: ReadonlySet<string> }>({ scope, ids: none });
  const ids = state.scope === scope ? state.ids : none;
  const set = useCallback(
    (next: ReadonlySet<string>) => {
      setState({ scope, ids: next });
    },
    [scope],
  );
  const clear = useCallback(() => {
    setState({ scope, ids: none });
  }, [scope]);
  return { ids, set, clear };
}
