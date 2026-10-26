import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { visuallyHidden } from '@mui/utils';
import { Box } from '@mui/material';
import { useEffect, useState } from 'react';

/** The spinner of `LoadingState` inside its status region: while it is on the page, something is still loading. */
const spinner = '[role="status"] > [role="progressbar"]';
const tickMs = 250;
/** Consecutive ticks (about half a second) a state must hold before it is announced, so a short gap is not a change. */
const holdTicks = 2;

const loadingNow = (client: QueryClient) =>
  client
    .getQueryCache()
    .findAll({ fetchStatus: 'fetching' })
    .some((query) => query.state.data === undefined) || document.querySelector(spinner) !== null;

/**
 * One polite status for the whole page: "Loading…" once when the first data of a screen is on its way, "Loaded" when it
 * has arrived. The per-panel `LoadingState` regions are silent (they only show the words), so a screen with twelve panels
 * announces twice, not twelve times (C15). It polls only while something loads; idle, it holds no timer.
 */
export function LoadingAnnouncer() {
  const client = useQueryClient();
  const [text, setText] = useState('');
  useEffect(() => {
    let timer = 0;
    let pending = false;
    let announced = false;
    let streak = 0;
    const tick = () => {
      pending = false;
      const loading = loadingNow(client);
      // `streak` counts ticks the page has disagreed with what was announced.
      streak = loading !== announced ? streak + 1 : 0;
      if (streak >= holdTicks) {
        announced = loading;
        streak = 0;
        setText(loading ? 'Loading…' : 'Loaded');
      }
      if (loading || streak > 0) {
        pending = true;
        timer = window.setTimeout(tick, tickMs);
      }
    };
    const unsubscribe = client.getQueryCache().subscribe(() => {
      if (!pending) tick();
    });
    tick();
    return () => {
      unsubscribe();
      window.clearTimeout(timer);
    };
  }, [client]);
  return (
    <Box role="status" aria-live="polite" sx={visuallyHidden}>
      {text}
    </Box>
  );
}
