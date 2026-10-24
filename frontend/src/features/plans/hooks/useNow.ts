import { useEffect, useState } from 'react';

/** The current time in ms, refreshed every `everyMs` (default one second) for a live countdown. */
export function useNow(everyMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => {
      setNow(Date.now());
    }, everyMs);
    return () => {
      clearInterval(id);
    };
  }, [everyMs]);
  return now;
}
