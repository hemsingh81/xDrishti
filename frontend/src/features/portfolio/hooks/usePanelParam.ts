import { useCallback } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router';

const pushedHere = (state: unknown, key: string): boolean =>
  typeof state === 'object' && state !== null && (state as Record<string, unknown>)[key] === true;

/**
 * A panel on top of the Portfolio screen that is addressed by one URL parameter (`?alerts=active`, `?trades=import`): its
 * value is the open state and the tab at once; null = closed. Other parameters (the filters, the open holding) are kept.
 *
 * Opening the panel from this screen adds a history entry and closing it goes back one step, so Back and Close agree (one
 * press each); a panel that was opened by a link (the bell, a bookmark) is closed by replacing its entry. The history
 * entry remembers that it added the panel (`<param>Pushed`), and a tab change keeps that mark.
 */
export function usePanelParam<T extends string>(param: string, parse: (value: string | null) => T | null) {
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const value = parse(params.get(param));
  const mark = `${param}Pushed`;
  const pushed = pushedHere(location.state, mark);

  const open = useCallback(
    (next: T) => {
      setParams(
        (previous) => {
          const copy = new URLSearchParams(previous);
          copy.set(param, next);
          return copy;
        },
        { state: { [mark]: true } },
      );
    },
    [setParams, param, mark],
  );

  const change = useCallback(
    (next: T) => {
      setParams(
        (previous) => {
          const copy = new URLSearchParams(previous);
          copy.set(param, next);
          return copy;
        },
        // A tab is a step within the panel, not a page of its own; the entry keeps its mark.
        { replace: true, state: location.state as unknown },
      );
    },
    [setParams, param, location.state],
  );

  const close = useCallback(() => {
    if (pushed) {
      void navigate(-1);
      return;
    }
    setParams(
      (previous) => {
        const copy = new URLSearchParams(previous);
        copy.delete(param);
        return copy;
      },
      { replace: true },
    );
  }, [pushed, navigate, setParams, param]);

  return { value, open, change, close };
}
