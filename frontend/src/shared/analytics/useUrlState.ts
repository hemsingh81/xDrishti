import { useCallback } from 'react';
import { useSearchParams } from 'react-router';

/**
 * View state that lives in the URL (tab, window, the open cell), so a link reproduces a view and Back works (P8-00 D4).
 * `choice` reads one of a fixed set of values and falls back to the default for anything else (a hand-edited or stale link
 * never breaks the page); `update` changes several parameters in one history entry and keeps the others. A `null` removes
 * a parameter, so the default is not written to the address.
 */
export function useUrlState() {
  const [params, setParams] = useSearchParams();

  const choice = <T extends string>(name: string, allowed: readonly T[], fallback: T): T => {
    const found = params.get(name);
    return allowed.find((value) => value === found) ?? fallback;
  };

  const text = (name: string): string | null => {
    const found = params.get(name);
    return found === null || found === '' ? null : found;
  };

  const update = useCallback(
    (patch: Readonly<Record<string, string | null>>) => {
      setParams((previous) => {
        const next = new URLSearchParams(previous);
        for (const [name, value] of Object.entries(patch)) {
          if (value === null) next.delete(name);
          else next.set(name, value);
        }
        return next;
      });
    },
    [setParams],
  );

  return { choice, text, update };
}
