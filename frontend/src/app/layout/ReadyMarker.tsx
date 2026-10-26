import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

/** The spinner of `LoadingState` inside its status region: while it is on the page the route's chunk or data is still coming. */
const loadingSelector = '[role="status"] > [role="progressbar"]';
const settleMs = 150;

/**
 * Renders nothing visible until the route has settled (no query fetching and no loading state for a moment), then an empty
 * `data-ready` element: the browser runner and its performance figures wait for it instead of guessing from timers. It
 * subscribes to the query cache itself, so only this component re-renders, never the shell. Mount it with the route's key
 * so it starts over on every navigation.
 */
export function ReadyMarker() {
  const client = useQueryClient();
  const [ready, setReady] = useState(false);
  useEffect(() => {
    let timer = 0;
    const evaluate = () => {
      window.clearTimeout(timer);
      if (client.isFetching() > 0) {
        setReady(false);
        return;
      }
      timer = window.setTimeout(() => {
        if (client.isFetching() === 0 && document.querySelector(loadingSelector) === null) setReady(true);
        else evaluate();
      }, settleMs);
    };
    const unsubscribe = client.getQueryCache().subscribe(evaluate);
    evaluate();
    return () => {
      unsubscribe();
      window.clearTimeout(timer);
    };
  }, [client]);
  return ready ? <span hidden data-ready="" /> : null;
}
